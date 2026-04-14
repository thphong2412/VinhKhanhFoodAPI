using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class CreatePoiModel : PageModel
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<CreatePoiModel> _logger;

        [BindProperty]
        public string Name { get; set; }
        [BindProperty]
        public string Category { get; set; }
        [BindProperty]
        public double Latitude { get; set; }
        [BindProperty]
        public double Longitude { get; set; }
        [BindProperty]
        public double Radius { get; set; } = 50;
        [BindProperty]
        public int Priority { get; set; } = 1;
        [BindProperty]
        public int CooldownSeconds { get; set; } = 300;
        [BindProperty]
        public string? ImageUrl { get; set; }
        [BindProperty]
        public string? WebsiteUrl { get; set; }
        [BindProperty]
        public string? QrCode { get; set; }

        public string? SuccessMessage { get; set; }

        public CreatePoiModel(IHttpClientFactory factory, ILogger<CreatePoiModel> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v)) return RedirectToPage("Login");
            if (!int.TryParse(v, out var uid)) return RedirectToPage("Login");

            if (!ModelState.IsValid)
            {
                return Page();
            }

            try
            {
                var registration = new
                {
                    OwnerId = uid,
                    Name = Name,
                    Category = Category,
                    Latitude = Latitude,
                    Longitude = Longitude,
                    Radius = Radius,
                    Priority = Priority,
                    CooldownSeconds = CooldownSeconds,
                    ImageUrl = ImageUrl,
                    WebsiteUrl = WebsiteUrl,
                    QrCode = QrCode,
                    Status = "pending"
                };

                var client = _factory.CreateClient("api");
                var res = await client.PostAsJsonAsync("api/poiregistration/submit", registration);

                if (!res.IsSuccessStatusCode)
                {
                    var errorContent = await res.Content.ReadAsStringAsync();
                    _logger.LogWarning("POI registration failed: {Status} {Content}", res.StatusCode, errorContent);
                    ModelState.AddModelError("", "Tạo POI thất bại: " + res.StatusCode);
                    return Page();
                }

                // Show success and redirect to MyPois
                TempData["SuccessMessage"] = "POI đã được gửi chờ duyệt! Admin sẽ xem xét sớm.";
                return RedirectToPage("MyPois");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating POI registration");
                ModelState.AddModelError("", "Lỗi: " + ex.Message);
                return Page();
            }
        }
    }
}
