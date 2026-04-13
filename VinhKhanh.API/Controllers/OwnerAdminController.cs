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

            var result = new List<object>();
            foreach (var r in list)
            {
                var user = await _db.Users.FindAsync(r.UserId);
                result.Add(new 
                { 
                    r.Id, 
                    r.UserId, 
                    Email = user?.Email,
                    r.ShopName, 
                    r.ShopAddress, 
                    r.Status, 
                    r.SubmittedAt 
                });
            }
            return Ok(result);
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPending()
        {
            var list = await _db.OwnerRegistrations
                .Where(r => r.Status == "pending")
                .OrderByDescending(r => r.SubmittedAt)
                .ToListAsync();

            var result = new List<object>();
            foreach (var r in list)
            {
                var user = await _db.Users.FindAsync(r.UserId);
                result.Add(new 
                { 
                    r.Id, 
                    r.UserId, 
                    Email = user?.Email,
                    r.ShopName, 
                    r.ShopAddress, 
                    r.Status, 
                    r.SubmittedAt 
                });
            }
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetDetails(int id)
        {
            var reg = await _db.OwnerRegistrations.FindAsync(id);
            if (reg == null) return NotFound();

            var user = await _db.Users.FindAsync(reg.UserId);
            return Ok(new
            {
                reg.Id,
                reg.UserId,
                Email = user?.Email,
                reg.ShopName,
                reg.ShopAddress,
                CccdEncrypted = reg.CccdEncrypted,
                reg.Status,
                reg.SubmittedAt,
                reg.ReviewedAt,
                reg.Notes,
                reg.ReviewedBy
            });
        }

        [HttpPost("{id}/approve")]
        public async Task<IActionResult> Approve(int id, [FromBody] ReviewRequest req)
        {
            var reg = await _db.OwnerRegistrations.FindAsync(id);
            if (reg == null) return NotFound();
            if (reg.Status != "pending") return BadRequest("already_processed");

            reg.Status = "approved";
            reg.ReviewedAt = DateTime.UtcNow;
            reg.Notes = req?.Notes;
            // For POC assume admin id = 1
            reg.ReviewedBy = 1;

            var user = await _db.Users.FindAsync(reg.UserId);
            if (user != null)
            {
                user.IsVerified = true;
                user.Role = "owner";
            }

            await _db.SaveChangesAsync();

            return Ok(new { reg.Id, reg.Status });
        }

        [HttpPost("{id}/reject")]
        public async Task<IActionResult> Reject(int id, [FromBody] ReviewRequest req)
        {
            var reg = await _db.OwnerRegistrations.FindAsync(id);
            if (reg == null) return NotFound();
            if (reg.Status != "pending") return BadRequest("already_processed");

            reg.Status = "rejected";
            reg.ReviewedAt = DateTime.UtcNow;
            reg.ReviewedBy = 1;
            reg.Notes = req?.Notes;
            await _db.SaveChangesAsync();
            return Ok(new { reg.Id, reg.Status });
        }
    }

    public class ReviewRequest { 
        public string Notes { get; set; } 
    }

    public class RejectionRequest { 
        public string Reason { get; set; } 
    }
}
