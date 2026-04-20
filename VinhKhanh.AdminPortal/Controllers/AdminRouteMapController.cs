using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.AdminPortal.Models;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Authorize]
    public class AdminRouteMapController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly IConfiguration _config;
        private readonly ILogger<AdminRouteMapController> _logger;

        public AdminRouteMapController(IHttpClientFactory factory, IConfiguration config, ILogger<AdminRouteMapController> logger)
        {
            _factory = factory;
            _config = config;
            _logger = logger;
        }

        public async Task<IActionResult> Index(int hours = 24)
        {
            var payload = await LoadRouteMapDataAsync(hours);
            return View(payload);
        }

        [HttpGet]
        public async Task<IActionResult> Data(int hours = 24)
        {
            var payload = await LoadRouteMapDataAsync(hours);
            return Json(payload);
        }

        private async Task<AdminRouteMapViewModel> LoadRouteMapDataAsync(int hours)
        {
            hours = Math.Clamp(hours, 1, 168);

            var vm = new AdminRouteMapViewModel
            {
                Hours = hours
            };

            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            try
            {
                vm.Pois = await client.GetFromJsonAsync<List<AdminPoiOverviewDto>>("admin/pois/overview") ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không tải được POI cho route map");
                vm.Pois = new();
            }

            try
            {
                vm.Routes = await client.GetFromJsonAsync<List<AnonymousUserRouteDto>>($"api/analytics/routes?hours={hours}&topUsers=120&maxPointsPerUser=260") ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không tải được route ẩn danh cho admin route map");
                vm.Routes = new();
            }

            return vm;
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

    public class AdminRouteMapViewModel
    {
        public int Hours { get; set; } = 24;
        public List<AdminPoiOverviewDto> Pois { get; set; } = new();
        public List<AnonymousUserRouteDto> Routes { get; set; } = new();
    }
}
