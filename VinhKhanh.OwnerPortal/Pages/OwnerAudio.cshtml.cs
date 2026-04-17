using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class OwnerAudioModel : PageModel
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<OwnerAudioModel> _logger;

        public List<AudioModel> Audios { get; set; } = new();
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

            Audios = await client.GetFromJsonAsync<List<AudioModel>>($"api/audio/by-poi/{poiId}") ?? new();
            return Page();
        }

        public async Task<IActionResult> OnPostUploadAsync(int poiId, string language)
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
            var base64 = Convert.ToBase64String(ms.ToArray());

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
                ReviewNotes = $"owner_audio_update::{(string.IsNullOrWhiteSpace(language) ? "vi" : language)}::{file.FileName}::{base64}"
            };

            var res = await client.PostAsJsonAsync($"api/poiregistration/submit-update/{poiId}", payload);
            if (!res.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = "Gửi yêu cầu cập nhật audio thất bại.";
            }
            else
            {
                TempData["SuccessMessage"] = "Đã gửi yêu cầu cập nhật audio, chờ admin duyệt.";
            }

            return RedirectToPage(new { poiId });
        }
    }
}
