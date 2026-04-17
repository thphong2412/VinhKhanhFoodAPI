using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using VinhKhanh.API.Data;
using VinhKhanh.API.Models;
using VinhKhanh.API.Services;

namespace VinhKhanh.API.Controllers
{
    [Route("admin/pois")]
    [ApiController]
    public class AdminPoisController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<VinhKhanh.API.Hubs.SyncHub> _hub;
        private readonly IQrCodeService _qrCodeService;

        public AdminPoisController(AppDbContext db, Microsoft.AspNetCore.SignalR.IHubContext<VinhKhanh.API.Hubs.SyncHub> hub, IQrCodeService qrCodeService)
        {
            _db = db;
            _hub = hub;
            _qrCodeService = qrCodeService;
        }

        [HttpGet("pending")]
        public async Task<IActionResult> Pending()
        {
            if (!IsAdminAuthorized()) return Unauthorized("Not authorized");
            var list = await _db.PointsOfInterest.Where(p => !p.IsPublished).OrderByDescending(p => p.Id).ToListAsync();
            return Ok(list);
        }

        [HttpPost("{id}/approve")]
        public async Task<IActionResult> Approve(int id)
        {
            if (!IsAdminAuthorized()) return Unauthorized("Not authorized");
            var poi = await _db.PointsOfInterest.FindAsync(id);
            if (poi == null) return NotFound();
            poi.IsPublished = true;
            await _db.SaveChangesAsync();

            try
            {
                if (_hub != null)
                {
                    await _hub.Clients.All.SendCoreAsync("PoiCreated", new object[] { new { poi.Id, poi.Name, poi.Latitude, poi.Longitude, OwnerId = poi.OwnerId, IsPublished = poi.IsPublished } }, System.Threading.CancellationToken.None);
                }
            }
            catch { }

            return Ok(new { poi.Id, poi.IsPublished });
        }

        [HttpPost("{id}/publish")]
        public async Task<IActionResult> Publish(int id)
        {
            if (!IsAdminAuthorized()) return Unauthorized("Not authorized");
            var poi = await _db.PointsOfInterest.FindAsync(id);
            if (poi == null) return NotFound();

            poi.IsPublished = true;
            await _db.SaveChangesAsync();

            try
            {
                await _hub.Clients.All.SendCoreAsync("PoiUpdated", new object[] { poi }, System.Threading.CancellationToken.None);
            }
            catch { }

            return Ok(new { poi.Id, poi.IsPublished });
        }

        [HttpPost("{id}/unpublish")]
        public async Task<IActionResult> Unpublish(int id)
        {
            if (!IsAdminAuthorized()) return Unauthorized("Not authorized");
            var poi = await _db.PointsOfInterest.FindAsync(id);
            if (poi == null) return NotFound();

            poi.IsPublished = false;
            await _db.SaveChangesAsync();

            try
            {
                await _hub.Clients.All.SendCoreAsync("PoiUpdated", new object[] { poi }, System.Threading.CancellationToken.None);
            }
            catch { }

            return Ok(new { poi.Id, poi.IsPublished });
        }

        [HttpPost("bulk/publish")]
        public async Task<IActionResult> BulkPublish([FromBody] BulkPoiActionRequest req)
        {
            if (!IsAdminAuthorized()) return Unauthorized("Not authorized");
            var ids = req?.PoiIds?.Distinct().ToList() ?? new List<int>();
            if (!ids.Any()) return BadRequest("empty_ids");

            var pois = await _db.PointsOfInterest.Where(p => ids.Contains(p.Id)).ToListAsync();
            foreach (var poi in pois)
            {
                poi.IsPublished = true;
            }
            await _db.SaveChangesAsync();

            try
            {
                foreach (var poi in pois)
                {
                    await _hub.Clients.All.SendCoreAsync("PoiUpdated", new object[] { poi }, System.Threading.CancellationToken.None);
                }
            }
            catch { }

            return Ok(new { updated = pois.Count });
        }

        [HttpPost("bulk/unpublish")]
        public async Task<IActionResult> BulkUnpublish([FromBody] BulkPoiActionRequest req)
        {
            if (!IsAdminAuthorized()) return Unauthorized("Not authorized");
            var ids = req?.PoiIds?.Distinct().ToList() ?? new List<int>();
            if (!ids.Any()) return BadRequest("empty_ids");

            var pois = await _db.PointsOfInterest.Where(p => ids.Contains(p.Id)).ToListAsync();
            foreach (var poi in pois)
            {
                poi.IsPublished = false;
            }
            await _db.SaveChangesAsync();

            try
            {
                foreach (var poi in pois)
                {
                    await _hub.Clients.All.SendCoreAsync("PoiUpdated", new object[] { poi }, System.Threading.CancellationToken.None);
                }
            }
            catch { }

            return Ok(new { updated = pois.Count });
        }

        [HttpPost("bulk/delete")]
        public async Task<IActionResult> BulkDelete([FromBody] BulkPoiActionRequest req)
        {
            if (!IsAdminAuthorized()) return Unauthorized("Not authorized");
            var ids = req?.PoiIds?.Distinct().ToList() ?? new List<int>();
            if (!ids.Any()) return BadRequest("empty_ids");

            var pois = await _db.PointsOfInterest.Where(p => ids.Contains(p.Id)).ToListAsync();
            if (!pois.Any()) return Ok(new { deleted = 0 });

            var poiIds = pois.Select(p => p.Id).ToList();
            var contents = await _db.PointContents.Where(c => poiIds.Contains(c.PoiId)).ToListAsync();
            var audios = await _db.AudioFiles.Where(a => poiIds.Contains(a.PoiId)).ToListAsync();

            if (contents.Any()) _db.PointContents.RemoveRange(contents);
            if (audios.Any()) _db.AudioFiles.RemoveRange(audios);
            _db.PointsOfInterest.RemoveRange(pois);

            await _db.SaveChangesAsync();

            try
            {
                foreach (var id in poiIds)
                {
                    await _hub.Clients.All.SendCoreAsync("PoiDeleted", new object[] { id }, System.Threading.CancellationToken.None);
                }
            }
            catch { }

            return Ok(new { deleted = pois.Count });
        }

        [HttpGet("overview")]
        public async Task<IActionResult> Overview()
        {
            if (!IsAdminAuthorized()) return Unauthorized("Not authorized");
            var now = DateTime.UtcNow;
            var sinceHeartbeat = now.AddMinutes(-20);

            var pois = await _db.PointsOfInterest.AsNoTracking().ToListAsync();
            var ownerUsers = await _db.Users.AsNoTracking().ToListAsync();
            var ownerRegs = await _db.OwnerRegistrations.AsNoTracking().ToListAsync();
            var poiRegs = await _db.PoiRegistrations.AsNoTracking().ToListAsync();
            var contents = await _db.PointContents.AsNoTracking().ToListAsync();
            var audios = await _db.AudioFiles.AsNoTracking().ToListAsync();
            var logs = await _db.TraceLogs.AsNoTracking().Where(t => t.TimestampUtc >= sinceHeartbeat).ToListAsync();

            var result = pois.Select(p =>
            {
                var owner = ownerUsers.FirstOrDefault(u => u.Id == p.OwnerId);
                var ownerReg = owner != null ? ownerRegs.FirstOrDefault(r => r.UserId == owner.Id) : null;
                var approvedPoiReg = poiRegs
                    .Where(r => r.ApprovedPoiId == p.Id && string.Equals(r.Status, "approved", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.ReviewedAt)
                    .FirstOrDefault();
                var pendingPoiReg = poiRegs
                    .Where(r => string.Equals(r.Status, "pending", StringComparison.OrdinalIgnoreCase)
                                && (
                                    (string.Equals(r.RequestType, "update", StringComparison.OrdinalIgnoreCase) && r.TargetPoiId == p.Id)
                                    || (string.Equals(r.RequestType, "delete", StringComparison.OrdinalIgnoreCase) && r.TargetPoiId == p.Id)
                                ))
                    .OrderByDescending(r => r.SubmittedAt)
                    .FirstOrDefault();
                var poiContents = contents.Where(c => c.PoiId == p.Id).ToList();
                var poiAudios = audios.Where(a => a.PoiId == p.Id).ToList();
                var poiLogs = logs.Where(l => l.PoiId == p.Id && l.ExtraJson != null && l.ExtraJson.Contains("poi_heartbeat", StringComparison.OrdinalIgnoreCase)).ToList();

                var hasImage = !string.IsNullOrWhiteSpace(p.ImageUrl);
                var hasAnyContent = poiContents.Any();
                var hasContentVi = poiContents.Any(c => string.Equals(c.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
                var hasContentEn = poiContents.Any(c => string.Equals(c.LanguageCode, "en", StringComparison.OrdinalIgnoreCase));
                var hasAnyAudio = poiAudios.Any();
                var hasAudioVi = poiAudios.Any(a => string.Equals(a.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
                var hasAudioEn = poiAudios.Any(a => string.Equals(a.LanguageCode, "en", StringComparison.OrdinalIgnoreCase));
                var lastHeartbeat = poiLogs.OrderByDescending(x => x.TimestampUtc).Select(x => (DateTime?)x.TimestampUtc).FirstOrDefault();

                var warnings = new List<string>();
                if (!hasImage) warnings.Add("Thiếu ảnh");
                if (!hasContentVi) warnings.Add("Thiếu nội dung VI");
                if (!hasContentEn) warnings.Add("Thiếu nội dung EN");
                if (!hasAudioVi) warnings.Add("Thiếu audio VI");
                if (!hasAudioEn) warnings.Add("Thiếu audio EN");

                return new
                {
                    p.Id,
                    p.Name,
                    p.Category,
                    p.Latitude,
                    p.Longitude,
                    p.Radius,
                    p.Priority,
                    p.IsPublished,
                    p.IsSaved,
                    p.OwnerId,
                    OwnerEmail = owner?.Email,
                    OwnerName = ownerReg?.ShopName,
                    ApprovedAtUtc = approvedPoiReg?.ReviewedAt,
                    PendingRequestType = pendingPoiReg?.RequestType,
                    PendingRequestSubmittedAtUtc = pendingPoiReg?.SubmittedAt,
                    HasImage = hasImage,
                    HasAnyContent = hasAnyContent,
                    HasContentVi = hasContentVi,
                    HasContentEn = hasContentEn,
                    HasAnyAudio = hasAnyAudio,
                    HasAudioVi = hasAudioVi,
                    HasAudioEn = hasAudioEn,
                    LastHeartbeatUtc = lastHeartbeat,
                    HeartbeatCountLast20m = poiLogs.Count,
                    Warnings = warnings
                };
            }).ToList();

            return Ok(result);
        }

        [HttpPost("regen-qr-all")]
        public async Task<IActionResult> RegenerateQrAll()
        {
            if (!IsAdminAuthorized()) return Unauthorized("Not authorized");

            var pois = await _db.PointsOfInterest.ToListAsync();
            if (!pois.Any()) return Ok(new { updated = 0, message = "no_pois" });

            var updated = 0;
            foreach (var poi in pois)
            {
                poi.QrCode = _qrCodeService.GenerateQrCode(poi.Id, poi.Name ?? $"POI {poi.Id}");
                updated++;
            }

            await _db.SaveChangesAsync();

            try
            {
                foreach (var poi in pois)
                {
                    await _hub.Clients.All.SendCoreAsync("PoiUpdated", new object[] { poi }, System.Threading.CancellationToken.None);
                }
            }
            catch { }

            return Ok(new { updated, message = "regen_qr_all_done" });
        }

        private bool IsAdminAuthorized()
        {
            var apiKey = Request.Headers["X-API-Key"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(apiKey) && apiKey == "admin123")
            {
                return true;
            }

            if (User?.Identity?.IsAuthenticated == true)
            {
                return User.IsInRole("Admin")
                    || User.IsInRole("SuperAdmin")
                    || User.IsInRole("admin")
                    || User.IsInRole("super_admin");
            }

            return false;
        }
    }

    public class BulkPoiActionRequest
    {
        public List<int> PoiIds { get; set; } = new();
    }
}
