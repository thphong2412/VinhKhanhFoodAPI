using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class PoiDetailsModel : PageModel
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<PoiDetailsModel> _logger;

        public PoiModel Poi { get; set; }
        public string ApiBaseUrl { get; set; } = string.Empty;

        public PoiDetailsModel(IHttpClientFactory factory, ILogger<PoiDetailsModel> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v)) 
                return RedirectToPage("Login");

            if (!int.TryParse(v, out var uid)) 
                return RedirectToPage("Login");

            try
            {
                var client = _factory.CreateClient("api");
                ApiBaseUrl = client.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;
                client.DefaultRequestHeaders.Remove("X-Owner-Id");
                client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
                var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{id}");

                if (poi == null || poi.OwnerId != uid)
                {
                    return NotFound();
                }

                Poi = poi;
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading POI details");
                return NotFound();
            }
        }

        public async Task<IActionResult> OnPostRequestDeleteAsync(int id)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-Owner-Id");
                client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
                var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{id}");
                if (poi == null || poi.OwnerId != uid)
                    return NotFound();

                var payload = new
                {
                    OwnerId = uid,
                    Name = poi.Name,
                    Category = poi.Category,
                    RequestType = "delete",
                    TargetPoiId = id,
                    Status = "pending",
                    ReviewNotes = "Owner yêu cầu xóa POI"
                };

                var res = await client.PostAsJsonAsync($"api/poiregistration/submit-delete/{id}", payload);
                if (!res.IsSuccessStatusCode)
                {
                    TempData["ErrorMessage"] = "Gửi yêu cầu xóa thất bại.";
                }
                else
                {
                    TempData["SuccessMessage"] = "Đã gửi yêu cầu xóa POI, chờ admin duyệt.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting delete request from owner portal");
                TempData["ErrorMessage"] = "Lỗi khi gửi yêu cầu xóa.";
            }

            return RedirectToPage("MyPois");
        }
    }
}
