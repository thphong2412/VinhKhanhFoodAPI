using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;

namespace VinhKhanh.API.Controllers
{
    [Route("admin/users")]
    [ApiController]
    [Authorize(Policy = "AdminApi")]
    public class AdminUsersController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AdminUsersController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var users = await _db.Users.OrderBy(u => u.Id).ToListAsync();
            return Ok(users.Select(u => new { u.Id, u.Email, u.Role, u.IsVerified, u.CreatedAt }));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();
            return Ok(new { user.Id, user.Email, user.Role, user.IsVerified, user.CreatedAt });
        }

        [HttpPost("{id}/toggle-verified")]
        public async Task<IActionResult> ToggleVerified(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();
            user.IsVerified = !user.IsVerified;
            await _db.SaveChangesAsync();
            return Ok(new { user.Id, user.IsVerified });
        }

        [HttpPost("{id}/update")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateRequest req)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();
            if (!string.IsNullOrEmpty(req.Email)) user.Email = req.Email;
            await _db.SaveChangesAsync();
            return Ok(new { user.Id, user.Email });
        }

        public class UpdateRequest { public string Email { get; set; } }
    }
}
