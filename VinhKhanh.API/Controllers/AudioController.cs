using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Security.Cryptography;
using System.Text;
using VinhKhanh.API.Data;
using VinhKhanh.API.Hubs;
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
        private static readonly Dictionary<string, string> _defaultVoices = new(StringComparer.OrdinalIgnoreCase)
        {
            ["vi"] = "vi-VN-HoaiMyNeural",
            ["en"] = "en-US-JennyNeural",
            ["zh"] = "zh-CN-XiaoxiaoNeural",
            ["ja"] = "ja-JP-NanamiNeural",
            ["ko"] = "ko-KR-SunHiNeural"
        };

        public AudioController(AppDbContext db, IWebHostEnvironment env, IHubContext<SyncHub> hubContext, IHttpClientFactory httpFactory)
        {
            _db = db;
            _env = env;
            _hubContext = hubContext;
            _httpFactory = httpFactory;
        }

        [HttpPost("tts")]
        public async Task<IActionResult> GenerateTts([FromBody] AudioTtsRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Text)) return BadRequest("text_required");
            if (req.Text.Trim().Length > 450)
            {
                return BadRequest("tts_text_too_long_max_450_chars");
            }

            var lang = string.IsNullOrWhiteSpace(req.Lang) ? "vi" : req.Lang.Trim().ToLowerInvariant();
            var resolvedVoice = string.IsNullOrWhiteSpace(req.Voice)
                ? (_defaultVoices.TryGetValue(lang, out var mapped) ? mapped : "en-US-JennyNeural")
                : req.Voice.Trim();

            var cacheRoot = Path.Combine(_env.ContentRootPath, "wwwroot", "tts-cache", lang);
            Directory.CreateDirectory(cacheRoot);

            var key = ComputeMd5($"{req.Text}:{lang}:{resolvedVoice}");
            var fileName = $"{key}.mp3";
            var filePath = Path.Combine(cacheRoot, fileName);
            var staticUrl = $"/tts-cache/{lang}/{fileName}";

            var hit = System.IO.File.Exists(filePath);
            if (!hit)
            {
                try
                {
                    var requestUrl =
                        $"https://translate.google.com/translate_tts?ie=UTF-8&client=tw-ob&tl={Uri.EscapeDataString(lang)}&q={Uri.EscapeDataString(req.Text)}";
                    var client = _httpFactory.CreateClient();
                    using var message = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                    message.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                    using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode)
                    {
                        var providerBody = await response.Content.ReadAsStringAsync();
                        return StatusCode((int)response.StatusCode, new
                        {
                            error = "tts_provider_failed",
                            status = (int)response.StatusCode,
                            detail = providerBody
                        });
                    }

                    await using var fs = System.IO.File.Create(filePath);
                    await response.Content.CopyToAsync(fs);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { error = "tts_generation_failed", detail = ex.Message });
                }
            }

            var mtime = System.IO.File.GetLastWriteTimeUtc(filePath).Ticks;
            Response.Headers["X-Cache"] = hit ? "HIT" : "MISS";
            Response.Headers["X-Voice-Resolved"] = resolvedVoice;
            Response.Headers["X-Static-Url"] = $"{staticUrl}?v={mtime}&l={lang}";

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

        [HttpGet("by-poi/{poiId}")]
        public async Task<IActionResult> GetByPoi(int poiId)
        {
            var files = await _db.AudioFiles.Where(a => a.PoiId == poiId).ToListAsync();
            return Ok(files);
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload([FromForm] int poiId, [FromForm] string language = "vi")
        {
            var file = Request.Form.Files.FirstOrDefault();
            if (file == null) return BadRequest("No file uploaded");

            var uploads = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads");
            Directory.CreateDirectory(uploads);
            var fileName = $"audio_{poiId}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploads, fileName);
            using (var fs = System.IO.File.Create(filePath))
            {
                await file.CopyToAsync(fs);
            }

            var url = $"/uploads/{fileName}";
            var model = new AudioModel { PoiId = poiId, Url = url, LanguageCode = language, IsTts = false, IsProcessed = true };
            _db.AudioFiles.Add(model);
            await _db.SaveChangesAsync();

            // ✅ Broadcast audio upload to all connected clients
            try
            {
                await _hubContext.Clients.All.SendAsync("AudioUploaded", new 
                { 
                    id = model.Id,
                    poiId = model.PoiId, 
                    url = model.Url,
                    languageCode = model.LanguageCode,
                    isTts = model.IsTts,
                    timestamp = DateTime.UtcNow
                });
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

            model.IsProcessed = true;
            _db.AudioFiles.Add(model);
            await _db.SaveChangesAsync();

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
                await _hubContext.Clients.All.SendAsync("AudioDeleted", new { id, poiId, timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SignalR broadcast failed: {ex.Message}");
            }

            return NoContent();
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
                "zh" => new[] { "zh-CN-XiaoxiaoNeural", "zh-CN-YunxiNeural" },
                "ja" => new[] { "ja-JP-NanamiNeural", "ja-JP-KeitaNeural" },
                "ko" => new[] { "ko-KR-SunHiNeural", "ko-KR-InJoonNeural" },
                _ => Array.Empty<string>()
            };
        }
    }

    public class AudioTtsRequest
    {
        public string Text { get; set; } = string.Empty;
        public string Lang { get; set; } = "vi";
        public string? Voice { get; set; }
    }
}
