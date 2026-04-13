using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;

namespace VinhKhanh.AdminPortal.Controllers
{
    public class OwnerPortalController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;
        private readonly Microsoft.Extensions.Logging.ILogger<OwnerPortalController> _logger;

        public OwnerPortalController(IHttpClientFactory factory, Microsoft.Extensions.Configuration.IConfiguration config, Microsoft.Extensions.Logging.ILogger<OwnerPortalController> logger)
        {
            _factory = factory;
            _config = config;
            _logger = logger;
        }

        private string GetApiKey()
        {
            try
            {
                var configured = _config?["ApiKey"];
                if (!string.IsNullOrEmpty(configured)) return configured;
            }
            catch { }
            return "admin123";
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(string email, string password, string shopName, string shopAddress, string cccd)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError("", "Email và mật khẩu là bắt buộc");
                return View();
            }

            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            var req = new
            {
                Email = email,
                Password = password,
                ShopName = shopName,
                ShopAddress = shopAddress,
                Cccd = cccd
            };

            try
            {
                var res = await client.PostAsJsonAsync("admin/auth/register-owner", req);
                if (res.IsSuccessStatusCode)
                {
                    TempData["Success"] = "Đăng ký thành công. Chờ admin duyệt.";
                    return RedirectToAction("Login");
                }

                if (res.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    ModelState.AddModelError("", "Email đã tồn tại");
                    return View();
                }

                var body = await res.Content.ReadAsStringAsync();
                _logger.LogWarning("Register-owner failed: {Status} {Body}", res.StatusCode, body);
                ModelState.AddModelError("", "Đăng ký thất bại: " + res.StatusCode);
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling API register-owner");
                ModelState.AddModelError("", "Lỗi khi kết nối tới API: " + ex.Message);
                return View();
            }
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string email, string password)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError("", "Email và mật khẩu là bắt buộc");
                return View();
            }

            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            try
            {
                var res = await client.PostAsJsonAsync("admin/auth/login", new { Email = email, Password = password });
                if (!res.IsSuccessStatusCode)
                {
                    ModelState.AddModelError("", "Đăng nhập thất bại");
                    return View();
                }

                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var userId = root.GetProperty("userId").GetInt32();
                var isVerified = root.GetProperty("isVerified").GetBoolean();

                // Set simple cookie for owner session (POC). In production use secure authentication.
                HttpContext.Response.Cookies.Append("owner_userid", userId.ToString(), new Microsoft.AspNetCore.Http.CookieOptions { HttpOnly = true });
                HttpContext.Response.Cookies.Append("owner_verified", isVerified ? "1" : "0", new Microsoft.AspNetCore.Http.CookieOptions { HttpOnly = true });

                return RedirectToAction("Dashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling API login");
                ModelState.AddModelError("", "Lỗi khi kết nối tới API: " + ex.Message);
                return View();
            }
        }

        [HttpGet]
        public IActionResult Dashboard(int userId, bool verified = false)
        {
            // Read from cookie if available
            var uid = userId;
            if (uid == 0 && HttpContext.Request.Cookies.TryGetValue("owner_userid", out var v)) int.TryParse(v, out uid);
            var ver = verified;
            if (!ver && HttpContext.Request.Cookies.TryGetValue("owner_verified", out var vv)) ver = vv == "1";

            ViewData["UserId"] = uid;
            ViewData["Verified"] = ver;
            return View();
        }
    }
}
