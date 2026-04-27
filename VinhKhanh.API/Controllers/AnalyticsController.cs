using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using VinhKhanh.API.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
        private readonly IMemoryCache _cache;

        public AnalyticsController(AppDbContext db, IHubContext<SyncHub> hubContext, IMemoryCache cache)
        {
            _db = db;
            _hubContext = hubContext;
            _cache = cache;
        }

        [HttpPost]
        public async Task<IActionResult> PostTrace([FromBody] TraceLog trace)
        {
            if (trace == null) return BadRequest();

            trace.ExtraJson ??= string.Empty;

            // Anti-spam: chặn spam theo event + device + poi theo từng cửa sổ thời gian linh hoạt
            var trackedEvents = new[] { "qr_scan", "tts_play", "audio_play", "listen_start", "audio_list_open", "poi_click", "poi_detail_open", "navigation_start", "poi_heartbeat" };
            var matchedEvent = trackedEvents.FirstOrDefault(ev => trace.ExtraJson.Contains($"\"event\":\"{ev}\"", StringComparison.OrdinalIgnoreCase));
            var exactDuplicateKey = $"trace-exact:{trace.DeviceId}:{trace.PoiId}:{matchedEvent}:{ComputeTraceFingerprint(trace.ExtraJson)}";

            if (!string.IsNullOrWhiteSpace(matchedEvent)
                && !string.IsNullOrWhiteSpace(trace.DeviceId))
            {
                if (_cache.TryGetValue(exactDuplicateKey, out _))
                {
                    return Ok(new
                    {
                        ignored = true,
                        reason = "exact_duplicate_cache",
                        eventName = matchedEvent,
                        poiId = trace.PoiId
                    });
                }

                var (windowSeconds, maxCount) = matchedEvent switch
                {
                    "poi_heartbeat" => (90, 4),
                    "poi_enter" => (60, 3),
                    "tts_play" => (60, 2),
                    "audio_play" => (60, 2),
                    "listen_start" => (45, 2),
                    "poi_click" => (30, 4),
                    "poi_detail_open" => (30, 4),
                    "navigation_start" => (60, 4),
                    "qr_scan" => (90, 3),
                    _ => (60, 6)
                };

                var exactSince = DateTime.UtcNow.AddSeconds(-8);
                var since = DateTime.UtcNow.AddSeconds(-windowSeconds);

                var recentLogs = await _db.TraceLogs
                    .AsNoTracking()
                    .Where(t => t.TimestampUtc >= since && t.DeviceId == trace.DeviceId)
                    .Select(t => new TraceLog
                    {
                        TimestampUtc = t.TimestampUtc,
                        DeviceId = t.DeviceId,
                        PoiId = t.PoiId,
                        Latitude = t.Latitude,
                        Longitude = t.Longitude,
                        ExtraJson = t.ExtraJson
                    })
                    .ToListAsync();

                bool IsEvent(TraceLog t, string eventName)
                {
                    return !string.IsNullOrWhiteSpace(t.ExtraJson)
                           && t.ExtraJson.Contains($"\"event\":\"{eventName}\"", StringComparison.OrdinalIgnoreCase);
                }

                // hard de-dup: cùng device + cùng POI + cùng event + cùng payload trong cửa sổ rất ngắn thì bỏ qua
                var hasExactDuplicate = recentLogs.Any(t => t.TimestampUtc >= exactSince
                                                            && t.PoiId == trace.PoiId
                                                            && t.ExtraJson == trace.ExtraJson);

                if (hasExactDuplicate)
                {
                    SetDuplicateCache(exactDuplicateKey, TimeSpan.FromSeconds(8));
                    return Ok(new
                    {
                        ignored = true,
                        reason = "exact_duplicate",
                        eventName = matchedEvent,
                        poiId = trace.PoiId
                    });
                }

                var recentDeviceEventCount = recentLogs.Count(t => IsEvent(t, matchedEvent));

                if (recentDeviceEventCount >= maxCount)
                {
                    return Ok(new
                    {
                        ignored = true,
                        reason = "event_rate_limited_per_device",
                        eventName = matchedEvent,
                        limit = maxCount,
                        windowSeconds,
                        poiId = trace.PoiId
                    });
                }

                if (trace.PoiId > 0)
                {
                    var recentPerPoiCount = recentLogs.Count(t => t.PoiId == trace.PoiId && IsEvent(t, matchedEvent));

                    if (recentPerPoiCount >= maxCount)
                    {
                        return Ok(new
                        {
                            ignored = true,
                            reason = "event_rate_limited_per_device_poi",
                            eventName = matchedEvent,
                            limit = maxCount,
                            windowSeconds,
                            poiId = trace.PoiId
                        });
                    }
                }

                if (string.Equals(matchedEvent, "poi_heartbeat", StringComparison.OrdinalIgnoreCase)
                    && trace.Latitude >= -90 && trace.Latitude <= 90
                    && trace.Longitude >= -180 && trace.Longitude <= 180)
                {
                    var lastHeartbeatKey = $"trace-last-heartbeat:{trace.DeviceId}";
                    if (_cache.TryGetValue(lastHeartbeatKey, out (DateTime TimestampUtc, double Latitude, double Longitude) cachedHeartbeat))
                    {
                        var cachedMoved = HaversineDistanceMeters(
                            cachedHeartbeat.Latitude,
                            cachedHeartbeat.Longitude,
                            trace.Latitude,
                            trace.Longitude);

                        var cachedElapsed = DateTime.UtcNow - cachedHeartbeat.TimestampUtc;
                        if (cachedMoved < 12 && cachedElapsed < TimeSpan.FromSeconds(15))
                        {
                            SetDuplicateCache(exactDuplicateKey, TimeSpan.FromSeconds(8));
                            return Ok(new
                            {
                                ignored = true,
                                reason = "heartbeat_not_moved_enough_cache",
                                eventName = matchedEvent,
                                poiId = trace.PoiId
                            });
                        }
                    }

                    var latestHeartbeat = recentLogs
                        .Where(t => IsEvent(t, "poi_heartbeat"))
                        .OrderByDescending(t => t.TimestampUtc)
                        .FirstOrDefault();

                    if (latestHeartbeat != null)
                    {
                        var moved = HaversineDistanceMeters(
                            latestHeartbeat.Latitude,
                            latestHeartbeat.Longitude,
                            trace.Latitude,
                            trace.Longitude);

                        var elapsed = DateTime.UtcNow - latestHeartbeat.TimestampUtc;
                        if (moved < 12 && elapsed < TimeSpan.FromSeconds(15))
                        {
                            SetDuplicateCache(exactDuplicateKey, TimeSpan.FromSeconds(8));
                            return Ok(new
                            {
                                ignored = true,
                                reason = "heartbeat_not_moved_enough",
                                eventName = matchedEvent,
                                poiId = trace.PoiId
                            });
                        }
                    }
                }
            }

            trace.TimestampUtc = DateTime.UtcNow;
            _db.TraceLogs.Add(trace);
            await _db.SaveChangesAsync();

            SetDuplicateCache(exactDuplicateKey: $"trace-exact:{trace.DeviceId}:{trace.PoiId}:{matchedEvent}:{ComputeTraceFingerprint(trace.ExtraJson)}", TimeSpan.FromSeconds(8));

            if (string.Equals(matchedEvent, "poi_heartbeat", StringComparison.OrdinalIgnoreCase))
            {
                _cache.Set($"trace-last-heartbeat:{trace.DeviceId}", (trace.TimestampUtc, trace.Latitude, trace.Longitude), new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
                });
            }

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

        private static string ComputeTraceFingerprint(string? extraJson)
        {
            if (string.IsNullOrWhiteSpace(extraJson)) return string.Empty;
            unchecked
            {
                var hash = 17;
                foreach (var ch in extraJson)
                {
                    hash = (hash * 31) + ch;
                }

                return hash.ToString("x8");
            }
        }

        private void SetDuplicateCache(string exactDuplicateKey, TimeSpan ttl)
        {
            _cache.Set(exactDuplicateKey, true, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            });
        }

        [HttpGet("poi-live-stats")]
        public async Task<IActionResult> GetPoiLiveStats(double? userLat = null, double? userLng = null, int top = 50)
        {
            top = Math.Clamp(top, 1, 200);

            var pois = await _db.PointsOfInterest.AsNoTracking().ToListAsync();
            var now = DateTime.UtcNow;
            var last3m = now.AddMinutes(-3);
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
                    .Where(x => x.TimestampUtc >= last3m && (IsEvent(x, "poi_heartbeat") || IsEvent(x, "poi_enter") || IsEvent(x, "qr_scan")))
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

                var totalListens = poiLogs.Count(x => IsEvent(x, "tts_play") || IsEvent(x, "audio_play"));

                var qrScanCount = poiLogs.Count(x => x.TimestampUtc >= last5m && IsEvent(x, "qr_scan"));

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
                    TotalListens = totalListens,
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

            var publishedPois = await _db.PointsOfInterest
                .AsNoTracking()
                .Where(p => p.IsPublished)
                .Select(p => new { p.Id, Name = p.Name ?? string.Empty, p.OwnerId })
                .ToListAsync();

            var publishedPoiMap = publishedPois.ToDictionary(p => p.Id, p => p.Name);
            var ownerIdByPoi = publishedPois.ToDictionary(p => p.Id, p => p.OwnerId);

            var publishedPoiIds = publishedPoiMap.Keys.ToHashSet();

            var ownerUsers = await _db.Users
                .AsNoTracking()
                .Where(u => u.Role == "Owner")
                .Select(u => new { u.Id, u.Email })
                .ToListAsync();

            var ownerRegs = await _db.OwnerRegistrations
                .AsNoTracking()
                .Select(r => new { r.UserId, r.ShopName })
                .ToListAsync();

            string ResolveOwnerName(int poiId)
            {
                if (!ownerIdByPoi.TryGetValue(poiId, out var ownerId) || !ownerId.HasValue) return string.Empty;

                var ownerReg = ownerRegs.FirstOrDefault(r => r.UserId == ownerId.Value);
                if (!string.IsNullOrWhiteSpace(ownerReg?.ShopName))
                {
                    return ownerReg.ShopName;
                }

                var ownerUser = ownerUsers.FirstOrDefault(u => u.Id == ownerId.Value);
                return ownerUser?.Email ?? string.Empty;
            }

            bool IsListenEvent(TraceLog t)
            {
                var eventName = GetExtraJsonValue(t.ExtraJson, "event");
                if (!string.Equals(eventName, "tts_play", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(eventName, "audio_play", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Loại source nội bộ queue để tránh bị nhân đôi với event do UI bắn.
                var source = GetExtraJsonValue(t.ExtraJson, "source");
                if (string.Equals(source, "app_audio_queue", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }

            var traces = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.PoiId > 0)
                .ToListAsync();

            var grouped = traces
                .Where(IsListenEvent)
                .Where(t => publishedPoiIds.Contains(t.PoiId))
                .GroupBy(t => t.PoiId)
                .Select(g => new { PoiId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(top)
                .ToList();

            var result = grouped.Select(x => new
            {
                x.PoiId,
                x.Count,
                PoiName = publishedPoiMap.TryGetValue(x.PoiId, out var name) ? name : string.Empty,
                OwnerName = ResolveOwnerName(x.PoiId)
            });

            return Ok(result);
        }

        [HttpGet("engagement")]
        public async Task<IActionResult> GetEngagement(int top = 20, int hours = 72)
        {
            top = Math.Clamp(top, 1, 200);
            hours = Math.Clamp(hours, 1, 24 * 30);

            var since = DateTime.UtcNow.AddHours(-hours);

            // Chỉ lấy POI hiện còn publish để tránh hiện POI cũ/ẩn trên analytics
            var publishedPoiMap = await _db.PointsOfInterest
                .AsNoTracking()
                .Where(p => p.IsPublished)
                .ToDictionaryAsync(p => p.Id, p => p.Name ?? string.Empty);

            var publishedPoiIds = publishedPoiMap.Keys.ToHashSet();

            var logs = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.TimestampUtc >= since && t.PoiId > 0)
                .ToListAsync();

            logs = logs.Where(t => publishedPoiIds.Contains(t.PoiId)).ToList();

            bool IsEvent(TraceLog t, string eventName)
            {
                var current = GetExtraJsonValue(t.ExtraJson, "event");
                return string.Equals(current, eventName, StringComparison.OrdinalIgnoreCase);
            }

            bool IsFromInternalAudioQueue(TraceLog t)
            {
                var source = GetExtraJsonValue(t.ExtraJson, "source");
                return string.Equals(source, "app_audio_queue", StringComparison.OrdinalIgnoreCase);
            }

            bool IsTtsListen(TraceLog t) => IsEvent(t, "tts_play") && !IsFromInternalAudioQueue(t);
            bool IsAudioListen(TraceLog t) => IsEvent(t, "audio_play") && !IsFromInternalAudioQueue(t);
            bool IsDetailOpen(TraceLog t) => IsEvent(t, "poi_detail_open");

            var grouped = logs
                .GroupBy(t => t.PoiId)
                .Select(g => new
                {
                    PoiId = g.Key,
                    TotalListens = g.Count(x => IsTtsListen(x) || IsAudioListen(x)),
                    TtsPlays = g.Count(IsTtsListen),
                    AudioPlays = g.Count(IsAudioListen),
                    DetailOpens = g.Count(IsDetailOpen),
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

            var result = grouped.Select(x => new
            {
                x.PoiId,
                PoiName = publishedPoiMap.TryGetValue(x.PoiId, out var name) ? name : string.Empty,
                x.TotalListens,
                x.TtsPlays,
                x.AudioPlays,
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
            if (poiId <= 0)
            {
                return Ok(new { poiId, avg = 0.0 });
            }

            var isPublishedPoi = await _db.PointsOfInterest
                .AsNoTracking()
                .AnyAsync(p => p.Id == poiId && p.IsPublished);

            if (!isPublishedPoi)
            {
                return Ok(new { poiId, avg = 0.0 });
            }

            var since = DateTime.UtcNow.AddDays(-30);
            var logs = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.PoiId == poiId && t.TimestampUtc >= since)
                .Select(t => new { t.TimestampUtc, t.DeviceId, t.DurationSeconds, t.ExtraJson })
                .ToListAsync();

            if (!logs.Any())
            {
                return Ok(new { poiId, avg = 0.0 });
            }

            double? ParseDurationFromExtra(string? extraJson)
            {
                if (string.IsNullOrWhiteSpace(extraJson)) return null;
                var raw = GetExtraJsonValue(extraJson, "durationSeconds") ?? GetExtraJsonValue(extraJson, "duration");
                if (string.IsNullOrWhiteSpace(raw)) return null;

                if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v)
                    && v > 0)
                {
                    return v;
                }

                return null;
            }

            bool IsListenSignal(string? extraJson)
            {
                var ev = GetExtraJsonValue(extraJson, "event");
                return string.Equals(ev, "listen_complete", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(ev, "listen_start", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(ev, "tts_play", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(ev, "audio_play", StringComparison.OrdinalIgnoreCase);
            }

            var directDurations = logs
                .Select(t => t.DurationSeconds ?? ParseDurationFromExtra(t.ExtraJson))
                .Where(x => x.HasValue && x.Value > 0)
                .Select(x => x!.Value)
                .ToList();

            if (directDurations.Any())
            {
                return Ok(new { poiId, avg = Math.Round(directDurations.Average(), 2) });
            }

            // Fallback: ước lượng thời gian phiên nghe theo mỗi thiết bị trong cửa sổ 10 phút.
            var tenMinutesTicks = TimeSpan.FromMinutes(10).Ticks;
            var sessionDurations = logs
                .Where(x => IsListenSignal(x.ExtraJson))
                .Where(x => !string.IsNullOrWhiteSpace(x.DeviceId))
                .Select(x => new
                {
                    DeviceKey = GetOnlineDeviceKey(x.DeviceId),
                    x.TimestampUtc
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.DeviceKey))
                .GroupBy(x => new { x.DeviceKey, Bucket = x.TimestampUtc.Ticks / tenMinutesTicks })
                .Select(g => (g.Max(x => x.TimestampUtc) - g.Min(x => x.TimestampUtc)).TotalSeconds)
                .Where(sec => sec >= 2 && sec <= 1800)
                .ToList();

            if (!sessionDurations.Any())
            {
                return Ok(new { poiId, avg = 0.0 });
            }

            return Ok(new { poiId, avg = Math.Round(sessionDurations.Average(), 2) });
        }

        [HttpGet("heatmap")]
        public async Task<IActionResult> GetHeatmap(int limit = 200, int hours = 24)
        {
            limit = Math.Clamp(limit, 1, 5000);
            hours = Math.Clamp(hours, 1, 168);
            var since = DateTime.UtcNow.AddHours(-hours);

            var publishedPoiIds = await _db.PointsOfInterest
                .AsNoTracking()
                .Where(p => p.IsPublished)
                .Select(p => p.Id)
                .ToListAsync();
            var publishedSet = publishedPoiIds.ToHashSet();

            var logs = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.TimestampUtc >= since)
                .Where(t => t.PoiId <= 0 || publishedSet.Contains(t.PoiId))
                .Where(t => t.ExtraJson != null && (
                    t.ExtraJson.Contains("poi_heartbeat") ||
                    t.ExtraJson.Contains("poi_enter") ||
                    t.ExtraJson.Contains("navigation_arrived") ||
                    t.ExtraJson.Contains("play") ||
                    t.ExtraJson.Contains("listen_start") ||
                    t.ExtraJson.Contains("listen_complete")))
                .OrderByDescending(t => t.TimestampUtc)
                .ToListAsync();

            // Giảm spam điểm lặp: cùng device + POI + tọa độ gần như trùng trong 20s chỉ giữ 1 bản ghi
            logs = logs
                .GroupBy(t => new
                {
                    Device = t.DeviceId ?? string.Empty,
                    t.PoiId,
                    Lat = Math.Round(t.Latitude, 5),
                    Lon = Math.Round(t.Longitude, 5),
                    Bucket = t.TimestampUtc.Ticks / TimeSpan.FromSeconds(20).Ticks
                })
                .Select(g => g.OrderByDescending(x => x.TimestampUtc).First())
                .OrderByDescending(t => t.TimestampUtc)
                .Take(limit)
                .ToList();

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
        public async Task<IActionResult> GetLogs(int limit = 200, int hours = 24, bool includeHeartbeats = false)
        {
            limit = Math.Clamp(limit, 20, 2000);
            hours = Math.Clamp(hours, 1, 168);
            var since = DateTime.UtcNow.AddHours(-hours);

            var publishedPoiIds = await _db.PointsOfInterest
                .AsNoTracking()
                .Where(p => p.IsPublished)
                .Select(p => p.Id)
                .ToListAsync();
            var publishedSet = publishedPoiIds.ToHashSet();

            var query = _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.TimestampUtc >= since)
                .Where(t => t.PoiId <= 0 || publishedSet.Contains(t.PoiId));

            if (!includeHeartbeats)
            {
                query = query.Where(t => t.ExtraJson == null || !t.ExtraJson.Contains("\"event\":\"poi_heartbeat\""));
            }

            var rawLogs = await query
                .OrderByDescending(t => t.TimestampUtc)
                .Take(limit * 4)
                .ToListAsync();

            var logs = rawLogs
                .GroupBy(t => new
                {
                    Device = t.DeviceId ?? string.Empty,
                    t.PoiId,
                    Extra = t.ExtraJson ?? string.Empty,
                    Bucket = t.TimestampUtc.Ticks / TimeSpan.FromSeconds(20).Ticks
                })
                .Select(g => g.OrderByDescending(x => x.TimestampUtc).First())
                .OrderByDescending(t => t.TimestampUtc)
                .Take(limit)
                .ToList();

            return Ok(logs);
        }

        // NEW: thống kê số lượt quét QR theo POI
        [HttpGet("qr-scan-counts")]
        public async Task<IActionResult> GetQrScanCounts(int top = 50)
        {
            var poiNames = await _db.PointsOfInterest
                .AsNoTracking()
                .Where(p => p.IsPublished)
                .ToDictionaryAsync(p => p.Id, p => p.Name ?? string.Empty);

            var q = await _db.TraceLogs
                .Where(t => t.ExtraJson != null
                            && GetExtraJsonValue(t.ExtraJson, "event") == "qr_scan"
                            && (string.Equals(GetExtraJsonValue(t.ExtraJson, "source"), "mobile_scan", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(GetExtraJsonValue(t.ExtraJson, "source"), "web_public_qr", StringComparison.OrdinalIgnoreCase)))
                .GroupBy(t => t.PoiId)
                .Select(g => new { PoiId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(top)
                .ToListAsync();

            return Ok(q.Select(x => new
            {
                x.PoiId,
                PoiName = poiNames.TryGetValue(x.PoiId, out var name) ? name : string.Empty,
                x.Count
            }));
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
            var webListenComplete = logs.Count(t => t.ExtraJson!.Contains("\"event\":\"listen_complete\"", StringComparison.OrdinalIgnoreCase));

            return Ok(new
            {
                web_qr_scan = webQrScan,
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

            var ttsPlays = logs.Count(t => string.Equals(GetExtraJsonValue(t.ExtraJson, "event"), "tts_play", StringComparison.OrdinalIgnoreCase));
            var audioPlays = logs.Count(t => string.Equals(GetExtraJsonValue(t.ExtraJson, "event"), "audio_play", StringComparison.OrdinalIgnoreCase));

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

        [HttpGet("top-visited-today")]
        public async Task<IActionResult> GetTopVisitedToday(int top = 20)
        {
            top = Math.Clamp(top, 1, 200);

            var now = DateTime.UtcNow;
            var todayStartUtc = now.Date;
            var insideThresholdUtc = now.AddSeconds(-90);

            var publishedPois = await _db.PointsOfInterest
                .AsNoTracking()
                .Where(p => p.IsPublished)
                .Select(p => new { p.Id, p.Name, p.Latitude, p.Longitude, p.Radius })
                .ToListAsync();

            if (publishedPois.Count == 0)
            {
                return Ok(Array.Empty<object>());
            }

            var publishedById = publishedPois.ToDictionary(p => p.Id);
            var publishedIds = publishedById.Keys.ToHashSet();

            var todayLogs = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.TimestampUtc >= todayStartUtc && t.PoiId > 0 && publishedIds.Contains(t.PoiId))
                .Where(t => !string.IsNullOrWhiteSpace(t.DeviceId))
                .Where(t => !string.IsNullOrWhiteSpace(t.ExtraJson))
                .Select(t => new
                {
                    t.PoiId,
                    t.DeviceId,
                    t.TimestampUtc,
                    t.Latitude,
                    t.Longitude,
                    EventName = GetExtraJsonValue(t.ExtraJson, "event"),
                    Source = GetExtraJsonValue(t.ExtraJson, "source")
                })
                .ToListAsync();

            bool IsMobileSource(string? source) => string.Equals(source, "mobile_app", StringComparison.OrdinalIgnoreCase);
            bool IsVisitSignal(string? eventName)
            {
                return string.Equals(eventName, "poi_enter", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(eventName, "poi_heartbeat", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(eventName, "navigation_arrived", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(eventName, "qr_scan", StringComparison.OrdinalIgnoreCase);
            }

            bool IsInsideSignal(string? eventName)
            {
                return string.Equals(eventName, "poi_heartbeat", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(eventName, "poi_enter", StringComparison.OrdinalIgnoreCase);
            }

            bool HasValidCoord(double lat, double lng)
            {
                return lat >= -90 && lat <= 90
                       && lng >= -180 && lng <= 180
                       && !(Math.Abs(lat) < 0.000001 && Math.Abs(lng) < 0.000001);
            }

            var mobileVisitLogs = todayLogs
                .Where(x => IsMobileSource(x.Source) && IsVisitSignal(x.EventName))
                .Select(x => new
                {
                    x.PoiId,
                    DeviceKey = GetOnlineDeviceKey(x.DeviceId),
                    x.TimestampUtc,
                    x.EventName,
                    x.Latitude,
                    x.Longitude
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.DeviceKey))
                .ToList();

            if (mobileVisitLogs.Count == 0)
            {
                return Ok(Array.Empty<object>());
            }

            var visitorsByPoi = mobileVisitLogs
                .GroupBy(x => x.PoiId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.DeviceKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());

            var lastVisitByPoi = mobileVisitLogs
                .GroupBy(x => x.PoiId)
                .ToDictionary(g => g.Key, g => g.Max(x => x.TimestampUtc));

            var activeInsideByDevice = mobileVisitLogs
                .Where(x => x.TimestampUtc >= insideThresholdUtc)
                .Where(x => IsInsideSignal(x.EventName))
                .Where(x => HasValidCoord(x.Latitude, x.Longitude))
                .Where(x => publishedById.TryGetValue(x.PoiId, out var poi)
                            && HaversineDistanceMeters(x.Latitude, x.Longitude, poi.Latitude, poi.Longitude) <= Math.Max(1, poi.Radius))
                .GroupBy(x => x.DeviceKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.TimestampUtc).First())
                .ToList();

            var currentlyInsideByPoi = activeInsideByDevice
                .GroupBy(x => x.PoiId)
                .ToDictionary(g => g.Key, g => g.Count());

            var result = visitorsByPoi
                .Select(kv =>
                {
                    var poiId = kv.Key;
                    var inside = currentlyInsideByPoi.TryGetValue(poiId, out var c) ? c : 0;
                    var lastVisitUtc = lastVisitByPoi.TryGetValue(poiId, out var lu) ? lu : DateTime.MinValue;

                    return new
                    {
                        PoiId = poiId,
                        PoiName = publishedById.TryGetValue(poiId, out var poi) ? (poi.Name ?? string.Empty) : string.Empty,
                        VisitorsToday = kv.Value,
                        CurrentlyInside = inside,
                        LastVisitUtc = lastVisitUtc
                    };
                })
                .OrderByDescending(x => x.VisitorsToday)
                .ThenByDescending(x => x.CurrentlyInside)
                .ThenByDescending(x => x.LastVisitUtc)
                .Take(top)
                .ToList();

            return Ok(result);
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var now = DateTime.UtcNow;
            var onlineSince = now.AddSeconds(-70);
            var todayStartUtc = now.Date;

            bool IsOnlineEvent(TraceLog t)
            {
                var eventName = GetExtraJsonValue(t.ExtraJson, "event");
                return string.Equals(eventName, "qr_scan", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(eventName, "listen_start", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(eventName, "poi_heartbeat", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(eventName, "web_session_active", StringComparison.OrdinalIgnoreCase);
            }

            var recentLogs = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.TimestampUtc >= onlineSince)
                .Where(t => !string.IsNullOrWhiteSpace(t.DeviceId))
                .Select(t => new { t.DeviceId, t.TimestampUtc, t.ExtraJson })
                .ToListAsync();

            // Tính session web còn online: có join/active gần đây và không có leave mới hơn.
            var activeWebDevices = recentLogs
                .Select(t => new
                {
                    DeviceKey = GetOnlineDeviceKey(t.DeviceId),
                    DeviceKeyNorm = (GetOnlineDeviceKey(t.DeviceId) ?? string.Empty).Trim().ToLowerInvariant(),
                    EventName = GetExtraJsonValue(t.ExtraJson, "event"),
                    SessionId = GetExtraJsonValue(t.ExtraJson, "sessionId"),
                    SessionIdNorm = (GetExtraJsonValue(t.ExtraJson, "sessionId") ?? string.Empty).Trim().ToLowerInvariant(),
                    Source = GetExtraJsonValue(t.ExtraJson, "source"),
                    t.TimestampUtc
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.DeviceKey)
                            && string.Equals(x.Source, "web_public_qr", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(x.SessionId))
                .GroupBy(x => new { x.DeviceKeyNorm, x.SessionIdNorm })
                .Where(g =>
                {
                    var latestJoinOrActive = g
                        .Where(x => string.Equals(x.EventName, "web_session_join", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(x.EventName, "web_session_active", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(x => x.TimestampUtc)
                        .FirstOrDefault();

                    if (latestJoinOrActive == null) return false;

                    var latestLeave = g
                        .Where(x => string.Equals(x.EventName, "web_session_leave", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(x => x.TimestampUtc)
                        .FirstOrDefault();

                    return latestLeave == null || latestLeave.TimestampUtc < latestJoinOrActive.TimestampUtc;
                })
                .Select(g => g
                    .Select(x => x.DeviceKey)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var mobileOnlineDevices = recentLogs
                .Where(t =>
                {
                    var source = GetExtraJsonValue(t.ExtraJson, "source");
                    if (!string.Equals(source, "mobile_app", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    return IsOnlineEvent(new TraceLog { DeviceId = t.DeviceId, TimestampUtc = t.TimestampUtc, ExtraJson = t.ExtraJson });
                })
                .Select(t => GetOnlineDeviceKey(t.DeviceId))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var webDevice in activeWebDevices)
            {
                mobileOnlineDevices.Add(webDevice);
            }

            var onlineUsers = mobileOnlineDevices.Count;

            var visitorsToday = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.TimestampUtc >= todayStartUtc)
                .Where(t => !string.IsNullOrWhiteSpace(t.DeviceId))
                .Select(t => t.DeviceId)
                .Distinct()
                .CountAsync();

            return Ok(new
            {
                onlineUsers,
                visitorsToday,
                sampledAtUtc = now
            });
        }

        [HttpGet("routes")]
        public async Task<IActionResult> GetAnonymousRoutes(int hours = 24, int topUsers = 80, int maxPointsPerUser = 240)
        {
            hours = Math.Clamp(hours, 1, 168);
            topUsers = Math.Clamp(topUsers, 1, 300);
            maxPointsPerUser = Math.Clamp(maxPointsPerUser, 20, 600);

            var since = DateTime.UtcNow.AddHours(-hours);

            var publishedPoiIds = await _db.PointsOfInterest
                .AsNoTracking()
                .Where(p => p.IsPublished)
                .Select(p => p.Id)
                .ToListAsync();

            var publishedSet = publishedPoiIds.ToHashSet();

            var logs = await _db.TraceLogs
                .AsNoTracking()
                .Where(t => t.TimestampUtc >= since)
                .Where(t => !string.IsNullOrWhiteSpace(t.DeviceId))
                .Where(t => t.Latitude >= -90 && t.Latitude <= 90 && t.Longitude >= -180 && t.Longitude <= 180)
                .Where(t => !(Math.Abs(t.Latitude) < 0.000001 && Math.Abs(t.Longitude) < 0.000001))
                .Where(t => t.ExtraJson != null && (
                    t.ExtraJson.Contains("\"source\":\"mobile_app\"", StringComparison.OrdinalIgnoreCase)
                    || t.ExtraJson.Contains("\"source\":\"web_public_qr\"", StringComparison.OrdinalIgnoreCase)
                    || t.ExtraJson.Contains("poi_heartbeat", StringComparison.OrdinalIgnoreCase)
                    || t.ExtraJson.Contains("navigation_start", StringComparison.OrdinalIgnoreCase)
                    || t.ExtraJson.Contains("navigation_arrived", StringComparison.OrdinalIgnoreCase)
                    || t.ExtraJson.Contains("listen_start", StringComparison.OrdinalIgnoreCase)
                    || t.ExtraJson.Contains("listen_complete", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(t => t.DeviceId)
                .ThenBy(t => t.TimestampUtc)
                .ToListAsync();

            logs = logs
                .Where(t => t.PoiId <= 0 || publishedSet.Contains(t.PoiId))
                .ToList();

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
                .Where(x => x.Points.Count >= 1)
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

        private static string? GetExtraJsonValue(string? extraJson, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(extraJson) || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(extraJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.GetRawText();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetOnlineDeviceKey(string? deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return string.Empty;
            if (deviceId.StartsWith("web-", StringComparison.OrdinalIgnoreCase)) return deviceId;

            var parts = deviceId.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            // deviceId mới: platform|manufacturer|model|version|installId
            // Ưu tiên installId để tránh 1 emulator bị tính thành nhiều user do format khác nhau.
            if (parts.Length >= 5)
            {
                var installId = parts[4]?.Trim();
                if (!string.IsNullOrWhiteSpace(installId)) return installId;
            }

            if (parts.Length >= 4)
            {
                return string.Join('|', parts.Take(4));
            }

            // Bỏ qua id không đúng format để không làm phồng số user online.
            return string.Empty;
        }

        private static string ParseDevicePart(string? deviceId, int index)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return string.Empty;
            var parts = deviceId.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return string.Empty;

            return index switch
            {
                // deviceId format mới: platform|manufacturer|model|version|installId
                // fallback format cũ: platform|manufacturer|model|version
                0 => parts.ElementAtOrDefault(0) ?? string.Empty,
                1 => parts.ElementAtOrDefault(1) ?? string.Empty,
                2 => parts.ElementAtOrDefault(2) ?? string.Empty,
                3 => parts.ElementAtOrDefault(4) ?? parts.ElementAtOrDefault(3) ?? string.Empty,
                _ => string.Empty
            };
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
