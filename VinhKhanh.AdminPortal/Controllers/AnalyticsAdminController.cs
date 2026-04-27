using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
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
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            try
            {
                var top = await SafeGetAsync(() => client.GetFromJsonAsync<List<TopPoiDto>>("api/analytics/topPois?top=10"), new List<TopPoiDto>());
                var heatmap = await SafeGetAsync(() => client.GetFromJsonAsync<List<HeatmapPointDto>>("api/analytics/heatmap?limit=200"), new List<HeatmapPointDto>());
                var poiOverview = await SafeGetAsync(() => client.GetFromJsonAsync<List<AdminPoiOverviewDto>>("admin/pois/overview"), new List<AdminPoiOverviewDto>());
                var engagement = await SafeGetAsync(() => client.GetFromJsonAsync<List<PoiEngagementDto>>("api/analytics/engagement?top=20&hours=168"), new List<PoiEngagementDto>());
                var activeUsers = await SafeGetAsync(() => client.GetFromJsonAsync<List<ActiveUserDto>>("api/analytics/active-users?hours=72&top=120"), new List<ActiveUserDto>());
                var timeseries = await SafeGetAsync(() => client.GetFromJsonAsync<System.Text.Json.JsonElement>("api/analytics/timeseries?hours=24&days=7"), default(System.Text.Json.JsonElement));
                var appListenMetrics = await SafeGetAsync(() => client.GetFromJsonAsync<System.Text.Json.JsonElement>("api/analytics/app-listen-metrics"), default(System.Text.Json.JsonElement));
                var qrScanCounts = await SafeGetAsync(() => client.GetFromJsonAsync<List<QrScanCountDto>>("api/analytics/qr-scan-counts?top=50"), new List<QrScanCountDto>());
                var summary = await SafeGetAsync(() => client.GetFromJsonAsync<AnalyticsSummaryDto>("api/analytics/summary"), new AnalyticsSummaryDto());
                var topVisitedToday = await SafeGetAsync(() => client.GetFromJsonAsync<List<TopVisitedTodayDto>>("api/analytics/top-visited-today?top=20"), new List<TopVisitedTodayDto>());

                top ??= new List<TopPoiDto>();
                poiOverview ??= new List<AdminPoiOverviewDto>();

                var ownerByPoi = poiOverview
                    .GroupBy(p => p.Id)
                    .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.OwnerName ?? string.Empty);

                foreach (var item in top)
                {
                    if (item == null) continue;
                    if (!string.IsNullOrWhiteSpace(item.OwnerName)) continue;

                    if (ownerByPoi.TryGetValue(item.PoiId, out var ownerName))
                    {
                        item.OwnerName = ownerName ?? string.Empty;
                    }
                }

                ViewData["TopPois"] = top;
                ViewData["Heatmap"] = heatmap ?? new List<HeatmapPointDto>();
                ViewData["PoiOverview"] = poiOverview;
                ViewData["Engagement"] = engagement ?? new List<PoiEngagementDto>();
                ViewData["ActiveUsers"] = activeUsers ?? new List<ActiveUserDto>();
                ViewData["Timeseries"] = timeseries;
                ViewData["AppListenMetrics"] = appListenMetrics;
                ViewData["QrScanCounts"] = qrScanCounts ?? new List<QrScanCountDto>();
                ViewData["TopVisitedToday"] = topVisitedToday ?? new List<TopVisitedTodayDto>();
                ViewData["Summary"] = summary ?? new AnalyticsSummaryDto();
                ViewData["GoogleMapsApiKey"] = _config["GoogleMaps:ApiKey"] ?? string.Empty;
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading analytics");
                TempData["Error"] = "Không thể tải analytics: " + ex.Message;

                ViewData["TopPois"] = new List<TopPoiDto>();
                ViewData["Heatmap"] = new List<HeatmapPointDto>();
                ViewData["PoiOverview"] = new List<AdminPoiOverviewDto>();
                ViewData["Engagement"] = new List<PoiEngagementDto>();
                ViewData["ActiveUsers"] = new List<ActiveUserDto>();
                ViewData["Timeseries"] = default(System.Text.Json.JsonElement);
                ViewData["AppListenMetrics"] = default(System.Text.Json.JsonElement);
                ViewData["QrScanCounts"] = new List<QrScanCountDto>();
                ViewData["TopVisitedToday"] = new List<TopVisitedTodayDto>();
                ViewData["Summary"] = new AnalyticsSummaryDto();
                ViewData["GoogleMapsApiKey"] = _config["GoogleMaps:ApiKey"] ?? string.Empty;
                return View();
            }
        }

        private async Task<T> SafeGetAsync<T>(Func<Task<T>> fetch, T fallback)
        {
            try
            {
                var result = await fetch();
                return result is null ? fallback : result;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Analytics endpoint request failed");
                return fallback;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Analytics endpoint timed out");
                return fallback;
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning(ex, "Analytics endpoint response not supported");
                return fallback;
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Analytics endpoint JSON parse failed");
                return fallback;
            }
        }
    }
}
