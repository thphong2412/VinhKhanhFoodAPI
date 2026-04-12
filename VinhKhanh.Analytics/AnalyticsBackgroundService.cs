using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VinhKhanh.Shared;

namespace VinhKhanh.Analytics
{
    public class AnalyticsBackgroundService : BackgroundService
    {
        private readonly ILogger<AnalyticsBackgroundService> _logger;
        private readonly HttpClient _http;

        public AnalyticsBackgroundService(ILogger<AnalyticsBackgroundService> logger)
        {
            _logger = logger;
            _http = new HttpClient();
            // assume API runs locally for POC
            _http.BaseAddress = new Uri("https://localhost:5001/");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AnalyticsBackgroundService started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Send a dummy anonymous trace (POC)
                    var trace = new TraceLog
                    {
                        PoiId = 0,
                        DeviceId = Environment.MachineName,
                        Latitude = 0,
                        Longitude = 0,
                        ExtraJson = "{}",
                        TimestampUtc = DateTime.UtcNow
                    };

                    await _http.PostAsJsonAsync("api/analytics", trace, stoppingToken);

                    // Retrieve top POIs for diagnostics
                    var top = await _http.GetFromJsonAsync<object>("api/analytics/topPois?top=5", stoppingToken);
                    _logger.LogInformation("Top pois: {0}", top);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Analytics worker error");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
