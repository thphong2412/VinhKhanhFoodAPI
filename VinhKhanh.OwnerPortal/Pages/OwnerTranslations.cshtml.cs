using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class OwnerTranslationsModel : PageModel
    {
        private readonly IHttpClientFactory _factory;

        public int PoiId { get; set; }
        public List<ContentModel> Contents { get; set; } = new();

        public OwnerTranslationsModel(IHttpClientFactory factory)
        {
            _factory = factory;
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

            Contents = await client.GetFromJsonAsync<List<ContentModel>>($"api/content/by-poi/{poiId}") ?? new();
            return Page();
        }

        public async Task<IActionResult> OnPostSubmitTranslationUpdateAsync(
            int poiId,
            string languageCode,
            string title,
            string? subtitle,
            string? description,
            string? priceMin,
            string? priceMax,
            double? rating,
            string? openTime,
            string? closeTime,
            string? phoneNumber,
            string? address)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-Owner-Id");
            client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            var note = System.Text.Json.JsonSerializer.Serialize(new
            {
                eventType = "owner_translation_update",
                languageCode = languageCode?.Trim().ToLowerInvariant(),
                title,
                subtitle,
                description,
                priceMin,
                priceMax,
                rating,
                openTime,
                closeTime,
                phoneNumber,
                address
            });

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
                ReviewNotes = note
            };

            var res = await client.PostAsJsonAsync($"api/poiregistration/submit-update/{poiId}", payload);
            if (!res.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = "Gửi yêu cầu cập nhật bản dịch thất bại.";
            }
            else
            {
                TempData["SuccessMessage"] = "Đã gửi yêu cầu cập nhật bản dịch, chờ admin duyệt.";
            }

            return RedirectToPage(new { poiId });
        }
    }
}
