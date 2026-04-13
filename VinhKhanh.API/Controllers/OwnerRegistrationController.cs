using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;
using VinhKhanh.API.Models;

namespace VinhKhanh.API.Controllers
{
    [Route("api/admin/registrations")]
    [ApiController]
    public class OwnerRegistrationController : ControllerBase
    {
        private readonly AppDbContext _context;
        public OwnerRegistrationController(AppDbContext context) => _context = context;

        [HttpGet("pending")]
        public async Task<ActionResult<IEnumerable<OwnerRegistration>>> GetPending()
        {
            return await _context.OwnerRegistrations.Where(r => r.Status == "pending").ToListAsync();
        }

        // FIX LỖI 404: API trả về dữ liệu chi tiết cho trang Admin
        [HttpGet("{id}")]
        public async Task<ActionResult<OwnerRegistration>> GetById(int id)
        {
            var reg = await _context.OwnerRegistrations.FindAsync(id);
            if (reg == null) return NotFound();
            return reg;
        }

        [HttpPost("{id}/approve")]
        public async Task<IActionResult> Approve(int id, [FromBody] RegistrationActionRequest req)
        {
            var reg = await _context.OwnerRegistrations.FindAsync(id);
            if (reg == null) return NotFound();
            reg.Status = "approved";
            reg.Notes = req?.Notes;
            reg.ReviewedAt = DateTime.Now;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("{id}/reject")]
        public async Task<IActionResult> Reject(int id, [FromBody] RegistrationActionRequest req)
        {
            var reg = await _context.OwnerRegistrations.FindAsync(id);
            if (reg == null) return NotFound();
            reg.Status = "rejected";
            reg.Notes = req?.Notes;
            reg.ReviewedAt = DateTime.Now;
            await _context.SaveChangesAsync();
            return Ok();
        }
    }

    public class RegistrationActionRequest { public string? Notes { get; set; } }
}