using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class EditPoiModel : PageModel
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<EditPoiModel> _logger;

        [BindProperty]
        public PoiModel Poi { get; set; } = new();

        [BindProperty] public string? ContentTitle_VI { get; set; }
        [BindProperty] public string? ContentSubtitle_VI { get; set; }
        [BindProperty] public string? ContentDescription_VI { get; set; }
        [BindProperty] public string? ContentPriceMin_VI { get; set; }
        [BindProperty] public string? ContentPriceMax_VI { get; set; }
        [BindProperty] public double? ContentRating_VI { get; set; }
        [BindProperty] public string? ContentOpenTime_VI { get; set; }
        [BindProperty] public string? ContentCloseTime_VI { get; set; }
        [BindProperty] public string? ContentPhoneNumber_VI { get; set; }
        [BindProperty] public string? ContentAddress_VI { get; set; }

        public EditPoiModel(IHttpClientFactory factory, ILogger<EditPoiModel> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-Owner-Id");
            client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{id}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            Poi = poi;
            var vi = poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
            if (vi != null)
            {
                ContentTitle_VI = vi.Title;
                ContentSubtitle_VI = vi.Subtitle;
                ContentDescription_VI = vi.Description;
                ContentPriceMin_VI = vi.PriceMin;
                ContentPriceMax_VI = vi.PriceMax;
                ContentRating_VI = vi.Rating;
                ContentOpenTime_VI = vi.OpenTime;
                ContentCloseTime_VI = vi.CloseTime;
                ContentPhoneNumber_VI = vi.PhoneNumber;
                ContentAddress_VI = vi.Address;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-Owner-Id");
            client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
            var existing = await client.GetFromJsonAsync<PoiModel>($"api/poi/{id}");
            if (existing == null || existing.OwnerId != uid) return NotFound();

            var payload = new
            {
                OwnerId = uid,
                Name = Poi.Name,
                Category = Poi.Category,
                Latitude = Poi.Latitude,
                Longitude = Poi.Longitude,
                Radius = Poi.Radius,
                Priority = Poi.Priority,
                CooldownSeconds = Poi.CooldownSeconds,
                ImageUrl = Poi.ImageUrl,
                WebsiteUrl = Poi.WebsiteUrl,
                QrCode = Poi.QrCode,
                ContentTitle = ContentTitle_VI,
                ContentSubtitle = ContentSubtitle_VI,
                ContentDescription = ContentDescription_VI,
                ContentPriceMin = ContentPriceMin_VI,
                ContentPriceMax = ContentPriceMax_VI,
                ContentRating = ContentRating_VI,
                ContentOpenTime = ContentOpenTime_VI,
                ContentCloseTime = ContentCloseTime_VI,
                ContentPhoneNumber = ContentPhoneNumber_VI,
                ContentAddress = ContentAddress_VI,
                RequestType = "update",
                TargetPoiId = id,
                Status = "pending"
            };

            var res = await client.PostAsJsonAsync($"api/poiregistration/submit-update/{id}", payload);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                _logger.LogWarning("Submit update failed: {Status} {Body}", res.StatusCode, body);
                ModelState.AddModelError(string.Empty, "Gửi yêu cầu chỉnh sửa thất bại.");
                return await OnGetAsync(id);
            }

            TempData["SuccessMessage"] = "Đã gửi yêu cầu chỉnh sửa POI, chờ admin duyệt.";
            return RedirectToPage("MyPois");
        }
    }
}
