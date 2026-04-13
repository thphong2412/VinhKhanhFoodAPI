using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;
using VinhKhanh.API.Models;

namespace VinhKhanh.API.Controllers
{
    [Route("admin/registrations")]
    [ApiController]
    public class OwnerAdminController : ControllerBase
    {
        private readonly AppDbContext _db;

        public OwnerAdminController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery]string status = "pending")
        {
            var q = _db.OwnerRegistrations.AsQueryable();
            if (!string.IsNullOrEmpty(status)) q = q.Where(r => r.Status == status);
            var list = await q.OrderByDescending(r => r.SubmittedAt).ToListAsync();
            return Ok(list.Select(r => new { r.Id, r.UserId, r.ShopName, r.ShopAddress, r.Status, r.SubmittedAt }));
        }

        [HttpPost("{id}/approve")]
        public async Task<IActionResult> Approve(int id)
        {
            var reg = await _db.OwnerRegistrations.FindAsync(id);
            if (reg == null) return NotFound();
            if (reg.Status != "pending") return BadRequest("already_processed");

            reg.Status = "approved";
            reg.ReviewedAt = DateTime.UtcNow;
            // For POC assume admin id = 1
            reg.ReviewedBy = 1;

            var user = await _db.Users.FindAsync(reg.UserId);
            if (user != null)
            {
                user.IsVerified = true;
                // Optionally set role to owner (already default)
                user.Role = "owner";
            }

            // NOTE: In this POC we don't auto-assign existing POIs. Admin can later reassign POIs manually if needed.

            await _db.SaveChangesAsync();

            return Ok(new { reg.Id, reg.Status });
        }

        [HttpPost("{id}/reject")]
        public async Task<IActionResult> Reject(int id, [FromBody] RejectionRequest req)
        {
            var reg = await _db.OwnerRegistrations.FindAsync(id);
            if (reg == null) return NotFound();
            if (reg.Status != "pending") return BadRequest("already_processed");

            reg.Status = "rejected";
            reg.ReviewedAt = DateTime.UtcNow;
            reg.ReviewedBy = 1;
            reg.Notes = req?.Reason;
            await _db.SaveChangesAsync();
            return Ok(new { reg.Id, reg.Status });
        }
    }

    public class RejectionRequest { public string Reason { get; set; } }
}
