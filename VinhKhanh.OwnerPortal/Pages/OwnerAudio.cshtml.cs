using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using VinhKhanh.Shared;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class OwnerAudioModel : PageModel
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<OwnerAudioModel> _logger;
        private readonly IConfiguration _config;

        public List<AudioListItem> Audios { get; set; } = new();
        public int PoiId { get; set; }

        public OwnerAudioModel(IHttpClientFactory factory, ILogger<OwnerAudioModel> logger, IConfiguration config)
        {
            _factory = factory;
            _logger = logger;
            _config = config;
        }

        private void ConfigureApiClient(HttpClient client, int ownerId)
        {
            client.DefaultRequestHeaders.Remove("X-Owner-Id");
            client.DefaultRequestHeaders.Add("X-Owner-Id", ownerId.ToString());
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", (_config["ApiKey"] ?? "admin123").Trim());
        }

        public async Task<IActionResult> OnGetAsync(int poiId)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            PoiId = poiId;
            var client = _factory.CreateClient("api");
            ConfigureApiClient(client, uid);
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            Audios = await client.GetFromJsonAsync<List<AudioListItem>>($"api/audio/by-poi/{poiId}") ?? new();
            foreach (var audio in Audios)
            {
                audio.Url = ToAbsoluteApiUrl(client, audio.Url);
            }
            return Page();
        }

        public async Task<IActionResult> OnPostUploadAsync(int poiId, string language, string? fileName)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            var client = _factory.CreateClient("api");
            ConfigureApiClient(client, uid);
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            if (Request.Form.Files.Count == 0)
            {
                TempData["ErrorMessage"] = "Chưa chọn file audio.";
                return RedirectToPage(new { poiId });
            }

            var file = Request.Form.Files[0];
            await using var stream = file.OpenReadStream();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var base64 = Convert.ToBase64String(bytes);

            var requestedFileName = string.IsNullOrWhiteSpace(fileName) ? file.FileName : fileName.Trim();
            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".mp3";
            if (!requestedFileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                requestedFileName += extension;
            }

            using var msUpload = new MemoryStream(bytes);
            var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent(poiId.ToString()), "poiId");
            multipart.Add(new StringContent(string.IsNullOrWhiteSpace(language) ? "vi" : language.Trim().ToLowerInvariant()), "language");
            multipart.Add(new StringContent(string.IsNullOrWhiteSpace(requestedFileName) ? file.FileName : requestedFileName), "fileName");
            multipart.Add(new StreamContent(msUpload), "file", file.FileName);

            var res = await client.PostAsync("api/audio/upload", multipart);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                TempData["ErrorMessage"] = "Upload audio thất bại." + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" {body}");
            }
            else
            {
                TempData["SuccessMessage"] = "Đã upload audio thành công.";
            }

            return RedirectToPage(new { poiId });
        }

        public async Task<IActionResult> OnPostDeleteAsync(int poiId, int id)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            var client = _factory.CreateClient("api");
            ConfigureApiClient(client, uid);
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            var deleteRes = await client.DeleteAsync($"api/audio/{id}");
            if (!deleteRes.IsSuccessStatusCode)
            {
                var body = await deleteRes.Content.ReadAsStringAsync();
                TempData["ErrorMessage"] = "Xóa audio thất bại."
                    + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" {body}");
            }
            else
            {
                TempData["SuccessMessage"] = "Đã xóa audio thành công.";
            }

            return RedirectToPage(new { poiId });
        }

        public async Task<IActionResult> OnPostUpdateMetadataAsync(int poiId, int id, string languageCode, string? fileName)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            var client = _factory.CreateClient("api");
            ConfigureApiClient(client, uid);
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            var payload = new
            {
                LanguageCode = string.IsNullOrWhiteSpace(languageCode) ? "vi" : languageCode.Trim().ToLowerInvariant(),
                FileName = string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim()
            };

            var submitRes = await client.PutAsJsonAsync($"api/audio/{id}/metadata", payload);
            if (!submitRes.IsSuccessStatusCode)
            {
                var body = await submitRes.Content.ReadAsStringAsync();
                TempData["ErrorMessage"] = "Cập nhật metadata audio thất bại." + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" {body}");
            }
            else
            {
                TempData["SuccessMessage"] = "Đã cập nhật tên file/ngôn ngữ audio.";
            }

            return RedirectToPage(new { poiId });
        }

        public async Task<IActionResult> OnPostGenerateTtsAsync(int poiId, string language, string text)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            if (string.IsNullOrWhiteSpace(text))
            {
                TempData["ErrorMessage"] = "Thiếu nội dung text để tạo TTS.";
                return RedirectToPage(new { poiId });
            }

            var client = _factory.CreateClient("api");
            ConfigureApiClient(client, uid);
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            var req = new
            {
                text,
                lang = string.IsNullOrWhiteSpace(language) ? "vi" : language.Trim().ToLowerInvariant(),
                voice = (string?)null
            };

            using var res = await client.PostAsJsonAsync("api/audio/tts", req);
            if (!res.IsSuccessStatusCode)
            {
                var errorBody = await res.Content.ReadAsStringAsync();
                TempData["ErrorMessage"] = "Generate TTS thất bại: " + res.StatusCode + (string.IsNullOrWhiteSpace(errorBody) ? string.Empty : $" - {errorBody}");
                return RedirectToPage(new { poiId });
            }

            var staticUrl = res.Headers.TryGetValues("X-Static-Url", out var vals) ? vals.FirstOrDefault() : null;
            if (string.IsNullOrWhiteSpace(staticUrl))
            {
                TempData["ErrorMessage"] = "TTS tạo xong nhưng thiếu URL file.";
                return RedirectToPage(new { poiId });
            }

            staticUrl = ToAbsoluteApiUrl(client, staticUrl);

            var model = new AudioModel
            {
                PoiId = poiId,
                Url = staticUrl,
                LanguageCode = string.IsNullOrWhiteSpace(language) ? "vi" : language.Trim().ToLowerInvariant(),
                IsTts = true,
                IsProcessed = true
            };

            var submitRes = await client.PostAsJsonAsync("api/audio/upload-reference", model);
            if (!submitRes.IsSuccessStatusCode)
            {
                var submitBody = await submitRes.Content.ReadAsStringAsync();
                TempData["ErrorMessage"] = "Lưu TTS thất bại." + (string.IsNullOrWhiteSpace(submitBody) ? string.Empty : $" {submitBody}");
                return RedirectToPage(new { poiId });
            }

            var warning = res.Headers.TryGetValues("X-TTS-Warning", out var warningVals) ? warningVals.FirstOrDefault() : null;
            TempData["SuccessMessage"] = string.IsNullOrWhiteSpace(warning)
                ? "Đã tạo và lưu TTS thành công."
                : "Đã tạo và lưu TTS (đang dùng fallback).";

            return RedirectToPage(new { poiId });
        }

        public async Task<IActionResult> OnPostGenerateTtsAllAsync(int poiId)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            var client = _factory.CreateClient("api");
            ConfigureApiClient(client, uid);
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            var res = await client.PostAsync($"api/audio/tts/generate-all/{poiId}", null);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                TempData["ErrorMessage"] = "Gửi yêu cầu generate TTS tất cả ngôn ngữ thất bại: " + res.StatusCode + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" - {body}");
                return RedirectToPage(new { poiId });
            }

            TempData["SuccessMessage"] = "Đã generate TTS tất cả ngôn ngữ.";

            return RedirectToPage(new { poiId });
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

        private static string ToAbsoluteApiUrl(HttpClient client, string? rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return string.Empty;
            if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var absolute)) return absolute.ToString();
            if (client.BaseAddress == null) return rawUrl;
            return new Uri(client.BaseAddress, rawUrl.TrimStart('/')).ToString();
        }
    }
}
