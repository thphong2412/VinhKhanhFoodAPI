using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VinhKhanh.Shared;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class AudioAdminController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

        public AudioAdminController(IHttpClientFactory factory, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _factory = factory;
            _config = config;
        }

        private string GetApiKey()
        {
            try
            {
                var configured = _config?["ApiKey"];
                if (!string.IsNullOrEmpty(configured)) return configured;
            }
            catch { }
            return "admin123";
        }

        public async Task<IActionResult> Index(int poiId)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            try
            {
                var audios = await client.GetFromJsonAsync<List<AudioListItem>>($"api/audio/by-poi/{poiId}") ?? new List<AudioListItem>();
                foreach (var audio in audios)
                {
                    audio.Url = ToAbsoluteApiUrl(client, audio.Url);
                }
                ViewData["PoiId"] = poiId;
                return View(audios);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Không thể tải audio: " + ex.Message;
                return View(new List<AudioListItem>());
            }
        }

        [HttpPost]
        public async Task<IActionResult> Upload(int poiId, string language, string? fileName)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            if (Request.Form.Files.Count == 0)
            {
                TempData["Error"] = "Chưa chọn file";
                return RedirectToAction("Index", new { poiId });
            }

            var file = Request.Form.Files[0];
            using var ms = new System.IO.MemoryStream();
            await file.CopyToAsync(ms);
            ms.Position = 0;

            var content = new MultipartFormDataContent();
            content.Add(new StringContent(poiId.ToString()), "poiId");
            content.Add(new StringContent(language ?? "vi"), "language");
            content.Add(new StringContent(fileName ?? string.Empty), "fileName");
            content.Add(new StreamContent(ms), "file", file.FileName);

            var res = await client.PostAsync("api/audio/upload", content);
            if (!res.IsSuccessStatusCode)
            {
                TempData["Error"] = "Upload thất bại: " + res.StatusCode;
            }
            else
            {
                TempData["Success"] = "Upload thành công";
            }

            return RedirectToAction("Index", new { poiId });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateMetadata(int id, int poiId, string languageCode, string? fileName)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            var payload = new
            {
                LanguageCode = string.IsNullOrWhiteSpace(languageCode) ? "vi" : languageCode.Trim().ToLowerInvariant(),
                FileName = string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim()
            };

            var res = await client.PutAsJsonAsync($"api/audio/{id}/metadata", payload);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                TempData["Error"] = "Cập nhật metadata audio thất bại: " + res.StatusCode + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" - {body}");
            }
            else
            {
                TempData["Success"] = "Đã cập nhật tên file/ngôn ngữ audio.";
            }

            return RedirectToAction("Index", new { poiId });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id, int poiId)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            await client.DeleteAsync($"api/audio/{id}");
            return RedirectToAction("Index", new { poiId });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateTts(int poiId, string language, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                TempData["Error"] = "Thiếu nội dung text để tạo TTS";
                return RedirectToAction("Index", new { poiId });
            }

            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            var req = new
            {
                text,
                lang = string.IsNullOrWhiteSpace(language) ? "vi" : language,
                voice = (string?)null
            };

            using var res = await client.PostAsJsonAsync("api/audio/tts", req);
            if (!res.IsSuccessStatusCode)
            {
                var errorBody = await res.Content.ReadAsStringAsync();
                TempData["Error"] = "Generate TTS thất bại: " + res.StatusCode + (string.IsNullOrWhiteSpace(errorBody) ? string.Empty : $" - {errorBody}");
                return RedirectToAction("Index", new { poiId });
            }

            var warning = res.Headers.TryGetValues("X-TTS-Warning", out var warningVals) ? warningVals.FirstOrDefault() : null;

            var staticUrl = res.Headers.TryGetValues("X-Static-Url", out var vals) ? vals.FirstOrDefault() : null;
            if (string.IsNullOrWhiteSpace(staticUrl))
            {
                TempData["Error"] = "TTS tạo xong nhưng thiếu X-Static-Url";
                return RedirectToAction("Index", new { poiId });
            }

            staticUrl = ToAbsoluteApiUrl(client, staticUrl);

            var model = new AudioModel
            {
                PoiId = poiId,
                Url = staticUrl,
                LanguageCode = string.IsNullOrWhiteSpace(language) ? "vi" : language,
                IsTts = true,
                IsProcessed = true
            };
            await client.PostAsJsonAsync("api/audio/upload-reference", model);

            TempData["Success"] = string.IsNullOrWhiteSpace(warning)
                ? "Generate TTS thành công"
                : "Generate TTS thành công (đang dùng fallback, kiểm tra Azure Speech key/region để có chất lượng voice tốt hơn)";
            return RedirectToAction("Index", new { poiId });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateTtsAllLanguages(int poiId)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            using var res = await client.PostAsync($"api/audio/tts/generate-all/{poiId}", null);
            var body = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                TempData["Error"] = "Generate TTS tất cả ngôn ngữ thất bại: " + res.StatusCode + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" - {body}");
                return RedirectToAction("Index", new { poiId });
            }

            try
            {
                var payload = JsonSerializer.Deserialize<BulkTtsResponse>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (payload == null)
                {
                    TempData["Success"] = "Generate TTS tất cả ngôn ngữ thành công.";
                    return RedirectToAction("Index", new { poiId });
                }

                var failedLangs = payload.Results
                    .Where(r => string.Equals(r.Status, "failed", StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.Language)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                var skippedLangs = payload.Results
                    .Where(r => string.Equals(r.Status, "skipped", StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.Language)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                var summary = $"Đã generate {payload.Generated}/{payload.Total} ngôn ngữ.";
                if (failedLangs.Count > 0)
                {
                    summary += " Lỗi: " + string.Join(", ", failedLangs) + ".";
                }
                if (skippedLangs.Count > 0)
                {
                    summary += " Bỏ qua (thiếu mô tả): " + string.Join(", ", skippedLangs) + ".";
                }

                TempData["Success"] = summary;
            }
            catch
            {
                TempData["Success"] = "Generate TTS tất cả ngôn ngữ thành công.";
            }

            return RedirectToAction("Index", new { poiId });
        }

        private static string ToAbsoluteApiUrl(HttpClient client, string? rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return string.Empty;
            if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var absolute)) return absolute.ToString();
            if (client.BaseAddress == null) return rawUrl;
            return new Uri(client.BaseAddress, rawUrl.TrimStart('/')).ToString();
        }

        public class AudioVoicesResponse
        {
            public int TotalLanguages { get; set; }
            public List<AudioVoiceItem> Items { get; set; } = new();
        }

        public class AudioListItem
        {
            public int Id { get; set; }
            public int PoiId { get; set; }
            public string Url { get; set; } = string.Empty;
            public string LanguageCode { get; set; } = "vi";
            public bool IsTts { get; set; }
            public bool IsProcessed { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public string SourceType { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
        }

        public class AudioVoiceItem
        {
            public string Lang { get; set; } = string.Empty;
            public string DefaultVoice { get; set; } = string.Empty;
            public List<string> Voices { get; set; } = new();
        }

        public class BulkTtsResponse
        {
            [JsonPropertyName("poiId")]
            public int PoiId { get; set; }

            [JsonPropertyName("total")]
            public int Total { get; set; }

            [JsonPropertyName("generated")]
            public int Generated { get; set; }

            [JsonPropertyName("results")]
            public List<BulkTtsResult> Results { get; set; } = new();
        }

        public class BulkTtsResult
        {
            [JsonPropertyName("language")]
            public string Language { get; set; } = string.Empty;

            [JsonPropertyName("status")]
            public string Status { get; set; } = string.Empty;
        }
    }
}
