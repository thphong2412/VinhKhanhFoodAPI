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

        public List<AudioListItem> Audios { get; set; } = new();
        public int PoiId { get; set; }

        public OwnerAudioModel(IHttpClientFactory factory, ILogger<OwnerAudioModel> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync(int poiId)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            PoiId = poiId;
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-Owner-Id");
            client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
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
            client.DefaultRequestHeaders.Remove("X-Owner-Id");
            client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
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

            var eventNote = $"owner_audio_update::{(string.IsNullOrWhiteSpace(language) ? "vi" : language.Trim().ToLowerInvariant())}::{(string.IsNullOrWhiteSpace(requestedFileName) ? "audio.mp3" : requestedFileName)}::{base64}";

            var payload = new
            {
                OwnerId = uid,
                Name = poi.Name,
                Category = poi.Category,
                Latitude = poi.Latitude,
                Longitude = poi.Longitude,
                Radius = poi.Radius,
                Priority = poi.Priority,
                CooldownSeconds = poi.CooldownSeconds,
                ImageUrl = poi.ImageUrl,
                WebsiteUrl = poi.WebsiteUrl,
                QrCode = poi.QrCode,
                RequestType = "update",
                TargetPoiId = poiId,
                Status = "pending",
                ReviewNotes = eventNote
            };

            var res = await client.PostAsJsonAsync($"api/poiregistration/submit-update/{poiId}", payload);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                TempData["ErrorMessage"] = "Gửi yêu cầu upload audio thất bại." + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" {body}");
            }
            else
            {
                TempData["SuccessMessage"] = "Đã gửi yêu cầu upload audio, chờ admin duyệt.";
            }

            return RedirectToPage(new { poiId });
        }

        public async Task<IActionResult> OnPostUpdateMetadataAsync(int poiId, int id, string languageCode, string? fileName)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-Owner-Id");
            client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            var payload = new
            {
                eventType = "owner_audio_metadata_update",
                audioId = id,
                languageCode = string.IsNullOrWhiteSpace(languageCode) ? "vi" : languageCode.Trim().ToLowerInvariant(),
                fileName = string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim()
            };

            var registrationPayload = new
            {
                OwnerId = uid,
                Name = poi.Name,
                Category = poi.Category,
                Latitude = poi.Latitude,
                Longitude = poi.Longitude,
                Radius = poi.Radius,
                Priority = poi.Priority,
                CooldownSeconds = poi.CooldownSeconds,
                ImageUrl = poi.ImageUrl,
                WebsiteUrl = poi.WebsiteUrl,
                QrCode = poi.QrCode,
                RequestType = "update",
                TargetPoiId = poiId,
                Status = "pending",
                ReviewNotes = JsonSerializer.Serialize(payload)
            };

            var submitRes = await client.PostAsJsonAsync($"api/poiregistration/submit-update/{poiId}", registrationPayload);
            if (!submitRes.IsSuccessStatusCode)
            {
                var body = await submitRes.Content.ReadAsStringAsync();
                TempData["ErrorMessage"] = "Gửi yêu cầu sửa metadata audio thất bại." + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" {body}");
            }
            else
            {
                TempData["SuccessMessage"] = "Đã gửi yêu cầu sửa tên file/ngôn ngữ audio, chờ admin duyệt.";
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
            client.DefaultRequestHeaders.Remove("X-Owner-Id");
            client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
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

            var eventPayload = new
            {
                eventType = "owner_tts_update",
                languageCode = string.IsNullOrWhiteSpace(language) ? "vi" : language.Trim().ToLowerInvariant(),
                url = staticUrl,
                isTts = true
            };

            var payload = new
            {
                OwnerId = uid,
                Name = poi.Name,
                Category = poi.Category,
                Latitude = poi.Latitude,
                Longitude = poi.Longitude,
                Radius = poi.Radius,
                Priority = poi.Priority,
                CooldownSeconds = poi.CooldownSeconds,
                ImageUrl = poi.ImageUrl,
                WebsiteUrl = poi.WebsiteUrl,
                QrCode = poi.QrCode,
                RequestType = "update",
                TargetPoiId = poiId,
                Status = "pending",
                ReviewNotes = JsonSerializer.Serialize(eventPayload)
            };

            var submitRes = await client.PostAsJsonAsync($"api/poiregistration/submit-update/{poiId}", payload);
            if (!submitRes.IsSuccessStatusCode)
            {
                var submitBody = await submitRes.Content.ReadAsStringAsync();
                TempData["ErrorMessage"] = "Gửi yêu cầu TTS thất bại." + (string.IsNullOrWhiteSpace(submitBody) ? string.Empty : $" {submitBody}");
                return RedirectToPage(new { poiId });
            }

            var warning = res.Headers.TryGetValues("X-TTS-Warning", out var warningVals) ? warningVals.FirstOrDefault() : null;
            TempData["SuccessMessage"] = string.IsNullOrWhiteSpace(warning)
                ? "Đã gửi yêu cầu tạo TTS, chờ admin duyệt."
                : "Đã gửi yêu cầu tạo TTS (đang dùng fallback), chờ admin duyệt.";

            return RedirectToPage(new { poiId });
        }

        public async Task<IActionResult> OnPostGenerateTtsAllAsync(int poiId)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-Owner-Id");
            client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            var payload = new
            {
                OwnerId = uid,
                Name = poi.Name,
                Category = poi.Category,
                Latitude = poi.Latitude,
                Longitude = poi.Longitude,
                Radius = poi.Radius,
                Priority = poi.Priority,
                CooldownSeconds = poi.CooldownSeconds,
                ImageUrl = poi.ImageUrl,
                WebsiteUrl = poi.WebsiteUrl,
                QrCode = poi.QrCode,
                RequestType = "update",
                TargetPoiId = poiId,
                Status = "pending",
                ReviewNotes = JsonSerializer.Serialize(new
                {
                    eventType = "owner_tts_generate_all",
                    note = "Owner yêu cầu tạo TTS cho toàn bộ ngôn ngữ"
                })
            };

            var res = await client.PostAsJsonAsync($"api/poiregistration/submit-update/{poiId}", payload);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                TempData["ErrorMessage"] = "Gửi yêu cầu generate TTS tất cả ngôn ngữ thất bại: " + res.StatusCode + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" - {body}");
                return RedirectToPage(new { poiId });
            }

            TempData["SuccessMessage"] = "Đã gửi yêu cầu generate TTS tất cả ngôn ngữ, chờ admin duyệt.";

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
