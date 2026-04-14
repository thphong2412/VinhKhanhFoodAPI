using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;
using VinhKhanh.API.Models;
using System.Security.Cryptography;
using System.Text;
using System;

namespace VinhKhanh.API.Controllers
{
    [Route("admin/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AuthController(AppDbContext db)
        {
            _db = db;
        }

        [HttpPost("register-owner")]
        public async Task<IActionResult> RegisterOwner([FromBody] OwnerRegisterRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.Email) || string.IsNullOrEmpty(req.Password))
                return BadRequest("missing");

            // ✅ Normalize email
            var email = req.Email?.Trim().ToLower() ?? "";

            var exists = await _db.Users.AnyAsync(u => u.Email.ToLower() == email);
            if (exists) return Conflict("email_exists");

            var user = new User
            {
                Email = email,  // Store normalized email
                PasswordHash = HashPassword(req.Password),
                Role = "owner",
                IsVerified = false
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var enc = VinhKhanh.API.Services.EncryptionService.Protect(req.Cccd ?? string.Empty);

            var reg = new OwnerRegistration
            {
                UserId = user.Id,
                ShopName = req.ShopName,
                ShopAddress = req.ShopAddress,
                CccdEncrypted = enc,
                Status = "pending",
                Notes = ""
            };

            _db.OwnerRegistrations.Add(reg);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetRegistration), new { id = reg.Id }, new { userId = user.Id, registrationId = reg.Id, status = reg.Status });
        }

        [HttpGet("registration/{id}")]
        public async Task<IActionResult> GetRegistration(int id)
        {
            var reg = await _db.OwnerRegistrations.FindAsync(id);
            if (reg == null) return NotFound();
            return Ok(new { reg.Id, reg.UserId, reg.ShopName, reg.ShopAddress, reg.Status, reg.SubmittedAt });
        }

        // simple login for owner (email + password) returns minimal token (dev)
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.Email))
                return Unauthorized(new { error = "Email is required" });

            // ✅ Case-insensitive email lookup with trimming
            var email = req.Email?.Trim().ToLower() ?? "";
            System.Diagnostics.Debug.WriteLine($"[Login] Attempting login with email: '{email}' (original: '{req.Email}')");
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower().Trim() == email);

            if (user == null) 
            {
                System.Diagnostics.Debug.WriteLine($"[Login] User not found: {req.Email}");
                return Unauthorized(new { error = "Email hoặc mật khẩu không chính xác" });
            }

            if (!VerifyPassword(req.Password, user.PasswordHash)) 
            {
                System.Diagnostics.Debug.WriteLine($"[Login] Password mismatch for: {req.Email}");
                return Unauthorized(new { error = "Email hoặc mật khẩu không chính xác" });
            }

            System.Diagnostics.Debug.WriteLine($"[Login] Success: {user.Email} (UserId: {user.Id}, IsVerified: {user.IsVerified})");
            return Ok(new { userId = user.Id, email = user.Email, role = user.Role, isVerified = user.IsVerified });
        }

        private static string HashPassword(string password)
        {
            // POC: use salted SHA256; replace with bcrypt/Argon2 in production
            var salt = "static-salt"; // TODO: use per-user salt
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(salt + password);
            return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        private static bool VerifyPassword(string password, string hash)
        {
            return HashPassword(password) == hash;
        }
    }

    public class OwnerRegisterRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string ShopName { get; set; }
        public string ShopAddress { get; set; }
        public string Cccd { get; set; }
    }

    public class LoginRequest { public string Email { get; set; } public string Password { get; set; } }
}

