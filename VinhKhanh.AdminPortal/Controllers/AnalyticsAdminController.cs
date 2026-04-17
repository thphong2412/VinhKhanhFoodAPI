using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.AdminPortal.Models;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class AnalyticsAdminController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<AnalyticsAdminController> _logger;
        private readonly IConfiguration _config;

        public AnalyticsAdminController(IHttpClientFactory factory, ILogger<AnalyticsAdminController> logger, IConfiguration config)
        {
            _factory = factory;
            _logger = logger;
            _config = config;
        }

        private string GetApiKey()
        {
            try
            {
                var configured = _config?["ApiKey"];
                if (!string.IsNullOrEmpty(configured)) return configured;
            }
            catch { }
            return "admin123";
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

                var top = await client.GetFromJsonAsync<List<TopPoiDto>>("api/analytics/topPois?top=10");
                var heatmap = await client.GetFromJsonAsync<List<HeatmapPointDto>>("api/analytics/heatmap?limit=200");
                var liveStats = await client.GetFromJsonAsync<List<VinhKhanh.Shared.PoiLiveStatsDto>>("api/analytics/poi-live-stats?top=30");
                var timeseries = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("api/analytics/timeseries?hours=24&days=7");
                var webQrMetrics = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("api/analytics/web-qr-metrics");
                ViewData["TopPois"] = top ?? new List<TopPoiDto>();
                ViewData["Heatmap"] = heatmap ?? new List<HeatmapPointDto>();
                ViewData["LiveStats"] = liveStats ?? new List<VinhKhanh.Shared.PoiLiveStatsDto>();
                ViewData["Timeseries"] = timeseries;
                ViewData["WebQrMetrics"] = webQrMetrics;
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading analytics");
                TempData["Error"] = "Không thể tải analytics: " + ex.Message;
                return View(new { TopPois = new List<TopPoiDto>(), Heatmap = new List<HeatmapPointDto>() });
            }
        }
    }
}
