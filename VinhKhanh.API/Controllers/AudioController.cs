using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Security.Cryptography;
using System.Text;
using VinhKhanh.API.Data;
using VinhKhanh.API.Hubs;
using VinhKhanh.API.Services;
using VinhKhanh.Shared;
using Microsoft.EntityFrameworkCore;

namespace VinhKhanh.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AudioController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<SyncHub> _hubContext;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _configuration;
        private readonly IQrCodeService _qrCodeService;
        private const int MaxUploadDurationSeconds = 90;
        private const int MaxTtsTextLength = 450;
        private static readonly Dictionary<string, string> _defaultVoices = new(StringComparer.OrdinalIgnoreCase)
        {
            ["vi"] = "vi-VN-HoaiMyNeural",
            ["en"] = "en-US-JennyNeural",
            ["fr"] = "fr-FR-DeniseNeural",
            ["zh"] = "zh-CN-XiaoxiaoNeural",
            ["ja"] = "ja-JP-NanamiNeural",
            ["ko"] = "ko-KR-SunHiNeural",
            ["th"] = "th-TH-PremwadeeNeural",
            ["es"] = "es-ES-ElviraNeural",
            ["ru"] = "ru-RU-SvetlanaNeural"
        };

        public AudioController(AppDbContext db, IWebHostEnvironment env, IHubContext<SyncHub> hubContext, IHttpClientFactory httpFactory, IConfiguration configuration, IQrCodeService qrCodeService)
        {
            _db = db;
            _env = env;
            _hubContext = hubContext;
            _httpFactory = httpFactory;
            _configuration = configuration;
            _qrCodeService = qrCodeService;
        }

        [HttpPost("tts")]
        public async Task<IActionResult> GenerateTts([FromBody] AudioTtsRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Text)) return BadRequest("text_required");
            if (req.Text.Trim().Length > MaxTtsTextLength)
            {
                return BadRequest($"tts_text_too_long_max_{MaxTtsTextLength}_chars");
            }

            var lang = NormalizeLang(req.Lang);
            var resolvedVoice = ResolveVoice(lang, req.Voice);

            var cacheRoot = Path.Combine(_env.ContentRootPath, "wwwroot", "tts-cache", lang);
            Directory.CreateDirectory(cacheRoot);

            var key = ComputeMd5($"{req.Text}:{lang}:{resolvedVoice ?? "auto"}");
            var fileName = $"{key}.mp3";
            var filePath = Path.Combine(cacheRoot, fileName);
            var staticUrl = $"/tts-cache/{lang}/{fileName}";

            var hit = System.IO.File.Exists(filePath);
            if (!hit)
            {
                try
                {
                    var generation = await TryGenerateTtsAudioAsync(req.Text, lang, resolvedVoice, filePath);
                    if (!generation.Success)
                    {
                        return StatusCode(generation.StatusCode, new
                        {
                            error = "tts_generation_failed",
                            status = generation.StatusCode,
                            detail = generation.Error
                        });
                    }

                    if (!generation.UsedAzure)
                    {
                        Response.Headers["X-TTS-Warning"] = "azure_speech_not_configured_or_failed_using_fallback";
                    }
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { error = "tts_generation_failed", detail = ex.Message });
                }
            }

            var mtime = System.IO.File.GetLastWriteTimeUtc(filePath).Ticks;
            Response.Headers["X-Cache"] = hit ? "HIT" : "MISS";
            Response.Headers["X-Voice-Resolved"] = resolvedVoice ?? string.Empty;
            Response.Headers["X-Static-Url"] = $"{staticUrl}?v={mtime}&l={lang}";
            Response.Headers["X-TTS-Provider"] = ResolveTtsProviderName();

            return PhysicalFile(filePath, "audio/mpeg", enableRangeProcessing: true);
        }

        [HttpGet("voices")]
        public IActionResult GetVoices()
        {
            var items = _defaultVoices
                .Select(kvp => new
                {
                    lang = kvp.Key,
                    default_voice = kvp.Value,
                    voices = GetVoiceOptions(kvp.Key)
                })
                .ToList();

            return Ok(new
            {
                total_languages = items.Count,
                items
            });
        }

        [HttpPost("tts/generate-all/{poiId}")]
        public async Task<IActionResult> GenerateAllLanguageTts(int poiId)
        {
            var supportedLangs = new[] { "vi", "en", "fr", "ja", "ko", "zh", "th", "es", "ru", "it" };
            var contents = await _db.PointContents
                .Where(c => c.PoiId == poiId)
                .ToListAsync();
            var generatedCount = 0;

            var viSource = contents.FirstOrDefault(c => string.Equals(c.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
            if (viSource == null)
            {
                return BadRequest(new { error = "vi_content_required", detail = "Không tìm thấy nội dung tiếng Việt để tạo TTS." });
            }

            var results = new List<object>();
            foreach (var lang in supportedLangs)
            {
                try
                {
                    var content = contents.FirstOrDefault(c => string.Equals(c.LanguageCode, lang, StringComparison.OrdinalIgnoreCase)) ?? viSource;
                    var text = (content.Description ?? string.Empty).Trim();

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        results.Add(new { language = lang, status = "skipped", reason = "description_empty" });
                        continue;
                    }

                    if (text.Length > MaxTtsTextLength)
                    {
                        text = text[..MaxTtsTextLength];
                    }

                    var voice = _defaultVoices.TryGetValue(lang, out var mappedVoice)
                        ? mappedVoice
                        : "en-US-JennyNeural";

                    var generated = await GenerateTtsToStaticUrlAsync(text, lang, voice);
                    if (!generated.Success || string.IsNullOrWhiteSpace(generated.StaticUrl))
                    {
                        results.Add(new { language = lang, status = "failed", reason = generated.Error ?? "tts_failed" });
                        continue;
                    }

                    var existingLangAudio = await _db.AudioFiles
                        .Where(a => a.PoiId == poiId && a.IsTts && (a.LanguageCode ?? string.Empty).ToLower() == lang)
                        .ToListAsync();
                    if (existingLangAudio.Any())
                    {
                        _db.AudioFiles.RemoveRange(existingLangAudio);
                    }

                    _db.AudioFiles.Add(new AudioModel
                    {
                        PoiId = poiId,
                        Url = generated.StaticUrl,
                        LanguageCode = lang,
                        IsTts = true,
                        IsProcessed = true
                    });
                    generatedCount++;

                    results.Add(new
                    {
                        language = lang,
                        status = "generated",
                        url = generated.StaticUrl,
                        provider = generated.Provider,
                        fallback = generated.FallbackUsed
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new { language = lang, status = "failed", reason = ex.Message });
                }
            }

            var poi = await _db.PointsOfInterest.FirstOrDefaultAsync(p => p.Id == poiId);
            if (poi != null && string.IsNullOrWhiteSpace(poi.QrCode))
            {
                poi.QrCode = _qrCodeService.GenerateQrCode(poi.Id, poi.Name ?? $"POI {poi.Id}");
            }

            await _db.SaveChangesAsync();

            try
            {
                await _hubContext.Clients.All.SendAsync("AudioUploaded", new
                {
                    poiId,
                    isBulk = true,
                    timestamp = DateTime.UtcNow
                });
            }
            catch
            {
                // ignore broadcast failure
            }

            return Ok(new
            {
                poiId,
                total = supportedLangs.Length,
                generated = generatedCount,
                results
            });
        }

        [HttpGet("by-poi/{poiId}")]
        public async Task<IActionResult> GetByPoi(int poiId)
        {
            var files = await _db.AudioFiles
                .Where(a => a.PoiId == poiId)
                .OrderBy(a => a.LanguageCode)
                .ThenBy(a => a.IsTts)
                .ThenByDescending(a => a.CreatedAtUtc)
                .ToListAsync();

            var payload = files.Select(a => new
            {
                a.Id,
                a.PoiId,
                a.Url,
                a.LanguageCode,
                a.IsTts,
                a.IsProcessed,
                a.CreatedAtUtc,
                sourceType = a.IsTts ? "tts" : "uploaded",
                fileName = ExtractFileNameFromUrl(a.Url)
            });
            return Ok(payload);
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload([FromForm] int poiId, [FromForm] string language = "vi", [FromForm] string? fileName = null)
        {
            var file = Request.Form.Files.FirstOrDefault();
            if (file == null) return BadRequest("No file uploaded");

            var durationCheck = await ValidateAudioDurationAsync(file);
            if (!durationCheck.IsValid)
            {
                return BadRequest(new
                {
                    error = "audio_too_long_max_90_seconds",
                    detail = durationCheck.Error,
                    maxSeconds = MaxUploadDurationSeconds
                });
            }

            var uploads = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads");
            Directory.CreateDirectory(uploads);

            var preferredName = string.IsNullOrWhiteSpace(fileName)
                ? Path.GetFileNameWithoutExtension(file.FileName)
                : Path.GetFileNameWithoutExtension(fileName);
            var safeOriginalName = SanitizeFileName(preferredName);

            var ext = Path.GetExtension(file.FileName);
            var preferredExt = Path.GetExtension(fileName ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(preferredExt))
            {
                ext = preferredExt;
            }
            if (string.IsNullOrWhiteSpace(ext)) ext = ".mp3";
            var savedFileName = $"audio_{poiId}_{Guid.NewGuid():N}_{safeOriginalName}{ext}";
            var filePath = Path.Combine(uploads, savedFileName);
            using (var fs = System.IO.File.Create(filePath))
            {
                await file.CopyToAsync(fs);
            }

            var normalizedLang = NormalizeLang(language);
            var url = $"/uploads/{savedFileName}";
            var model = new AudioModel { PoiId = poiId, Url = url, LanguageCode = normalizedLang, IsTts = false, IsProcessed = true };
            _db.AudioFiles.Add(model);

            var poi = await _db.PointsOfInterest.FirstOrDefaultAsync(p => p.Id == poiId);
            if (poi != null && string.IsNullOrWhiteSpace(poi.QrCode))
            {
                poi.QrCode = _qrCodeService.GenerateQrCode(poi.Id, poi.Name ?? $"POI {poi.Id}");
            }

            await _db.SaveChangesAsync();

            // ✅ Broadcast audio upload to all connected clients
            try
            {
                await _hubContext.Clients.All.SendAsync("AudioUploaded", model);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SignalR broadcast failed: {ex.Message}");
            }

            return Ok(model);
        }

        [HttpPost("upload-reference")]
        public async Task<IActionResult> UploadReference([FromBody] AudioModel model)
        {
            if (model == null || model.PoiId <= 0 || string.IsNullOrWhiteSpace(model.Url))
                return BadRequest("invalid_reference");

            model.LanguageCode = NormalizeLang(model.LanguageCode);
            model.IsProcessed = true;
            _db.AudioFiles.Add(model);

            var poi = await _db.PointsOfInterest.FirstOrDefaultAsync(p => p.Id == model.PoiId);
            if (poi != null && string.IsNullOrWhiteSpace(poi.QrCode))
            {
                poi.QrCode = _qrCodeService.GenerateQrCode(poi.Id, poi.Name ?? $"POI {poi.Id}");
            }

            await _db.SaveChangesAsync();

            try
            {
                await _hubContext.Clients.All.SendAsync("AudioUploaded", model);
            }
            catch
            {
                // ignore realtime broadcast failure
            }

            return Ok(model);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var a = await _db.AudioFiles.FindAsync(id);
            if (a == null) return NotFound();

            var poiId = a.PoiId;
            _db.AudioFiles.Remove(a);
            await _db.SaveChangesAsync();

            // ✅ Broadcast audio deletion to all connected clients
            try
            {
                await _hubContext.Clients.All.SendAsync("AudioDeleted", id, poiId);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SignalR broadcast failed: {ex.Message}");
            }

            return NoContent();
        }

        [HttpPut("{id}/metadata")]
        public async Task<IActionResult> UpdateMetadata(int id, [FromBody] AudioMetadataUpdateRequest request)
        {
            var audio = await _db.AudioFiles.FirstOrDefaultAsync(a => a.Id == id);
            if (audio == null) return NotFound();

            var ownerHeader = Request.Headers["X-Owner-Id"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(ownerHeader) && int.TryParse(ownerHeader, out var ownerId) && ownerId > 0)
            {
                var poi = await _db.PointsOfInterest.FirstOrDefaultAsync(p => p.Id == audio.PoiId);
                if (poi == null || poi.OwnerId != ownerId)
                {
                    return Forbid();
                }
            }

            var normalizedLang = NormalizeLang(request?.LanguageCode);
            var desiredName = request?.FileName?.Trim() ?? string.Empty;

            var oldUrl = audio.Url ?? string.Empty;
            var currentStoredName = ExtractFileNameFromUrl(oldUrl);
            var finalStoredName = currentStoredName;

            // Rename physical file only for uploaded files under /uploads
            if (!string.IsNullOrWhiteSpace(desiredName)
                && !audio.IsTts
                && oldUrl.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            {
                var uploads = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads");
                Directory.CreateDirectory(uploads);

                var oldPath = Path.Combine(uploads, currentStoredName);
                if (System.IO.File.Exists(oldPath))
                {
                    var oldExt = Path.GetExtension(currentStoredName);
                    var desiredExt = Path.GetExtension(desiredName);
                    if (string.IsNullOrWhiteSpace(desiredExt)) desiredExt = oldExt;
                    if (string.IsNullOrWhiteSpace(desiredExt)) desiredExt = ".mp3";

                    var desiredStem = Path.GetFileNameWithoutExtension(desiredName);
                    var safeStem = SanitizeFileName(desiredStem);
                    var candidate = $"{safeStem}{desiredExt}";
                    var targetPath = Path.Combine(uploads, candidate);

                    if (string.Equals(currentStoredName, candidate, StringComparison.OrdinalIgnoreCase) == false)
                    {
                        var suffix = 1;
                        while (System.IO.File.Exists(targetPath))
                        {
                            candidate = $"{safeStem}_{suffix}{desiredExt}";
                            targetPath = Path.Combine(uploads, candidate);
                            suffix++;
                        }

                        System.IO.File.Move(oldPath, targetPath);
                        finalStoredName = candidate;
                        audio.Url = $"/uploads/{finalStoredName}";
                    }
                }
            }

            audio.LanguageCode = normalizedLang;
            _db.AudioFiles.Update(audio);
            await _db.SaveChangesAsync();

            try
            {
                await _hubContext.Clients.All.SendAsync("AudioUploaded", audio);
            }
            catch
            {
                // ignore realtime broadcast failure
            }

            return Ok(new
            {
                audio.Id,
                audio.PoiId,
                audio.Url,
                audio.LanguageCode,
                audio.IsTts,
                fileName = finalStoredName
            });
        }

        [HttpGet("pack-manifest")]
        public IActionResult GetPackManifest([FromQuery] string lang = "vi")
        {
            var normalizedLang = string.IsNullOrWhiteSpace(lang) ? "vi" : lang.Trim().ToLowerInvariant();
            var root = Path.Combine(_env.ContentRootPath, "wwwroot", "tts-cache", normalizedLang);
            if (!Directory.Exists(root))
            {
                return Ok(new
                {
                    lang = normalizedLang,
                    pack_version = "empty",
                    total_files = 0,
                    total_bytes = 0,
                    files = Array.Empty<object>()
                });
            }

            var files = Directory.GetFiles(root, "*.mp3", SearchOption.TopDirectoryOnly)
                .Select(path =>
                {
                    var fi = new FileInfo(path);
                    using var sha = SHA256.Create();
                    using var fs = System.IO.File.OpenRead(path);
                    var hash = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
                    return new
                    {
                        file = fi.Name,
                        url = $"/tts-cache/{normalizedLang}/{fi.Name}",
                        size = fi.Length,
                        sha256 = hash,
                        mtime_utc = fi.LastWriteTimeUtc
                    };
                })
                .OrderBy(f => f.file)
                .ToList();

            var totalBytes = files.Sum(f => (long)f.size);
            var versionSeed = string.Join("|", files.Select(f => $"{f.file}:{f.sha256}:{f.size}"));
            var packVersion = files.Count == 0 ? "empty" : ComputeMd5(versionSeed).Substring(0, 12);

            return Ok(new
            {
                lang = normalizedLang,
                pack_version = packVersion,
                total_files = files.Count,
                total_bytes = totalBytes,
                files
            });
        }

        // GET: api/audio/pending
        [HttpGet("pending")]
        public async Task<IActionResult> GetPending()
        {
            var list = await _db.AudioFiles.Where(a => !a.IsProcessed).ToListAsync();
            return Ok(list);
        }

        // POST: api/audio/process/{id}
        // Process a queued TTS audio item (mock implementation for POC)
        [HttpPost("process/{id}")]
        public async Task<IActionResult> Process(int id)
        {
            var item = await _db.AudioFiles.FindAsync(id);
            if (item == null) return NotFound();

            if (item.IsProcessed) return BadRequest("Already processed");

            // Mock processing: create a placeholder file under wwwroot/uploads
            var uploads = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads");
            Directory.CreateDirectory(uploads);
            var fileName = $"tts_{id}.txt"; // placeholder text file for POC
            var filePath = Path.Combine(uploads, fileName);
            await System.IO.File.WriteAllTextAsync(filePath, $"TTS_PLACEHOLDER for audio {id} generated at {DateTime.UtcNow}");

            item.Url = $"/uploads/{fileName}";
            item.IsProcessed = true;
            _db.AudioFiles.Update(item);
            await _db.SaveChangesAsync();

            // ✅ Broadcast TTS processing completed
            try
            {
                await _hubContext.Clients.All.SendAsync("AudioProcessed", new 
                { 
                    id = item.Id,
                    poiId = item.PoiId,
                    url = item.Url,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SignalR broadcast failed: {ex.Message}");
            }

            return Ok(item);
        }

        private static string ComputeMd5(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            var hash = MD5.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static IEnumerable<string> GetVoiceOptions(string lang)
        {
            return lang.ToLowerInvariant() switch
            {
                "vi" => new[] { "vi-VN-HoaiMyNeural", "vi-VN-NamMinhNeural" },
                "en" => new[] { "en-US-JennyNeural", "en-US-GuyNeural" },
                "fr" => new[] { "fr-FR-DeniseNeural", "fr-FR-HenriNeural" },
                "zh" => new[] { "zh-CN-XiaoxiaoNeural", "zh-CN-YunxiNeural" },
                "ja" => new[] { "ja-JP-NanamiNeural", "ja-JP-KeitaNeural" },
                "ko" => new[] { "ko-KR-SunHiNeural", "ko-KR-InJoonNeural" },
                "th" => new[] { "th-TH-PremwadeeNeural", "th-TH-NiwatNeural" },
                "es" => new[] { "es-ES-ElviraNeural", "es-ES-AlvaroNeural" },
                "ru" => new[] { "ru-RU-SvetlanaNeural", "ru-RU-DmitryNeural" },
                "it" => new[] { "it-IT-ElsaNeural", "it-IT-DiegoNeural" },
                _ => Array.Empty<string>()
            };
        }

        private static string ExtractFileNameFromUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            var sanitized = url.Split('?', '#')[0];
            return Path.GetFileName(sanitized);
        }

        private static string NormalizeLang(string? lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "vi";
            var normalized = lang.Trim().ToLowerInvariant();
            if (normalized.Contains('-')) normalized = normalized.Split('-')[0];
            if (normalized.Contains('_')) normalized = normalized.Split('_')[0];
            return normalized;
        }

        private static string SanitizeFileName(string? value)
        {
            var source = string.IsNullOrWhiteSpace(value) ? "audio" : value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                source = source.Replace(invalid, '_');
            }

            source = source.Replace(' ', '_');
            if (source.Length > 60) source = source[..60];
            return string.IsNullOrWhiteSpace(source) ? "audio" : source;
        }

        private async Task<(bool Success, string? StaticUrl, string Provider, bool FallbackUsed, string? Error)> GenerateTtsToStaticUrlAsync(string text, string lang, string voice)
        {
            var normalizedLang = NormalizeLang(lang);
            var resolvedVoice = ResolveVoice(normalizedLang, voice);

            var cacheRoot = Path.Combine(_env.ContentRootPath, "wwwroot", "tts-cache", normalizedLang);
            Directory.CreateDirectory(cacheRoot);

            var key = ComputeMd5($"{text}:{normalizedLang}:{resolvedVoice ?? "auto"}");
            var fileName = $"{key}.mp3";
            var filePath = Path.Combine(cacheRoot, fileName);
            var staticUrl = $"/tts-cache/{normalizedLang}/{fileName}";

            var exists = System.IO.File.Exists(filePath);
            if (!exists)
            {
                var generation = await TryGenerateTtsAudioAsync(text, normalizedLang, resolvedVoice, filePath);
                if (!generation.Success)
                {
                    return (false, null, ResolveTtsProviderName(), !generation.UsedAzure, generation.Error);
                }

                var provider = generation.UsedAzure ? "azure-speech" : "google-translate-tts";
                return (true, staticUrl, provider, !generation.UsedAzure, null);
            }

            return (true, staticUrl, ResolveTtsProviderName(), false, null);
        }

        private string ResolveTtsProviderName()
        {
            var hasSpeechKey = !string.IsNullOrWhiteSpace(GetAzureSpeechKey());
            var hasSpeechRegion = !string.IsNullOrWhiteSpace(GetAzureSpeechRegion());
            return hasSpeechKey && hasSpeechRegion ? "azure-speech+fallback" : "google-translate-tts";
        }

        private string? GetAzureSpeechKey()
        {
            return _configuration["AzureSpeech:Key"]
                   ?? _configuration["Azure:Speech:Key"]
                   ?? _configuration["Speech:Key"]
                   ?? _configuration["AZURE_SPEECH_KEY"]
                   ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
        }

        private string? GetAzureSpeechRegion()
        {
            return _configuration["AzureSpeech:Region"]
                   ?? _configuration["Azure:Speech:Region"]
                   ?? _configuration["Speech:Region"]
                   ?? _configuration["AZURE_SPEECH_REGION"]
                   ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
        }

        private string? ResolveVoice(string lang, string? requestedVoice)
        {
            if (!string.IsNullOrWhiteSpace(requestedVoice))
            {
                return requestedVoice.Trim();
            }

            if (_defaultVoices.TryGetValue(NormalizeLang(lang), out var mappedVoice))
            {
                return mappedVoice;
            }

            return null;
        }

        private async Task<(bool Success, bool UsedAzure, int StatusCode, string? Error)> TryGenerateTtsAudioAsync(string text, string lang, string voice, string outputPath)
        {
                var azureError = await TryGenerateWithAzureSpeechAsync(text, lang, voice, outputPath);
            if (azureError == null)
            {
                return (true, true, 200, null);
            }

            var fallbackError = await TryGenerateWithGoogleTtsAsync(text, lang, outputPath);
            if (fallbackError == null)
            {
                return (true, false, 200, null);
            }

            var error = $"azure_failed={azureError}; fallback_failed={fallbackError}";
            return (false, false, 502, error);
        }

        private async Task<string?> TryGenerateWithAzureSpeechAsync(string text, string lang, string voice, string outputPath)
        {
            var speechKey = GetAzureSpeechKey();
            var speechRegion = GetAzureSpeechRegion();

            if (string.IsNullOrWhiteSpace(speechKey) || string.IsNullOrWhiteSpace(speechRegion))
            {
                return "azure_speech_key_or_region_missing";
            }

            try
            {
                var endpoint = $"https://{speechRegion}.tts.speech.microsoft.com/cognitiveservices/v1";
                var ssml = BuildAzureSsml(text, voice, lang);

                var client = _httpFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("Ocp-Apim-Subscription-Key", speechKey);
                request.Headers.Add("User-Agent", "VinhKhanhFoodAPI-TTS");
                request.Headers.Add("X-Microsoft-OutputFormat", "audio-16khz-64kbitrate-mono-mp3");
                request.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    return $"azure_speech_http_{(int)response.StatusCode}:{body}";
                }

                await using var fs = System.IO.File.Create(outputPath);
                await response.Content.CopyToAsync(fs);
                return null;
            }
            catch (Exception ex)
            {
                return $"azure_speech_exception:{ex.Message}";
            }
        }

        private static string BuildAzureSsml(string text, string voice, string lang)
        {
            var safeText = System.Security.SecurityElement.Escape(text) ?? string.Empty;
            var safeVoice = System.Security.SecurityElement.Escape(string.IsNullOrWhiteSpace(voice) ? "vi-VN-HoaiMyNeural" : voice.Trim()) ?? "vi-VN-HoaiMyNeural";
            var safeLang = System.Security.SecurityElement.Escape(string.IsNullOrWhiteSpace(lang) ? "en-US" : lang.Trim()) ?? "en-US";
            return $"<speak version=\"1.0\" xml:lang=\"{safeLang}\"><voice name=\"{safeVoice}\">{safeText}</voice></speak>";
        }

        private async Task<string?> TryGenerateWithGoogleTtsAsync(string text, string lang, string outputPath)
        {
            try
            {
                var requestUrl =
                    $"https://translate.google.com/translate_tts?ie=UTF-8&client=tw-ob&tl={Uri.EscapeDataString(lang)}&q={Uri.EscapeDataString(text)}";
                var client = _httpFactory.CreateClient();
                using var message = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                message.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    var providerBody = await response.Content.ReadAsStringAsync();
                    return $"google_tts_http_{(int)response.StatusCode}:{providerBody}";
                }

                await using var fs = System.IO.File.Create(outputPath);
                await response.Content.CopyToAsync(fs);
                return null;
            }
            catch (Exception ex)
            {
                return $"google_tts_exception:{ex.Message}";
            }
        }

        private async Task<(bool IsValid, string? Error)> ValidateAudioDurationAsync(IFormFile file)
        {
            if (file.Length <= 0) return (false, "empty_audio_file");

            if (file.Length > 12 * 1024 * 1024)
            {
                return (false, "audio_file_too_large_max_12mb");
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"audio-duration-{Guid.NewGuid()}{Path.GetExtension(file.FileName)}");
            try
            {
                await using (var fs = System.IO.File.Create(tempPath))
                {
                    await file.CopyToAsync(fs);
                }

                var durationSeconds = await EstimateDurationFromMp3Async(tempPath);
                if (durationSeconds == null)
                {
                    return (false, "audio_duration_read_failed_invalid_mp3_or_unsupported_format");
                }

                if (durationSeconds.Value > MaxUploadDurationSeconds)
                {
                    return (false, $"audio_duration_exceeded:{Math.Round(durationSeconds.Value, 1)}s");
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"audio_duration_read_failed:{ex.Message}");
            }
            finally
            {
                try
                {
                    if (System.IO.File.Exists(tempPath))
                    {
                        System.IO.File.Delete(tempPath);
                    }
                }
                catch
                {
                    // ignore cleanup failure
                }
            }
        }

        private static async Task<double?> EstimateDurationFromMp3Async(string filePath)
        {
            await using var fs = System.IO.File.OpenRead(filePath);
            if (fs.Length < 128) return null;

            var bitrateKbps = TryReadMp3BitrateKbps(fs);
            if (bitrateKbps == null || bitrateKbps <= 0) return null;

            var durationSeconds = fs.Length * 8d / (bitrateKbps.Value * 1000d);
            if (durationSeconds <= 0) return null;
            return durationSeconds;
        }

        private static int? TryReadMp3BitrateKbps(FileStream fs)
        {
            var buffer = new byte[4];

            while (fs.Position + 4 <= fs.Length)
            {
                var bytesRead = fs.Read(buffer, 0, 4);
                if (bytesRead < 4) break;

                if (buffer[0] == 0x49 && buffer[1] == 0x44 && buffer[2] == 0x33)
                {
                    if (fs.Position + 6 > fs.Length) return null;
                    var header = new byte[6];
                    var headerRead = fs.Read(header, 0, 6);
                    if (headerRead < 6) return null;

                    var sizeBytes = new[] { header[2], header[3], header[4], header[5] };
                    var tagSize = SynchsafeToInt(sizeBytes);
                    fs.Position += tagSize;
                    continue;
                }

                var b0 = buffer[0];
                var b1 = buffer[1];
                var b2 = buffer[2];

                var isFrameSync = b0 == 0xFF && (b1 & 0xE0) == 0xE0;
                if (!isFrameSync)
                {
                    fs.Position -= 3;
                    continue;
                }

                var versionBits = (b1 >> 3) & 0x03;
                var layerBits = (b1 >> 1) & 0x03;
                var bitrateIndex = (b2 >> 4) & 0x0F;

                if (versionBits == 0x01 || layerBits != 0x01 || bitrateIndex == 0x0F || bitrateIndex == 0)
                {
                    fs.Position -= 3;
                    continue;
                }

                var isMpeg1 = versionBits == 0x03;
                var bitrateTable = isMpeg1
                    ? new[] { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 }
                    : new[] { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 };

                var kbps = bitrateTable[bitrateIndex];
                return kbps > 0 ? kbps : null;
            }

            return null;
        }

        private static int SynchsafeToInt(byte[] bytes)
        {
            if (bytes.Length != 4) return 0;
            return (bytes[0] << 21) | (bytes[1] << 14) | (bytes[2] << 7) | bytes[3];
        }
    }

    public class AudioTtsRequest
    {
        public string Text { get; set; } = string.Empty;
        public string Lang { get; set; } = "vi";
        public string? Voice { get; set; }
    }

        public class AudioMetadataUpdateRequest
        {
            public string LanguageCode { get; set; } = "vi";
            public string? FileName { get; set; }
        }
}
