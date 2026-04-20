using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using VinhKhanh.API.Data;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.Shared;
using VinhKhanh.API.Hubs;

namespace VinhKhanh.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AnalyticsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<SyncHub> _hubContext;

        public AnalyticsController(AppDbContext db, IHubContext<SyncHub> hubContext)
        {
            _db = db;
            _hubContext = hubContext;
        }

        [HttpPost]
        public async Task<IActionResult> PostTrace([FromBody] TraceLog trace)
        {
            if (trace == null) return BadRequest();

            trace.ExtraJson ??= string.Empty;

            // Anti-spam: chặn spam theo event + device + poi theo từng cửa sổ thời gian linh hoạt
            var trackedEvents = new[] { "qr_scan", "tts_play", "audio_play", "listen_start", "audio_list_open", "poi_click", "poi_detail_open", "navigation_start", "poi_heartbeat" };
            var matchedEvent = trackedEvents.FirstOrDefault(ev => trace.ExtraJson.Contains($"\"event\":\"{ev}\"", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(matchedEvent)
                && !string.IsNullOrWhiteSpace(trace.DeviceId)
                && trace.PoiId > 0)
            {
                var (windowSeconds, maxCount) = matchedEvent switch
                {
                    "poi_heartbeat" => (90, 12),
                    "poi_enter" => (60, 3),
                    "tts_play" => (60, 6),
                    "audio_play" => (60, 6),
                    "listen_start" => (45, 8),
                    "poi_click" => (30, 8),
                    "poi_detail_open" => (30, 8),
                    "navigation_start" => (60, 4),
                    "qr_scan" => (90, 10),
                    _ => (60, 6)
                };

                var since = DateTime.UtcNow.AddSeconds(-windowSeconds);
                var recentCount = await _db.TraceLogs
                    .Where(t => t.TimestampUtc >= since
                                && t.PoiId == trace.PoiId
                                && t.DeviceId == trace.DeviceId
                                && t.ExtraJson != null
                                && t.ExtraJson.Contains($"\"event\":\"{matchedEvent}\""))
                    .CountAsync();

                if (recentCount >= maxCount)
                {
                    return Ok(new
                    {
                        ignored = true,
                        reason = "event_rate_limited",
                        eventName = matchedEvent,
                        limit = maxCount,
                        windowSeconds,
                        poiId = trace.PoiId
                    });
                }
            }

            trace.TimestampUtc = DateTime.UtcNow;
            _db.TraceLogs.Add(trace);
            await _db.SaveChangesAsync();

            try
            {
                await _hubContext.Clients.All.SendAsync("TraceLogged", new
                {
                    trace.PoiId,
                    trace.DeviceId,
                    trace.TimestampUtc,
                    trace.DurationSeconds,
                    trace.ExtraJson
                });
            }
            catch
            {
            }

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
            top = Math.Clamp(top, 1, 200);

            bool IsListenEvent(TraceLog t)
            {
                var extra = t.ExtraJson ?? string.Empty;
                return extra.Contains("\"event\":\"play\"", StringComparison.OrdinalIgnoreCase)
                       || extra.Contains("\"event\":\"tts_play\"", StringComparison.OrdinalIgnoreCase)
                       || extra.Contains("\"event\":\"audio_play\"", StringComparison.OrdinalIgnoreCase)
                       || extra.Contains("\"event\":\"listen_start\"", StringComparison.OrdinalIgnoreCase);
            }

            var traces = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.PoiId > 0)
                .ToListAsync();

            var grouped = traces
                .Where(IsListenEvent)
                .GroupBy(t => t.PoiId)
                .Select(g => new { PoiId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(top)
                .ToList();

            var poiIds = grouped.Select(x => x.PoiId).ToList();
            var poiNames = await _db.PointsOfInterest
                .AsNoTracking()
                .Where(p => poiIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name ?? string.Empty);

            var result = grouped.Select(x => new
            {
                x.PoiId,
                x.Count,
                PoiName = poiNames.TryGetValue(x.PoiId, out var name) ? name : string.Empty
            });

            return Ok(result);
        }

        [HttpGet("engagement")]
        public async Task<IActionResult> GetEngagement(int top = 20, int hours = 72)
        {
            top = Math.Clamp(top, 1, 200);
            hours = Math.Clamp(hours, 1, 24 * 30);

            var since = DateTime.UtcNow.AddHours(-hours);

            var logs = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.TimestampUtc >= since && t.PoiId > 0)
                .ToListAsync();

            bool IsEvent(TraceLog t, string eventName)
            {
                return !string.IsNullOrWhiteSpace(t.ExtraJson)
                       && t.ExtraJson.Contains($"\"event\":\"{eventName}\"", StringComparison.OrdinalIgnoreCase);
            }

            bool IsListen(TraceLog t) => IsEvent(t, "play") || IsEvent(t, "tts_play") || IsEvent(t, "audio_play") || IsEvent(t, "listen_start");

            var grouped = logs
                .GroupBy(t => t.PoiId)
                .Select(g => new
                {
                    PoiId = g.Key,
                    TotalListens = g.Count(IsListen),
                    TtsPlays = g.Count(x => IsEvent(x, "tts_play") || IsEvent(x, "play")),
                    AudioPlays = g.Count(x => IsEvent(x, "audio_play")),
                    ListenStarts = g.Count(x => IsEvent(x, "listen_start")),
                    DetailOpens = g.Count(x => IsEvent(x, "poi_detail_open") || IsEvent(x, "poi_click")),
                    Users = g.Select(x => x.DeviceId)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(30)
                        .ToList()
                })
                .OrderByDescending(x => x.TotalListens)
                .ThenByDescending(x => x.DetailOpens)
                .Take(top)
                .ToList();

            var poiIds = grouped.Select(x => x.PoiId).ToList();
            var poiNames = await _db.PointsOfInterest
                .AsNoTracking()
                .Where(p => poiIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name ?? string.Empty);

            var result = grouped.Select(x => new
            {
                x.PoiId,
                PoiName = poiNames.TryGetValue(x.PoiId, out var name) ? name : string.Empty,
                x.TotalListens,
                x.TtsPlays,
                x.AudioPlays,
                x.ListenStarts,
                x.DetailOpens,
                UniqueUsers = x.Users.Count,
                Users = x.Users
            });

            return Ok(result);
        }

        [HttpGet("active-users")]
        public async Task<IActionResult> GetActiveUsers(int hours = 24, int top = 100)
        {
            hours = Math.Clamp(hours, 1, 24 * 30);
            top = Math.Clamp(top, 1, 500);

            var since = DateTime.UtcNow.AddHours(-hours);
            var logs = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.TimestampUtc >= since)
                .ToListAsync();

            var users = logs
                .Where(t => !string.IsNullOrWhiteSpace(t.DeviceId))
                .GroupBy(t => t.DeviceId)
                .Select(g => new
                {
                    DeviceId = g.Key,
                    Platform = ParseDevicePart(g.Key, 0),
                    DeviceManufacturer = ParseDevicePart(g.Key, 1),
                    DeviceModel = ParseDevicePart(g.Key, 2),
                    DeviceVersion = ParseDevicePart(g.Key, 3),
                    TotalEvents = g.Count(),
                    LastSeenUtc = g.Max(x => x.TimestampUtc),
                    PoiIds = g.Select(x => x.PoiId).Where(id => id > 0).Distinct().Take(20).ToList()
                })
                .OrderByDescending(x => x.LastSeenUtc)
                .ThenByDescending(x => x.TotalEvents)
                .Take(top)
                .ToList();

            return Ok(users);
        }

        [HttpGet("avg-duration")]
        public async Task<IActionResult> GetAvgDuration(int poiId)
        {
            var q = await _db.TraceLogs
                .Where(t => t.PoiId == poiId
                            && t.DurationSeconds.HasValue
                            && t.DurationSeconds.Value > 0
                            && t.ExtraJson != null
                            && t.ExtraJson.Contains("\"event\":\"listen_complete\"", StringComparison.OrdinalIgnoreCase))
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

            var logs = await _db.TraceLogs
                .Where(t => t.TimestampUtc >= since)
                .Where(t => t.ExtraJson != null && (
                    t.ExtraJson.Contains("poi_heartbeat") ||
                    t.ExtraJson.Contains("poi_enter") ||
                    t.ExtraJson.Contains("navigation_arrived") ||
                    t.ExtraJson.Contains("play") ||
                    t.ExtraJson.Contains("listen_start") ||
                    t.ExtraJson.Contains("listen_complete")))
                .OrderByDescending(t => t.TimestampUtc)
                .Take(limit)
                .ToListAsync();

            var poiCoord = await _db.PointsOfInterest
                .AsNoTracking()
                .Select(p => new { p.Id, p.Latitude, p.Longitude })
                .ToDictionaryAsync(x => x.Id, x => new { x.Latitude, x.Longitude });

            var points = logs
                .Select(t =>
                {
                    var hasValidTraceCoord = t.Latitude >= -90 && t.Latitude <= 90
                                             && t.Longitude >= -180 && t.Longitude <= 180
                                             && !(Math.Abs(t.Latitude) < 0.000001 && Math.Abs(t.Longitude) < 0.000001);
                    if (hasValidTraceCoord)
                    {
                        return new { Latitude = t.Latitude, Longitude = t.Longitude };
                    }

                    if (t.PoiId > 0 && poiCoord.TryGetValue(t.PoiId, out var c))
                    {
                        var validPoiCoord = c.Latitude >= -90 && c.Latitude <= 90
                                            && c.Longitude >= -180 && c.Longitude <= 180
                                            && !(Math.Abs(c.Latitude) < 0.000001 && Math.Abs(c.Longitude) < 0.000001);
                        if (validPoiCoord)
                        {
                            return new { Latitude = c.Latitude, Longitude = c.Longitude };
                        }
                    }

                    return null;
                })
                .Where(x => x != null)
                .ToList();

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
                .Where(t => t.ExtraJson != null
                            && t.ExtraJson.Contains("\"event\":\"qr_scan\"")
                            && (t.ExtraJson.Contains("\"source\":\"mobile_scan\"")
                                || t.ExtraJson.Contains("\"source\":\"web_public_qr\"")))
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

        [HttpGet("app-listen-metrics")]
        public async Task<IActionResult> GetAppListenMetrics()
        {
            var logs = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.ExtraJson != null)
                .ToListAsync();

            logs = logs
                .Where(t => !string.IsNullOrWhiteSpace(t.ExtraJson)
                            && t.ExtraJson!.Contains("\"source\":\"mobile_app\"", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var ttsPlays = logs.Count(t => t.ExtraJson!.Contains("\"event\":\"tts_play\"", StringComparison.OrdinalIgnoreCase));
            var audioPlays = logs.Count(t => t.ExtraJson!.Contains("\"event\":\"audio_play\"", StringComparison.OrdinalIgnoreCase));

            return Ok(new
            {
                app_tts_play = ttsPlays,
                app_audio_play = audioPlays,
                app_total_listen = ttsPlays + audioPlays
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

        [HttpGet("routes")]
        public async Task<IActionResult> GetAnonymousRoutes(int hours = 24, int topUsers = 80, int maxPointsPerUser = 240)
        {
            hours = Math.Clamp(hours, 1, 168);
            topUsers = Math.Clamp(topUsers, 1, 300);
            maxPointsPerUser = Math.Clamp(maxPointsPerUser, 20, 600);

            var since = DateTime.UtcNow.AddHours(-hours);

            var logs = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.TimestampUtc >= since)
                .Where(t => !string.IsNullOrWhiteSpace(t.DeviceId))
                .Where(t => t.Latitude >= -90 && t.Latitude <= 90 && t.Longitude >= -180 && t.Longitude <= 180)
                .Where(t => !(Math.Abs(t.Latitude) < 0.000001 && Math.Abs(t.Longitude) < 0.000001))
                .Where(t => t.ExtraJson != null && (
                    t.ExtraJson.Contains("\"source\":\"mobile_app\"", StringComparison.OrdinalIgnoreCase)
                    || t.ExtraJson.Contains("poi_heartbeat", StringComparison.OrdinalIgnoreCase)
                    || t.ExtraJson.Contains("navigation_start", StringComparison.OrdinalIgnoreCase)
                    || t.ExtraJson.Contains("navigation_arrived", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(t => t.DeviceId)
                .ThenBy(t => t.TimestampUtc)
                .ToListAsync();

            var grouped = logs
                .GroupBy(x => x.DeviceId)
                .Select(g =>
                {
                    var ordered = g.OrderBy(x => x.TimestampUtc).ToList();
                    var sampled = DownsampleRoutePoints(ordered, maxPointsPerUser);
                    var lastSeen = ordered.Count > 0 ? ordered[^1].TimestampUtc : DateTime.MinValue;

                    return new AnonymousUserRouteDto
                    {
                        DeviceId = g.Key,
                        LastSeenUtc = lastSeen,
                        TotalPoints = ordered.Count,
                        Points = sampled.Select(p => new AnonymousRoutePointDto
                        {
                            Latitude = p.Latitude,
                            Longitude = p.Longitude,
                            TimestampUtc = p.TimestampUtc,
                            PoiId = p.PoiId
                        }).ToList()
                    };
                })
                .Where(x => x.Points.Count >= 2)
                .OrderByDescending(x => x.LastSeenUtc)
                .ThenByDescending(x => x.TotalPoints)
                .Take(topUsers)
                .ToList();

            return Ok(grouped);
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

        private static string ParseDevicePart(string? deviceId, int index)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return string.Empty;
            var parts = deviceId.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length <= index) return string.Empty;
            return parts[index] ?? string.Empty;
        }

        private static List<TraceLog> DownsampleRoutePoints(List<TraceLog> points, int maxPoints)
        {
            if (points.Count <= maxPoints) return points;

            var result = new List<TraceLog>(maxPoints)
            {
                points[0]
            };

            if (maxPoints <= 2)
            {
                result.Add(points[^1]);
                return result;
            }

            var middleTarget = maxPoints - 2;
            var step = (double)(points.Count - 2) / middleTarget;
            var cursor = 1d;

            for (var i = 0; i < middleTarget; i++)
            {
                var index = Math.Clamp((int)Math.Round(cursor), 1, points.Count - 2);
                result.Add(points[index]);
                cursor += step;
            }

            result.Add(points[^1]);
            return result;
        }

        private sealed class AnonymousUserRouteDto
        {
            public string DeviceId { get; set; } = string.Empty;
            public DateTime LastSeenUtc { get; set; }
            public int TotalPoints { get; set; }
            public List<AnonymousRoutePointDto> Points { get; set; } = new();
        }

        private sealed class AnonymousRoutePointDto
        {
            public double Latitude { get; set; }
            public double Longitude { get; set; }
            public DateTime TimestampUtc { get; set; }
            public int PoiId { get; set; }
        }
    }
}
