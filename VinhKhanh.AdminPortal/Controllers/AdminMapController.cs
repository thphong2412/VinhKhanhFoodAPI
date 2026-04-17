using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.AdminPortal.Models;
using VinhKhanh.Shared;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Authorize]
    public class AdminMapController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly IConfiguration _config;
        private readonly ILogger<AdminMapController> _logger;

        public AdminMapController(IHttpClientFactory factory, IConfiguration config, ILogger<AdminMapController> logger)
        {
            _factory = factory;
            _config = config;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var pois = await LoadOverviewAsync();
            return View(pois);
        }

        [HttpGet]
        public async Task<IActionResult> PoiData()
        {
            var pois = await LoadOverviewAsync();
            return Json(pois);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePublish(int id, bool publish)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            var endpoint = publish ? $"admin/pois/{id}/publish" : $"admin/pois/{id}/unpublish";
            var res = await client.PostAsync(endpoint, null);
            if (!res.IsSuccessStatusCode)
            {
                return BadRequest(new { success = false, message = "toggle_publish_failed" });
            }

            return Ok(new { success = true });
        }

        private async Task<List<AdminPoiOverviewDto>> LoadOverviewAsync()
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            try
            {
                var pois = await client.GetFromJsonAsync<List<AdminPoiOverviewDto>>("admin/pois/overview");
                return (pois ?? new List<AdminPoiOverviewDto>())
                    .OrderBy(p => p.Priority)
                    .ThenBy(p => p.Name)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không tải được dữ liệu POI cho bản đồ admin");
                TempData["Error"] = "Không thể tải dữ liệu bản đồ từ API.";
                return new List<AdminPoiOverviewDto>();
            }
        }

        private string GetApiKey()
        {
            try
            {
                var configured = _config?["ApiKey"];
                if (!string.IsNullOrWhiteSpace(configured)) return configured;
            }
            catch { }

            return "admin123";
        }
    }
}
