using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using VinhKhanh.API.Data;
using VinhKhanh.API.Models;
using VinhKhanh.API.Hubs;
using System.Security.Cryptography;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System;

namespace VinhKhanh.API.Controllers
{
    [Route("admin/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly IHubContext<SyncHub> _hub;

        public AuthController(AppDbContext db, IConfiguration config, IHubContext<SyncHub> hub)
        {
            _db = db;
            _config = config;
            _hub = hub;
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
                PermissionsJson = "owner.poi.read,owner.poi.create,owner.poi.update,owner.analytics.read",
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

            try
            {
                await _hub.Clients.All.SendAsync("OwnerRegistrationSubmitted", new
                {
                    registrationId = reg.Id,
                    userId = user.Id,
                    email = user.Email,
                    shopName = reg.ShopName,
                    submittedAt = reg.SubmittedAt
                });
            }
            catch
            {
                // ignore notification failures
            }

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
            var permissions = (user.PermissionsJson ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var token = GenerateJwt(user, permissions);
            return Ok(new { userId = user.Id, email = user.Email, role = user.Role, isVerified = user.IsVerified, permissions, accessToken = token, tokenType = "Bearer" });
        }

        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var resolvedUserId = ResolveCurrentUserId();
            if (resolvedUserId <= 0) return Unauthorized(new { error = "unauthorized" });

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == resolvedUserId);
            if (user == null) return Unauthorized(new { error = "user_not_found" });

            var permissions = (user.PermissionsJson ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return Ok(new
            {
                userId = user.Id,
                email = user.Email,
                role = user.Role,
                isVerified = user.IsVerified,
                permissions
            });
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
        {
            if (req == null
                || string.IsNullOrWhiteSpace(req.CurrentPassword)
                || string.IsNullOrWhiteSpace(req.NewPassword))
            {
                return BadRequest(new { error = "missing_password" });
            }

            if (req.NewPassword.Length < 8)
            {
                return BadRequest(new { error = "weak_password", minLength = 8 });
            }

            var resolvedUserId = ResolveCurrentUserId();
            if (resolvedUserId <= 0) return Unauthorized(new { error = "unauthorized" });

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == resolvedUserId);
            if (user == null) return Unauthorized(new { error = "user_not_found" });

            if (!VerifyPassword(req.CurrentPassword, user.PasswordHash))
            {
                return BadRequest(new { error = "current_password_invalid" });
            }

            user.PasswordHash = HashPassword(req.NewPassword);
            await _db.SaveChangesAsync();

            return Ok(new { message = "password_changed" });
        }

        private int ResolveCurrentUserId()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (int.TryParse(claim, out var idFromClaim) && idFromClaim > 0)
                {
                    return idFromClaim;
                }
            }

            if (Request.Headers.TryGetValue("X-User-Id", out var raw)
                && int.TryParse(raw.FirstOrDefault(), out var idFromHeader)
                && idFromHeader > 0)
            {
                return idFromHeader;
            }

            return 0;
        }

        private string GenerateJwt(User user, string[] permissions)
        {
            var issuer = _config["Jwt:Issuer"] ?? "VinhKhanh.API";
            var audience = _config["Jwt:Audience"] ?? "VinhKhanh.Clients";
            var key = _config["Jwt:Key"] ?? "dev-super-secret-key-please-change";
            var ttlMinutes = int.TryParse(_config["Jwt:AccessTokenMinutes"], out var m) ? Math.Clamp(m, 10, 24 * 60) : 180;

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(ClaimTypes.Role, NormalizeRoleForClaim(user.Role)),
                new Claim("role_raw", user.Role ?? string.Empty)
            };

            foreach (var perm in permissions.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                claims.Add(new Claim("permission", perm));
            }

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(ttlMinutes),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static string NormalizeRoleForClaim(string? role)
        {
            var value = (role ?? string.Empty).Trim().ToLowerInvariant();
            return value switch
            {
                "super_admin" => "SuperAdmin",
                "admin" => "Admin",
                "owner" => "Owner",
                _ => "User"
            };
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
    public class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}

