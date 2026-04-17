using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using VinhKhanh.API.Data;
using VinhKhanh.API.Services;
using VinhKhanh.API.Hubs;
using VinhKhanh.Shared;

namespace VinhKhanh.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PoiController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IQrCodeService _qrCodeService;
        private readonly IHubContext<SyncHub> _hubContext;
        private readonly IPoiCleanupService _cleanupService;

        public PoiController(AppDbContext context, IQrCodeService qrCodeService, IHubContext<SyncHub> hubContext, IPoiCleanupService cleanupService)
        {
            _context = context;
            _qrCodeService = qrCodeService;
            _hubContext = hubContext;
            _cleanupService = cleanupService;
        }

        [HttpGet("load-all")]
        public async Task<IActionResult> LoadAll([FromQuery] string lang = "vi")
        {
            var preferredLang = string.IsNullOrWhiteSpace(lang) ? "vi" : lang.Trim().ToLowerInvariant();
            var includeUnpublished = string.Equals(Request.Headers["X-Include-Unpublished"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(Request.Query["includeUnpublished"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);

            var apiKey = Request.Headers["X-API-Key"].FirstOrDefault();
            var isAdminCaller = !string.IsNullOrWhiteSpace(apiKey) && apiKey == "admin123";

            var poiQuery = _context.PointsOfInterest.AsNoTracking();
            if (!(includeUnpublished || isAdminCaller))
            {
                poiQuery = poiQuery.Where(p => p.IsPublished);
            }

            var pois = await poiQuery.ToListAsync();

            var poiIds = pois.Select(p => p.Id).ToList();
            var contents = await _context.PointContents
                .AsNoTracking()
                .Where(c => poiIds.Contains(c.PoiId))
                .ToListAsync();

            object BuildContentPayload(ContentModel source, bool isFallback)
            {
                var audioDecorated = DecorateAudioUrl(source.AudioUrl, preferredLang);
                var safeDescription = !string.IsNullOrWhiteSpace(source.Description)
                    ? source.Description
                    : $"{source.Title ?? "POI"} - mo ta dang cap nhat.";
                return new
                {
                    source.Id,
                    source.PoiId,
                    source.LanguageCode,
                    source.Title,
                    source.Subtitle,
                    Description = safeDescription,
                    audio_url = audioDecorated,
                    source.IsTTS,
                    source.PriceRange,
                    source.Rating,
                    source.OpeningHours,
                    source.PhoneNumber,
                    source.Address,
                    source.ShareUrl,
                    is_fallback = isFallback
                };
            }

            var payload = pois.Select(p =>
            {
                var tier1 = contents.FirstOrDefault(c => c.PoiId == p.Id && string.Equals(c.LanguageCode, preferredLang, StringComparison.OrdinalIgnoreCase));
                var tier2 = contents.FirstOrDefault(c => c.PoiId == p.Id && string.Equals(c.LanguageCode, "en", StringComparison.OrdinalIgnoreCase));
                var tier3 = contents.FirstOrDefault(c => c.PoiId == p.Id && string.Equals(c.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));

                var chosen = tier1 ?? tier2 ?? tier3;
                if (chosen == null)
                {
                    return new
                    {
                        poi = p,
                        localization = (object?)null,
                        fallback_tier = 0
                    };
                }

                var fallbackTier = chosen == tier1 ? 1 : (chosen == tier2 ? 2 : 3);
                var chosenPayload = BuildContentPayload(chosen, fallbackTier > 1);

                return new
                {
                    poi = p,
                    localization = chosenPayload,
                    fallback_tier = fallbackTier
                };
            }).ToList();

            return Ok(new
            {
                lang = preferredLang,
                include_unpublished = includeUnpublished || isAdminCaller,
                total = payload.Count,
                items = payload
            });
        }

        [HttpGet("nearby")]
        public async Task<IActionResult> Nearby([FromQuery] double lat, [FromQuery] double lng, [FromQuery] double radiusMeters = 1500, [FromQuery] int top = 10)
        {
            if (double.IsNaN(lat) || double.IsNaN(lng)) return BadRequest("lat_lng_required");
            var r = Math.Clamp(radiusMeters, 100, 10000);
            var t = Math.Clamp(top, 1, 100);

            var pois = await _context.PointsOfInterest
                .AsNoTracking()
                .Where(p => p.IsPublished)
                .ToListAsync();

            var nearby = pois
                .Select(p => new
                {
                    poi = p,
                    distance = HaversineDistanceMeters(lat, lng, p.Latitude, p.Longitude)
                })
                .Where(x => x.distance <= r)
                .OrderBy(x => x.distance)
                .Take(t)
                .ToList();

            return Ok(new
            {
                center = new { lat, lng },
                radius_meters = r,
                total = nearby.Count,
                items = nearby.Select(x => new { x.poi, distance_meters = Math.Round(x.distance, 2) })
            });
        }

        // 1. GET: api/Poi (Lấy danh sách)
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PoiModel>>> GetPois()
        {
            try
            {
                var q = _context.PointsOfInterest.AsQueryable();

                // Kiểm tra Admin bằng API Key
                var apiKey = HttpContext.Request.Headers["X-API-Key"].FirstOrDefault();
                var configuredKey = "admin123";
                var isAdminCaller = !string.IsNullOrEmpty(apiKey) && apiKey == configuredKey;

                if (!isAdminCaller)
                {
                    q = q.Where(p => p.IsPublished);
                }

                var ownerIdStr = HttpContext.Request.Query["ownerId"].FirstOrDefault();
                if (!string.IsNullOrEmpty(ownerIdStr) && int.TryParse(ownerIdStr, out var ownerId))
                {
                    var ownerHeader = HttpContext.Request.Headers["X-Owner-Id"].FirstOrDefault();
                    var ownerScopedCaller = int.TryParse(ownerHeader, out var ownerFromHeader) && ownerFromHeader == ownerId;
                    if (ownerScopedCaller)
                    {
                        q = _context.PointsOfInterest.AsQueryable();
                    }

                    q = q.Where(p => p.OwnerId == ownerId);
                }

                var result = await q.ToListAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // 2. GET: api/Poi/{id} - FIX LỖI 404/405 TRANG CHI TIẾT VÀ SỬA
        [HttpGet("{id}")]
        public async Task<ActionResult<PoiModel>> GetPoiById(int id)
        {
            var poi = await _context.PointsOfInterest
                                    .Include(p => p.Contents)
                                    .FirstOrDefaultAsync(m => m.Id == id);

            if (poi == null) return NotFound();

            if (!poi.IsPublished)
            {
                var isAdminCaller = IsAdminApiKeyCaller();
                var ownerHeader = HttpContext?.Request?.Headers["X-Owner-Id"].FirstOrDefault();
                var ownerScopedCaller = int.TryParse(ownerHeader, out var ownerIdFromHeader)
                                        && poi.OwnerId.HasValue
                                        && poi.OwnerId.Value == ownerIdFromHeader;

                if (!isAdminCaller && !ownerScopedCaller)
                {
                    return NotFound();
                }
            }

            return Ok(poi);
        }

        // 3. PUT: api/Poi/{id} - Cập nhật thông tin (broadcast via SignalR)
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdatePoi(int id, PoiModel model)
        {
            if (!CanEditPoi())
            {
                return Unauthorized("Not authorized to update POI");
            }

            if (id != model.Id) return BadRequest("ID mismatch");

            var poi = await _context.PointsOfInterest.FindAsync(id);
            if (poi == null) return NotFound();

            var oldName = poi.Name;

            // Cập nhật các trường dữ liệu
            poi.Name = model.Name;
            poi.Category = model.Category;
            poi.Latitude = model.Latitude;
            poi.Longitude = model.Longitude;
            poi.Radius = model.Radius;
            poi.Priority = model.Priority;
            poi.CooldownSeconds = model.CooldownSeconds;
            poi.ImageUrl = model.ImageUrl;
            if (model.OwnerId.HasValue && model.OwnerId.Value > 0)
            {
                poi.OwnerId = model.OwnerId;
            }
            poi.IsPublished = model.IsPublished;

            // ✅ Regenerate QR Code nếu tên thay đổi
            if (string.IsNullOrWhiteSpace(poi.QrCode) || !string.Equals(oldName, model.Name, StringComparison.Ordinal))
            {
                poi.QrCode = _qrCodeService.GenerateQrCode(poi.Id, poi.Name);
            }

            await _context.SaveChangesAsync();

            // ✅ Notify clients via SignalR
            await _hubContext.Clients.All.SendAsync("PoiUpdated", poi);

            return Ok(poi);
        }

        // 4. POST: api/Poi - Thêm mới (tự động generate QR Code)
        [HttpPost]
        public async Task<ActionResult<PoiModel>> PostPoi(PoiModel poi)
        {
            if (!CanEditPoi())
            {
                return Unauthorized("Not authorized to create POI");
            }

            _context.PointsOfInterest.Add(poi);
            await _context.SaveChangesAsync();

            // ✅ Auto-generate QR Code sau khi có Id thật
            if (string.IsNullOrWhiteSpace(poi.QrCode))
            {
                poi.QrCode = _qrCodeService.GenerateQrCode(poi.Id, poi.Name);
                await _context.SaveChangesAsync();
            }

            // ✅ Notify clients via SignalR
            await _hubContext.Clients.All.SendAsync("PoiAdded", poi);

            return CreatedAtAction(nameof(GetPoiById), new { id = poi.Id }, poi);
        }

        // 5. DELETE: api/Poi/{id} - Xóa địa điểm (broadcast via SignalR, cleanup audio files)
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePoi(int id)
        {
            if (!CanDeletePoi())
            {
                return Unauthorized("Not authorized to delete POI");
            }

            var poi = await _context.PointsOfInterest.FindAsync(id);
            if (poi == null) return NotFound();

            // ✅ Cleanup related audio files and disk storage BEFORE deleting
            // (Contents and AudioFiles will be cascade-deleted by EF Core)
            await _cleanupService.CleanupPoiAsync(id);

            _context.PointsOfInterest.Remove(poi);
            await _context.SaveChangesAsync();

            // ✅ Notify clients via SignalR
            await _hubContext.Clients.All.SendAsync("PoiDeleted", id);

            return NoContent();
        }

        private static string? DecorateAudioUrl(string? raw, string lang)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            var separator = raw.Contains('?') ? "&" : "?";
            var version = DateTime.UtcNow.Ticks;
            return $"{raw}{separator}v={version}&l={Uri.EscapeDataString(lang)}";
        }

        private static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000;
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                    + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
                    * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double ToRadians(double deg) => deg * (Math.PI / 180.0);

        private bool IsAdminApiKeyCaller()
        {
            var apiKey = HttpContext?.Request?.Headers["X-API-Key"].FirstOrDefault();
            return !string.IsNullOrWhiteSpace(apiKey) && apiKey == "admin123";
        }

        private bool CanEditPoi()
        {
            if (IsAdminApiKeyCaller()) return true;
            if (User?.Identity?.IsAuthenticated != true) return false;

            return User.IsInRole("Admin")
                   || User.IsInRole("SuperAdmin")
                   || User.IsInRole("Owner")
                   || User.IsInRole("admin")
                   || User.IsInRole("super_admin")
                   || User.IsInRole("owner");
        }

        private bool CanDeletePoi()
        {
            if (IsAdminApiKeyCaller()) return true;
            if (User?.Identity?.IsAuthenticated != true) return false;

            return User.IsInRole("Admin")
                   || User.IsInRole("SuperAdmin")
                   || User.IsInRole("admin")
                   || User.IsInRole("super_admin");
        }
    }
}