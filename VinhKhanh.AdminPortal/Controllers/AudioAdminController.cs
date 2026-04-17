using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;
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
                var audios = await client.GetFromJsonAsync<List<AudioModel>>($"api/audio/by-poi/{poiId}") ?? new List<AudioModel>();
                foreach (var audio in audios)
                {
                    audio.Url = ToAbsoluteApiUrl(client, audio.Url);
                }
                try
                {
                    var voices = await client.GetFromJsonAsync<AudioVoicesResponse>("api/audio/voices");
                    ViewData["VoiceCatalog"] = voices;
                }
                catch
                {
                    ViewData["VoiceCatalog"] = new AudioVoicesResponse();
                }
                ViewData["PoiId"] = poiId;
                return View(audios);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Không thể tải audio: " + ex.Message;
                return View(new List<AudioModel>());
            }
        }

        [HttpPost]
        public async Task<IActionResult> Upload(int poiId, string language)
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
        public async Task<IActionResult> Delete(int id, int poiId)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            await client.DeleteAsync($"api/audio/{id}");
            return RedirectToAction("Index", new { poiId });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateTts(int poiId, string language, string voice, string text)
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
                voice = string.IsNullOrWhiteSpace(voice) ? null : voice
            };

            using var res = await client.PostAsJsonAsync("api/audio/tts", req);
            if (!res.IsSuccessStatusCode)
            {
                var errorBody = await res.Content.ReadAsStringAsync();
                TempData["Error"] = "Generate TTS thất bại: " + res.StatusCode + (string.IsNullOrWhiteSpace(errorBody) ? string.Empty : $" - {errorBody}");
                return RedirectToAction("Index", new { poiId });
            }

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

            TempData["Success"] = "Generate TTS thành công";
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

        public class AudioVoiceItem
        {
            public string Lang { get; set; } = string.Empty;
            public string DefaultVoice { get; set; } = string.Empty;
            public List<string> Voices { get; set; } = new();
        }
    }
}
