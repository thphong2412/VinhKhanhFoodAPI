using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using VinhKhanh.API.Data;
using VinhKhanh.API.Models;
using VinhKhanh.Shared;

namespace VinhKhanh.API.Controllers
{
    [Route("api/localizations")]
    [ApiController]
    public class LocalizationController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IWebHostEnvironment _env;
        private readonly IHttpClientFactory _httpFactory;
        private static readonly ConcurrentDictionary<string, LocalizationWarmupStatusDto> _warmups = new();
        private static readonly string[] _blockedKeywords = new[] { "lừa đảo", "giả mạo", "đánh bạc", "ma túy" };
        private static readonly HashSet<string> SupportedAutoTranslateLanguages = new(StringComparer.OrdinalIgnoreCase)
        {
            "en", "ja", "ko", "zh", "ru", "th", "es", "fr", "it"
        };
        private const long MaxTtsCacheBytes = 750L * 1024 * 1024;
        private const int MaxTtsCacheFiles = 8000;
        private const int TtsCleanupBatchDelete = 400;

        public LocalizationController(AppDbContext db, IServiceScopeFactory scopeFactory, IWebHostEnvironment env, IHttpClientFactory httpFactory)
        {
            _db = db;
            _scopeFactory = scopeFactory;
            _env = env;
            _httpFactory = httpFactory;
        }

        [HttpPost("prepare-hotset")]
        public async Task<IActionResult> PrepareHotset([FromBody] LocalizationPrepareRequest req)
        {
            if (!HasPermission("localization.prepare")) return Forbid();

            if (req == null || req.PoiIds == null || req.PoiIds.Count == 0)
                return BadRequest("poi_ids required");

            var lang = string.IsNullOrWhiteSpace(req.Lang) ? "en" : NormalizeLanguageCode(req.Lang);
            var ids = req.PoiIds.Distinct().Take(50).ToList();

            var all = await _db.PointContents
                .Where(c => ids.Contains(c.PoiId))
                .ToListAsync();

            var items = new List<ContentModel>();
            int ready = 0;
            int pending = 0;

            foreach (var poiId in ids)
            {
                var localized = all.FirstOrDefault(c => c.PoiId == poiId && c.LanguageCode == lang);
                if (localized != null)
                {
                    ready++;
                    items.Add(localized);
                    await LogLocalizationJobAsync(poiId, lang, "hotset", "completed", "cached");
                    continue;
                }

                var en = all.FirstOrDefault(c => c.PoiId == poiId && c.LanguageCode == "en");
                if (en != null)
                {
                    pending++;
                    items.Add(en);
                    await LogLocalizationJobAsync(poiId, lang, "hotset", "completed", "fallback_en");
                    continue;
                }

                var vi = all.FirstOrDefault(c => c.PoiId == poiId && c.LanguageCode == "vi");
                if (vi != null)
                {
                    pending++;
                    items.Add(vi);
                    await LogLocalizationJobAsync(poiId, lang, "hotset", "completed", "fallback_vi");
                }
            }

            return Ok(new LocalizationPrepareResult
            {
                ReadyCount = ready,
                PendingCount = pending,
                Items = items
            });
        }

        [HttpPost("on-demand")]
        public async Task<IActionResult> OnDemand([FromBody] LocalizationOnDemandRequest req)
        {
            if (!HasPermission("localization.on_demand")) return Forbid();

            if (req == null || req.PoiId <= 0) return BadRequest("poi_id required");

            var lang = string.IsNullOrWhiteSpace(req.Lang) ? "en" : NormalizeLanguageCode(req.Lang);
            var existing = await _db.PointContents.FirstOrDefaultAsync(c => c.PoiId == req.PoiId && c.LanguageCode == lang);
            if (existing != null)
            {
                await LogLocalizationJobAsync(req.PoiId, lang, "on_demand", "completed", "cached");
                return Ok(new LocalizationOnDemandResult { Status = "cached", Localization = existing });
            }

            var fallback = await _db.PointContents.FirstOrDefaultAsync(c => c.PoiId == req.PoiId && c.LanguageCode == "en")
                          ?? await _db.PointContents.FirstOrDefaultAsync(c => c.PoiId == req.PoiId && c.LanguageCode == "vi");

            if (fallback == null) return NotFound("content_not_found");

            var blocked = _blockedKeywords.Any(k => (fallback.Description ?? string.Empty).Contains(k, StringComparison.OrdinalIgnoreCase));
            if (blocked)
            {
                await LogLocalizationJobAsync(req.PoiId, lang, "on_demand", "blocked", "moderation_blocked");
                return BadRequest("content_blocked_by_policy");
            }

            var generated = new ContentModel
            {
                PoiId = fallback.PoiId,
                LanguageCode = lang,
                Title = await TranslateTextFreeAsync(fallback.Title, lang),
                Subtitle = await TranslateTextFreeAsync(fallback.Subtitle, lang),
                Description = await TranslateTextFreeAsync(fallback.Description, lang),
                AudioUrl = $"/api/audio/tts?lang={Uri.EscapeDataString(lang)}&text={Uri.EscapeDataString((fallback.Description ?? fallback.Title ?? string.Empty).Length > 400 ? (fallback.Description ?? fallback.Title ?? string.Empty)[..400] : (fallback.Description ?? fallback.Title ?? string.Empty))}",
                IsTTS = true,
                PriceRange = fallback.PriceRange,
                Rating = fallback.Rating,
                OpeningHours = fallback.OpeningHours,
                PhoneNumber = fallback.PhoneNumber,
                Address = await TranslateTextFreeAsync(fallback.Address, lang),
                ShareUrl = fallback.ShareUrl
            };

            _db.PointContents.Add(generated);
            await _db.SaveChangesAsync();
            await LogLocalizationJobAsync(req.PoiId, lang, "on_demand", "completed", "generated");
            TryCleanupTtsCache();

            return Ok(new LocalizationOnDemandResult { Status = "generated", Localization = generated });
        }

        [HttpPost("warmup")]
        public async Task<IActionResult> Warmup([FromBody] LocalizationWarmupRequest req)
        {
            if (!HasPermission("localization.warmup")) return Forbid();

            var lang = string.IsNullOrWhiteSpace(req?.Lang) ? "en" : NormalizeLanguageCode(req.Lang);
            var key = lang;

            if (_warmups.TryGetValue(key, out var running) && running.Status == "running")
            {
                return Ok(running);
            }

            var pois = await _db.PointsOfInterest.Where(p => p.IsPublished).Select(p => p.Id).ToListAsync();
            var status = new LocalizationWarmupStatusDto
            {
                Lang = lang,
                Status = "running",
                TotalPois = pois.Count,
                Ready = 0,
                Failed = 0,
                Progress = 0,
                StartedAtUtc = DateTime.UtcNow,
                LastMessage = "warmup_started"
            };
            _warmups[key] = status;

            _ = Task.Run(async () =>
            {
                var startedAt = DateTime.UtcNow;
                int ready = 0;
                int failed = 0;

                foreach (var poiId in pois)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                        var exists = await scopedDb.PointContents.AnyAsync(c => c.PoiId == poiId && c.LanguageCode == lang);
                        if (!exists)
                        {
                            var source = await scopedDb.PointContents.FirstOrDefaultAsync(c => c.PoiId == poiId && c.LanguageCode == "en")
                                         ?? await scopedDb.PointContents.FirstOrDefaultAsync(c => c.PoiId == poiId && c.LanguageCode == "vi");
                            if (source != null)
                            {
                                var blocked = _blockedKeywords.Any(k => (source.Description ?? string.Empty).Contains(k, StringComparison.OrdinalIgnoreCase));
                                if (blocked)
                                {
                                    await LogLocalizationJobAsync(poiId, lang, "warmup", "blocked", "moderation_blocked");
                                    ready++;
                                    continue;
                                }

                                scopedDb.PointContents.Add(new ContentModel
                                {
                                    PoiId = source.PoiId,
                                    LanguageCode = lang,
                                    Title = await TranslateTextFreeAsync(source.Title, lang),
                                    Subtitle = await TranslateTextFreeAsync(source.Subtitle, lang),
                                    Description = await TranslateTextFreeAsync(source.Description, lang),
                                    AudioUrl = $"/api/audio/tts?lang={Uri.EscapeDataString(lang)}&text={Uri.EscapeDataString((source.Description ?? source.Title ?? string.Empty).Length > 400 ? (source.Description ?? source.Title ?? string.Empty)[..400] : (source.Description ?? source.Title ?? string.Empty))}",
                                    IsTTS = true,
                                    PriceRange = source.PriceRange,
                                    Rating = source.Rating,
                                    OpeningHours = source.OpeningHours,
                                    PhoneNumber = source.PhoneNumber,
                                    Address = await TranslateTextFreeAsync(source.Address, lang),
                                    ShareUrl = source.ShareUrl
                                });
                                await scopedDb.SaveChangesAsync();
                                await LogLocalizationJobAsync(poiId, lang, "warmup", "completed", "generated");
                            }
                        }

                        ready++;
                        _warmups[key] = new LocalizationWarmupStatusDto
                        {
                            Lang = lang,
                            Status = "running",
                            TotalPois = pois.Count,
                            Ready = ready,
                            Failed = failed,
                            Progress = pois.Count == 0 ? 1 : (double)(ready + failed) / pois.Count,
                            StartedAtUtc = startedAt,
                            LastMessage = $"processed_poi_{poiId}"
                        };
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        await LogLocalizationJobAsync(poiId, lang, "warmup", "failed", ex.Message);

                        _warmups[key] = new LocalizationWarmupStatusDto
                        {
                            Lang = lang,
                            Status = "running",
                            TotalPois = pois.Count,
                            Ready = ready,
                            Failed = failed,
                            Progress = pois.Count == 0 ? 1 : (double)(ready + failed) / pois.Count,
                            StartedAtUtc = startedAt,
                            LastMessage = $"failed_poi_{poiId}"
                        };
                    }
                }

                _warmups[key] = new LocalizationWarmupStatusDto
                {
                    Lang = lang,
                    Status = failed > 0 ? "completed_with_failures" : "completed",
                    TotalPois = pois.Count,
                    Ready = ready,
                    Failed = failed,
                    Progress = 1,
                    StartedAtUtc = startedAt,
                    CompletedAtUtc = DateTime.UtcNow,
                    LastMessage = failed > 0 ? "warmup_completed_with_failures" : "warmup_completed"
                };

                TryCleanupTtsCache();
            });

            return Ok(status);
        }

        [HttpGet("warmup/{lang}/status")]
        public IActionResult WarmupStatus(string lang)
        {
            if (!HasPermission("localization.status")) return Forbid();

            var key = string.IsNullOrWhiteSpace(lang) ? "en" : lang.Trim().ToLower();
            if (_warmups.TryGetValue(key, out var status)) return Ok(status);

            return Ok(new LocalizationWarmupStatusDto
            {
                Lang = key,
                Status = "idle",
                TotalPois = 0,
                Ready = 0,
                Failed = 0,
                Progress = 0,
                StartedAtUtc = DateTime.MinValue,
                LastMessage = "warmup_idle"
            });
        }

        private bool HasPermission(string permission)
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")) return true;
                var claimPerms = User.FindAll("permission").Select(c => c.Value);
                if (claimPerms.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase))) return true;
            }

            if (Request.Headers.TryGetValue("X-API-Key", out var apiKey) && !string.IsNullOrWhiteSpace(apiKey.FirstOrDefault()))
            {
                // admin/service callers using API key are allowed in current architecture
                return true;
            }

            if (!Request.Headers.TryGetValue("X-User-Id", out var uidRaw)) return false;
            if (!int.TryParse(uidRaw.FirstOrDefault(), out var userId) || userId <= 0) return false;

            var user = _db.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return false;

            if (string.Equals(user.Role, "super_admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var perms = (user.PermissionsJson ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return perms.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));
        }

        private async Task LogLocalizationJobAsync(int poiId, string lang, string jobType, string status, string? notes)
        {
            try
            {
                await _db.LocalizationJobLogs.AddAsync(new LocalizationJobLog
                {
                    PoiId = poiId,
                    LanguageCode = lang,
                    JobType = jobType,
                    Status = status,
                    Notes = notes,
                    TimestampUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }
            catch
            {
                // swallow logging errors to avoid breaking localization flow
            }
        }

        private async Task<string> TranslateTextFreeAsync(string? source, string targetLang)
        {
            if (string.IsNullOrWhiteSpace(source)) return string.Empty;

            var target = NormalizeLanguageCode(targetLang);
            if (string.Equals(target, "vi", StringComparison.OrdinalIgnoreCase)) return source;

            if (!SupportedAutoTranslateLanguages.Contains(target) && target.Length > 2)
            {
                return source;
            }

            try
            {
                var client = _httpFactory.CreateClient();
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=vi&tl={Uri.EscapeDataString(target)}&dt=t&q={Uri.EscapeDataString(source)}";
                using var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return source;

                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                {
                    return source;
                }

                var segments = doc.RootElement[0];
                if (segments.ValueKind != JsonValueKind.Array)
                {
                    return source;
                }

                var sb = new StringBuilder();
                foreach (var seg in segments.EnumerateArray())
                {
                    if (seg.ValueKind != JsonValueKind.Array || seg.GetArrayLength() == 0) continue;
                    var part = seg[0].GetString();
                    if (!string.IsNullOrWhiteSpace(part)) sb.Append(part);
                }

                var translated = sb.ToString().Trim();
                return string.IsNullOrWhiteSpace(translated) ? source : translated;
            }
            catch
            {
                return source;
            }
        }

        private static string NormalizeLanguageCode(string? language)
        {
            var normalized = (language ?? "en").Trim().ToLowerInvariant();
            if (normalized.Contains('-')) normalized = normalized.Split('-')[0];
            if (normalized.Contains('_')) normalized = normalized.Split('_')[0];
            if (normalized == "vn") return "vi";
            return string.IsNullOrWhiteSpace(normalized) ? "en" : normalized;
        }

        private void TryCleanupTtsCache()
        {
            try
            {
                var root = Path.Combine(_env.ContentRootPath, "wwwroot", "tts-cache");
                if (!Directory.Exists(root)) return;

                var files = Directory
                    .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .Where(f => f.Exists)
                    .OrderBy(f => f.LastWriteTimeUtc)
                    .ToList();

                if (!files.Any()) return;

                long totalBytes = files.Sum(f => f.Length);
                var totalFiles = files.Count;
                if (totalBytes <= MaxTtsCacheBytes && totalFiles <= MaxTtsCacheFiles)
                {
                    return;
                }

                var removeCount = Math.Min(TtsCleanupBatchDelete, files.Count);
                for (var i = 0; i < removeCount; i++)
                {
                    try { files[i].Delete(); } catch { }
                }
            }
            catch
            {
            }
        }
    }
}
