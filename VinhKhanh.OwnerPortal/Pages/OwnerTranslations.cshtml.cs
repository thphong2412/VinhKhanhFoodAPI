using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Json;
using System.Text.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class OwnerTranslationsModel : PageModel
    {
        private readonly IHttpClientFactory _factory;

        public int PoiId { get; set; }
        public List<ContentModel> Contents { get; set; } = new();
        public string ViSourceJson { get; set; } = "{}";

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
            var vi = Contents.FirstOrDefault(x => string.Equals(x.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
            ViSourceJson = JsonSerializer.Serialize(new
            {
                title = vi?.Title,
                subtitle = vi?.Subtitle,
                description = vi?.Description,
                priceMin = vi?.PriceMin,
                priceMax = vi?.PriceMax,
                rating = vi?.Rating,
                openTime = vi?.OpenTime,
                closeTime = vi?.CloseTime,
                phoneNumber = vi?.PhoneNumber,
                address = vi?.Address
            });
            return Page();
        }

        public async Task<IActionResult> OnGetAutoTranslateAsync(int poiId, string languageCode)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return Unauthorized();

            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-Owner-Id");
            client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            var normalizedLang = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant();

            var contents = await client.GetFromJsonAsync<List<ContentModel>>($"api/content/by-poi/{poiId}") ?? new List<ContentModel>();
            var vi = contents.FirstOrDefault(x => string.Equals(x.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
            if (vi == null)
            {
                return NotFound(new { error = "missing_vi_source" });
            }

            var payload = new
            {
                targetLanguageCode = normalizedLang,
                source = new
                {
                    title = vi.Title,
                    subtitle = vi.Subtitle,
                    description = vi.Description,
                    priceMin = vi.PriceMin,
                    priceMax = vi.PriceMax,
                    rating = vi.Rating,
                    openTime = vi.OpenTime,
                    closeTime = vi.CloseTime,
                    phoneNumber = vi.PhoneNumber,
                    address = vi.Address
                }
            };

            var response = await client.PostAsJsonAsync("api/ai/translate-content", payload);
            if (!response.IsSuccessStatusCode)
            {
                return BadRequest(new { error = "auto_translate_failed" });
            }

            var body = await response.Content.ReadAsStringAsync();
            return Content(body, "application/json");
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

            var normalizedLang = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant();
            var existing = await client.GetFromJsonAsync<List<ContentModel>>($"api/content/by-poi/{poiId}") ?? new();
            var current = existing.FirstOrDefault(c => string.Equals(c.LanguageCode, normalizedLang, StringComparison.OrdinalIgnoreCase));

            var content = new ContentModel
            {
                Id = current?.Id ?? 0,
                PoiId = poiId,
                LanguageCode = normalizedLang,
                Title = title?.Trim(),
                Subtitle = subtitle?.Trim(),
                Description = description?.Trim(),
                PriceMin = priceMin,
                PriceMax = priceMax,
                Rating = rating ?? current?.Rating ?? 0,
                OpenTime = openTime?.Trim(),
                CloseTime = closeTime?.Trim(),
                PhoneNumber = phoneNumber?.Trim(),
                Address = address?.Trim(),
                AudioUrl = current?.AudioUrl,
                ShareUrl = current?.ShareUrl,
                IsTTS = current?.IsTTS ?? false
            };
            content.NormalizeCompositeFields();

            var eventPayload = new
            {
                eventType = "owner_translation_update",
                languageCode = normalizedLang,
                title = content.Title,
                subtitle = content.Subtitle,
                description = content.Description,
                priceMin = content.PriceMin,
                priceMax = content.PriceMax,
                rating = content.Rating,
                openTime = content.OpenTime,
                closeTime = content.CloseTime,
                phoneNumber = content.PhoneNumber,
                address = content.Address
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
                ContentTitle = string.Equals(normalizedLang, "vi", StringComparison.OrdinalIgnoreCase) ? content.Title : null,
                ContentSubtitle = string.Equals(normalizedLang, "vi", StringComparison.OrdinalIgnoreCase) ? content.Subtitle : null,
                ContentDescription = string.Equals(normalizedLang, "vi", StringComparison.OrdinalIgnoreCase) ? content.Description : null,
                ContentPriceMin = string.Equals(normalizedLang, "vi", StringComparison.OrdinalIgnoreCase) ? content.PriceMin : null,
                ContentPriceMax = string.Equals(normalizedLang, "vi", StringComparison.OrdinalIgnoreCase) ? content.PriceMax : null,
                ContentRating = string.Equals(normalizedLang, "vi", StringComparison.OrdinalIgnoreCase) ? (double?)content.Rating : null,
                ContentOpenTime = string.Equals(normalizedLang, "vi", StringComparison.OrdinalIgnoreCase) ? content.OpenTime : null,
                ContentCloseTime = string.Equals(normalizedLang, "vi", StringComparison.OrdinalIgnoreCase) ? content.CloseTime : null,
                ContentPhoneNumber = string.Equals(normalizedLang, "vi", StringComparison.OrdinalIgnoreCase) ? content.PhoneNumber : null,
                ContentAddress = string.Equals(normalizedLang, "vi", StringComparison.OrdinalIgnoreCase) ? content.Address : null,
                RequestType = "update",
                TargetPoiId = poiId,
                Status = "pending",
                ReviewNotes = JsonSerializer.Serialize(eventPayload)
            };

            var res = await client.PostAsJsonAsync($"api/poiregistration/submit-update/{poiId}", payload);

            if (!res.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = "Cập nhật bản dịch thất bại.";
            }
            else
            {
                TempData["SuccessMessage"] = "Đã gửi yêu cầu cập nhật bản dịch, chờ admin duyệt.";
            }

            return RedirectToPage(new { poiId });
        }
    }
}
