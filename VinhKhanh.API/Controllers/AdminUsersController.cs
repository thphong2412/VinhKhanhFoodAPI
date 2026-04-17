using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;
using VinhKhanh.API.Models;

namespace VinhKhanh.API.Controllers
{
    [Route("admin/users")]
    [ApiController]
    public class AdminUsersController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<AdminUsersController> _logger;

        public AdminUsersController(AppDbContext db, ILogger<AdminUsersController> logger)
        {
            _db = db;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            if (!Request.Headers.TryGetValue("X-API-Key", out var apiKey) || apiKey != "admin123")
                return Unauthorized("Invalid API Key");

            try
            {
                var users = await _db.Users
                    .AsNoTracking()
                    .OrderBy(u => u.Id)
                    .Select(u => new
                    {
                        u.Id,
                        u.Email,
                        u.Role,
                        u.IsVerified,
                        CreatedAt = EF.Property<DateTime?>(u, nameof(VinhKhanh.API.Models.User.CreatedAt)) ?? DateTime.UtcNow
                    })
                    .ToListAsync();

                var ownerRegs = await _db.OwnerRegistrations.AsNoTracking().ToListAsync();

                var merged = users.Select(u =>
                {
                    var reg = ownerRegs.FirstOrDefault(r => r.UserId == u.Id);
                    return new
                    {
                        u.Id,
                        u.Email,
                        u.Role,
                        u.IsVerified,
                        u.CreatedAt,
                        ShopName = reg?.ShopName,
                        ShopAddress = reg?.ShopAddress,
                        OwnerSubmittedAt = reg?.SubmittedAt,
                        OwnerReviewedAt = reg?.ReviewedAt,
                        OwnerRegistrationStatus = reg?.Status
                    };
                }).ToList();

                return Ok(merged);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AdminUsers.List failed, returning empty list to keep admin UI responsive");
                return Ok(Array.Empty<object>());
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();
            var reg = await _db.OwnerRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == user.Id);
            return Ok(new
            {
                user.Id,
                user.Email,
                user.Role,
                user.IsVerified,
                user.CreatedAt,
                ShopName = reg?.ShopName,
                ShopAddress = reg?.ShopAddress,
                OwnerSubmittedAt = reg?.SubmittedAt,
                OwnerReviewedAt = reg?.ReviewedAt,
                OwnerRegistrationStatus = reg?.Status
            });
        }

        [HttpGet("{id}/detail")]
        public async Task<IActionResult> GetDetail(int id)
        {
            if (!Request.Headers.TryGetValue("X-API-Key", out var apiKey) || apiKey != "admin123")
                return Unauthorized("Invalid API Key");

            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();

            var reg = await _db.OwnerRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == user.Id);

            var pois = await _db.PointsOfInterest
                .AsNoTracking()
                .Where(p => p.OwnerId == user.Id)
                .OrderByDescending(p => p.Id)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Category,
                    p.IsPublished,
                    p.Radius,
                    p.Priority,
                    p.CooldownSeconds,
                    p.Latitude,
                    p.Longitude
                })
                .ToListAsync();

            return Ok(new
            {
                user.Id,
                user.Email,
                user.Role,
                user.IsVerified,
                user.CreatedAt,
                ShopName = reg?.ShopName,
                ShopAddress = reg?.ShopAddress,
                OwnerSubmittedAt = reg?.SubmittedAt,
                OwnerReviewedAt = reg?.ReviewedAt,
                OwnerRegistrationStatus = reg?.Status,
                Pois = pois
            });
        }

        [HttpPost("{id}/toggle-verified")]
        public async Task<IActionResult> ToggleVerified(int id)
        {
            if (!Request.Headers.TryGetValue("X-API-Key", out var apiKey) || apiKey != "admin123")
                return Unauthorized("Invalid API Key");

            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();
            user.IsVerified = !user.IsVerified;
            await _db.SaveChangesAsync();
            return Ok(new { user.Id, user.IsVerified });
        }

        [HttpPost("{id}/update")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateRequest req)
        {
            if (!Request.Headers.TryGetValue("X-API-Key", out var apiKey) || apiKey != "admin123")
                return Unauthorized("Invalid API Key");

            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();
            if (!string.IsNullOrEmpty(req.Email)) user.Email = req.Email;
            await _db.SaveChangesAsync();
            return Ok(new { user.Id, user.Email });
        }

        public class UpdateRequest { public string Email { get; set; } }
    }
}
