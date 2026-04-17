using Microsoft.AspNetCore.Mvc;
using VinhKhanh.API.Data;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.Shared;

namespace VinhKhanh.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AnalyticsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AnalyticsController(AppDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> PostTrace([FromBody] TraceLog trace)
        {
            if (trace == null) return BadRequest();

            trace.ExtraJson ??= string.Empty;

            // Anti-spam: nếu QR scan > 5 lần / phút / (device + poi) thì bỏ qua không lưu
            if (trace.ExtraJson.Contains("\"event\":\"qr_scan\"", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(trace.DeviceId)
                && trace.PoiId > 0)
            {
                var since = DateTime.UtcNow.AddMinutes(-1);
                var recentQrCount = await _db.TraceLogs
                    .Where(t => t.TimestampUtc >= since
                                && t.PoiId == trace.PoiId
                                && t.DeviceId == trace.DeviceId
                                && t.ExtraJson != null
                                && t.ExtraJson.Contains("\"event\":\"qr_scan\""))
                    .CountAsync();

                if (recentQrCount >= 5)
                {
                    return Ok(new
                    {
                        ignored = true,
                        reason = "qr_scan_rate_limited",
                        limit = 5,
                        windowSeconds = 60,
                        poiId = trace.PoiId
                    });
                }
            }

            trace.TimestampUtc = DateTime.UtcNow;
            _db.TraceLogs.Add(trace);
            await _db.SaveChangesAsync();
            return Ok(trace);
        }

        [HttpGet("poi-live-stats")]
        public async Task<IActionResult> GetPoiLiveStats(double? userLat = null, double? userLng = null, int top = 50)
        {
            top = Math.Clamp(top, 1, 200);

            var pois = await _db.PointsOfInterest.AsNoTracking().ToListAsync();
            var now = DateTime.UtcNow;
            var last5m = now.AddMinutes(-5);
            var last30m = now.AddMinutes(-30);
            var last7d = now.AddDays(-7);

            var logs7d = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.TimestampUtc >= last7d)
                .ToListAsync();

            bool IsEvent(TraceLog t, string eventName)
            {
                return !string.IsNullOrWhiteSpace(t.ExtraJson)
                       && t.ExtraJson.Contains($"\"event\":\"{eventName}\"", StringComparison.OrdinalIgnoreCase);
            }

            var stats = new List<PoiLiveStatsDto>();

            foreach (var poi in pois)
            {
                var poiLogs = logs7d.Where(x => x.PoiId == poi.Id).ToList();

                var activeUsers = poiLogs
                    .Where(x => x.TimestampUtc >= last5m && (IsEvent(x, "poi_heartbeat") || IsEvent(x, "poi_enter") || IsEvent(x, "qr_scan")))
                    .Select(x => x.DeviceId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                var enRouteUsers = poiLogs
                    .Where(x => x.TimestampUtc >= last30m && IsEvent(x, "navigation_start"))
                    .Select(x => x.DeviceId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                var visitedUsers = poiLogs
                    .Where(x => IsEvent(x, "navigation_arrived") || IsEvent(x, "poi_enter") || IsEvent(x, "qr_scan"))
                    .Select(x => x.DeviceId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                var qrScanCount = poiLogs.Count(x => IsEvent(x, "qr_scan"));

                var rating = 0.0;
                var content = await _db.PointContents.AsNoTracking().FirstOrDefaultAsync(c => c.PoiId == poi.Id && c.LanguageCode == "vi")
                             ?? await _db.PointContents.AsNoTracking().FirstOrDefaultAsync(c => c.PoiId == poi.Id);
                if (content != null && content.Rating > 0) rating = content.Rating;

                var isOpen = false;
                if (!string.IsNullOrWhiteSpace(content?.OpeningHours))
                {
                    var parts = content.OpeningHours.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                    if (parts.Length == 2 && TimeSpan.TryParse(parts[0], out var start) && TimeSpan.TryParse(parts[1], out var end))
                    {
                        var localNow = DateTime.Now.TimeOfDay;
                        isOpen = start <= end ? (localNow >= start && localNow <= end) : (localNow >= start || localNow <= end);
                    }
                }

                double? distanceMeters = null;
                if (userLat.HasValue && userLng.HasValue)
                {
                    distanceMeters = HaversineDistanceMeters(userLat.Value, userLng.Value, poi.Latitude, poi.Longitude);
                }

                var sponsoredWeight = Math.Clamp(poi.Priority / 10.0, 0.0, 1.5);

                var distanceScore = distanceMeters.HasValue
                    ? Math.Max(0, 1.0 - (distanceMeters.Value / 3000.0))
                    : 0.5;
                var openScore = isOpen ? 1.0 : 0.25;
                var ratingScore = Math.Clamp(rating / 5.0, 0.0, 1.0);
                var socialProofScore = Math.Min(1.0, ((activeUsers * 1.2) + (enRouteUsers * 0.8) + (Math.Min(visitedUsers, 40) * 0.2)) / 20.0);

                var conversionScore =
                    (openScore * 0.25) +
                    (distanceScore * 0.20) +
                    (ratingScore * 0.20) +
                    (sponsoredWeight * 0.15) +
                    (socialProofScore * 0.20);

                var isHot = activeUsers >= 3 || enRouteUsers >= 5 || qrScanCount >= 15;

                stats.Add(new PoiLiveStatsDto
                {
                    PoiId = poi.Id,
                    PoiName = poi.Name,
                    IsHot = isHot,
                    ActiveUsers = activeUsers,
                    EnRouteUsers = enRouteUsers,
                    VisitedUsers = visitedUsers,
                    QrScanCount = qrScanCount,
                    Rating = rating,
                    IsOpen = isOpen,
                    DistanceMeters = distanceMeters,
                    SponsoredWeight = sponsoredWeight,
                    ConversionScore = Math.Round(conversionScore, 4)
                });
            }

            var ranked = stats
                .OrderByDescending(x => x.ConversionScore)
                .ThenByDescending(x => x.ActiveUsers)
                .ThenBy(x => x.DistanceMeters ?? double.MaxValue)
                .Take(top)
                .ToList();

            return Ok(ranked);
        }

        [HttpGet("topPois")]
        public async Task<IActionResult> GetTopPois(int top = 10)
        {
            var q = _db.TraceLogs
                .GroupBy(t => t.PoiId)
                .Select(g => new { PoiId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(top);

            return Ok(await q.ToListAsync());
        }

        [HttpGet("avg-duration")]
        public async Task<IActionResult> GetAvgDuration(int poiId)
        {
            var q = await _db.TraceLogs
                .Where(t => t.PoiId == poiId && t.DurationSeconds.HasValue)
                .Select(t => t.DurationSeconds.Value)
                .ToListAsync();

            if (!q.Any()) return Ok(new { poiId, avg = 0.0 });
            return Ok(new { poiId, avg = q.Average() });
        }

        [HttpGet("heatmap")]
        public async Task<IActionResult> GetHeatmap(int limit = 200, int hours = 24)
        {
            limit = Math.Clamp(limit, 1, 5000);
            hours = Math.Clamp(hours, 1, 168);
            var since = DateTime.UtcNow.AddHours(-hours);

            var points = await _db.TraceLogs
                .Where(t => t.TimestampUtc >= since)
                .Where(t => t.Latitude >= -90 && t.Latitude <= 90 && t.Longitude >= -180 && t.Longitude <= 180)
                .Where(t => !(Math.Abs(t.Latitude) < 0.000001 && Math.Abs(t.Longitude) < 0.000001))
                .Where(t => t.ExtraJson != null && (
                    t.ExtraJson.Contains("poi_heartbeat") ||
                    t.ExtraJson.Contains("poi_enter") ||
                    t.ExtraJson.Contains("navigation_arrived") ||
                    t.ExtraJson.Contains("play")))
                .OrderByDescending(t => t.TimestampUtc)
                .Take(limit)
                .Select(t => new { t.Latitude, t.Longitude })
                .ToListAsync();
            return Ok(points);
        }

        // NEW: API trả về lịch sử sử dụng (TraceLog) cho admin
        [HttpGet("logs")]
        public async Task<IActionResult> GetLogs(int limit = 200)
        {
            var logs = await _db.TraceLogs
                .OrderByDescending(t => t.TimestampUtc)
                .Take(limit)
                .ToListAsync();
            return Ok(logs);
        }

        // NEW: thống kê số lượt quét QR theo POI
        [HttpGet("qr-scan-counts")]
        public async Task<IActionResult> GetQrScanCounts(int top = 50)
        {
            var q = await _db.TraceLogs
                .Where(t => t.ExtraJson != null && t.ExtraJson.Contains("\"event\":\"qr_scan\""))
                .GroupBy(t => t.PoiId)
                .Select(g => new { PoiId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(top)
                .ToListAsync();

            return Ok(q);
        }

        [HttpGet("web-qr-metrics")]
        public async Task<IActionResult> GetWebQrMetrics()
        {
            var logs = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.ExtraJson != null)
                .ToListAsync();

            logs = logs
                .Where(t => !string.IsNullOrWhiteSpace(t.ExtraJson)
                            && t.ExtraJson!.Contains("\"source\":\"web_public_qr\"", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var webQrScan = logs.Count(t => t.ExtraJson!.Contains("\"event\":\"qr_scan\"", StringComparison.OrdinalIgnoreCase));
            var webListenStart = logs.Count(t => t.ExtraJson!.Contains("\"event\":\"listen_start\"", StringComparison.OrdinalIgnoreCase));
            var webListenComplete = logs.Count(t => t.ExtraJson!.Contains("\"event\":\"listen_complete\"", StringComparison.OrdinalIgnoreCase));

            return Ok(new
            {
                web_qr_scan = webQrScan,
                web_listen_start = webListenStart,
                web_listen_complete = webListenComplete
            });
        }

        [HttpGet("timeseries")]
        public async Task<IActionResult> GetTimeseries(int hours = 24, int days = 7)
        {
            hours = Math.Clamp(hours, 1, 168);
            days = Math.Clamp(days, 1, 30);

            var now = DateTime.UtcNow;
            var hourSince = now.AddHours(-hours);
            var daySince = now.AddDays(-days);

            var logsForHours = await _db.TraceLogs
                .AsNoTracking()
                .Where(x => x.TimestampUtc >= hourSince)
                .ToListAsync();

            var logsForDays = await _db.TraceLogs
                .AsNoTracking()
                .Where(x => x.TimestampUtc >= daySince)
                .ToListAsync();

            var hourly = logsForHours
                .GroupBy(x => new DateTime(x.TimestampUtc.Year, x.TimestampUtc.Month, x.TimestampUtc.Day, x.TimestampUtc.Hour, 0, 0, DateTimeKind.Utc))
                .Select(g => new
                {
                    Time = g.Key,
                    TotalEvents = g.Count(),
                    QrScans = g.Count(x => !string.IsNullOrWhiteSpace(x.ExtraJson) && x.ExtraJson.Contains("\"event\":\"qr_scan\"")),
                    NavigationStarts = g.Count(x => !string.IsNullOrWhiteSpace(x.ExtraJson) && x.ExtraJson.Contains("\"event\":\"navigation_start\"")),
                    ActiveHeartbeats = g.Count(x => !string.IsNullOrWhiteSpace(x.ExtraJson) && x.ExtraJson.Contains("\"event\":\"poi_heartbeat\""))
                })
                .OrderBy(x => x.Time)
                .ToList();

            var daily = logsForDays
                .GroupBy(x => x.TimestampUtc.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    TotalEvents = g.Count(),
                    UniqueDevices = g.Select(x => x.DeviceId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Count(),
                    QrScans = g.Count(x => !string.IsNullOrWhiteSpace(x.ExtraJson) && x.ExtraJson.Contains("\"event\":\"qr_scan\"")),
                    Visits = g.Count(x => !string.IsNullOrWhiteSpace(x.ExtraJson) && (x.ExtraJson.Contains("\"event\":\"poi_enter\"") || x.ExtraJson.Contains("\"event\":\"navigation_arrived\"")))
                })
                .OrderBy(x => x.Date)
                .ToList();

            return Ok(new { Hourly = hourly, Daily = daily });
        }

        private static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000;
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double ToRadians(double deg) => deg * (Math.PI / 180.0);
    }
}
