using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.AdminPortal.Models;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class AdminPoiRegistrationsController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly IConfiguration _config;
        private readonly ILogger<AdminPoiRegistrationsController> _logger;

        public AdminPoiRegistrationsController(IHttpClientFactory factory, IConfiguration config, ILogger<AdminPoiRegistrationsController> logger)
        {
            _factory = factory;
            _config = config;
            _logger = logger;
        }

        private string GetApiKey()
        {
            var configured = _config?["ApiKey"];
            return !string.IsNullOrEmpty(configured) ? configured : "admin123";
        }

        /// <summary>
        /// View all pending POI registrations
        /// </summary>
        public async Task<IActionResult> Pending()
        {
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

                var response = await client.GetAsync("api/poiregistration/pending");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"API returned {response.StatusCode}: {errorContent}");
                    TempData["Error"] = $"Lỗi khi tải danh sách chờ duyệt: Response status code does not indicate success: {(int)response.StatusCode} ({response.StatusCode}).";
                    return View(new List<PoiRegistrationDto>());
                }

                var registrations = await response.Content.ReadFromJsonAsync<List<PoiRegistrationDto>>();
                return View(registrations ?? new List<PoiRegistrationDto>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching pending registrations");
                TempData["Error"] = "Lỗi khi tải danh sách chờ duyệt: " + ex.Message;
                return View(new List<PoiRegistrationDto>());
            }
        }

        /// <summary>
        /// View details of a pending POI registration
        /// </summary>
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

                var registration = await client.GetFromJsonAsync<PoiRegistrationDto>($"api/poiregistration/{id}");
                if (registration == null) return NotFound();

                return View(registration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching registration details");
                TempData["Error"] = "Lỗi khi tải chi tiết: " + ex.Message;
                return RedirectToAction("Pending");
            }
        }

        /// <summary>
        /// Admin approves a POI registration
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id)
        {
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

                var notes = Request.Form["Notes"].ToString();
                var request = new { Notes = notes, ReviewedBy = 1 };

                var res = await client.PostAsJsonAsync($"api/poiregistration/{id}/approve", request);
                if (res.IsSuccessStatusCode)
                {
                    TempData["Success"] = "POI đã được duyệt thành công!";
                    return RedirectToAction("Pending");
                }

                TempData["Error"] = "Duyệt POI thất bại";
                return RedirectToAction("Details", new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving POI");
                TempData["Error"] = "Lỗi: " + ex.Message;
                return RedirectToAction("Details", new { id });
            }
        }

        /// <summary>
        /// Admin rejects a POI registration
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id)
        {
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

                var notes = Request.Form["RejectReason"].ToString();
                var request = new { Notes = notes, ReviewedBy = 1 };

                var res = await client.PostAsJsonAsync($"api/poiregistration/{id}/reject", request);
                if (res.IsSuccessStatusCode)
                {
                    TempData["Success"] = "POI đã bị từ chối.";
                    return RedirectToAction("Pending");
                }

                TempData["Error"] = "Từ chối POI thất bại";
                return RedirectToAction("Details", new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting POI");
                TempData["Error"] = "Lỗi: " + ex.Message;
                return RedirectToAction("Details", new { id });
            }
        }
    }
}
