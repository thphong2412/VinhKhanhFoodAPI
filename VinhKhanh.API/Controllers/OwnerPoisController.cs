using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VinhKhanh.API.Data;
using VinhKhanh.Shared;

namespace VinhKhanh.API.Controllers
{
    [Route("owner/pois")]
    [ApiController]
    public class OwnerPoisController : ControllerBase
    {
        private readonly AppDbContext _db;

        public OwnerPoisController(AppDbContext db)
        {
            _db = db;
        }

        // Owner lists their own POIs (requires cookie-based owner_userid on owner portal)
        [HttpGet("mine")]
        public async Task<IActionResult> MyPois()
        {
            // For POC the owner portal sets a cookie "owner_userid" to identify owner
            if (!HttpContext.Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var ownerId))
            {
                return Unauthorized();
            }
            var list = await _db.PointsOfInterest.Where(p => p.OwnerId == ownerId).ToListAsync();
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] PoiModel poi)
        {
            if (!HasPermission("owner.poi.create")) return Forbid();
            if (poi == null || !poi.OwnerId.HasValue) return BadRequest();

            var user = await _db.Users.FindAsync(poi.OwnerId.Value);
            if (user == null) return BadRequest("invalid_owner");
            if (!user.IsVerified) return BadRequest("owner_not_verified");

            // Owner-submitted POIs are not published by default
            poi.IsPublished = false;
            _db.PointsOfInterest.Add(poi);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(MyPois), new { poi.Id }, poi);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] PoiModel model)
        {
            if (!HasPermission("owner.poi.update")) return Forbid();
            var existing = await _db.PointsOfInterest.FindAsync(id);
            if (existing == null) return NotFound();
            if (existing.OwnerId != model.OwnerId) return Forbid();

            existing.Name = model.Name;
            existing.Category = model.Category;
            existing.Latitude = model.Latitude;
            existing.Longitude = model.Longitude;
            existing.Radius = model.Radius;
            existing.Priority = model.Priority;
            existing.CooldownSeconds = model.CooldownSeconds;
            existing.ImageUrl = model.ImageUrl;
            existing.QrCode = model.QrCode;
            await _db.SaveChangesAsync();
            return Ok(existing);
        }

        private bool HasPermission(string permission)
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")) return true;
                var claimPerms = User.FindAll("permission").Select(c => c.Value);
                if (claimPerms.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase))) return true;
            }

            if (Request.Headers.TryGetValue("X-API-Key", out var apiKey) && !string.IsNullOrWhiteSpace(apiKey.FirstOrDefault()))
            {
                return true;
            }

            if (!Request.Headers.TryGetValue("X-User-Id", out var uidRaw)) return false;
            if (!int.TryParse(uidRaw.FirstOrDefault(), out var userId) || userId <= 0) return false;

            var user = _db.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return false;

            if (string.Equals(user.Role, "super_admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var perms = (user.PermissionsJson ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return perms.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));
        }
    }
}
