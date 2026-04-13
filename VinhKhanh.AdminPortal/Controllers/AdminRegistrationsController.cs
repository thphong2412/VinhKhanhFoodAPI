using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.AdminPortal.Models;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class AdminRegistrationsController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;
        private readonly Microsoft.Extensions.Logging.ILogger<AdminRegistrationsController> _logger;

        public AdminRegistrationsController(IHttpClientFactory factory, Microsoft.Extensions.Configuration.IConfiguration config, Microsoft.Extensions.Logging.ILogger<AdminRegistrationsController> logger)
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

        public async Task<IActionResult> Index()
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            try
            {
                var registrations = await client.GetFromJsonAsync<List<OwnerRegistrationDto>>("admin/registrations");
                return View(registrations ?? new List<OwnerRegistrationDto>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load registrations");
                TempData["Error"] = "Không thể tải danh sách đăng ký: " + ex.Message;
                return View(new List<OwnerRegistrationDto>());
            }
        }

        public async Task<IActionResult> Pending()
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            try
            {
                var registrations = await client.GetFromJsonAsync<List<OwnerRegistrationDto>>("admin/registrations/pending");
                return View(registrations ?? new List<OwnerRegistrationDto>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load pending registrations");
                TempData["Error"] = "Không thể tải danh sách chờ duyệt: " + ex.Message;
                return View(new List<OwnerRegistrationDto>());
            }
        }

        public async Task<IActionResult> Details(int id)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            try
            {
                var registration = await client.GetFromJsonAsync<OwnerRegistrationDto>($"admin/registrations/{id}");
                return View(registration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load registration details");
                TempData["Error"] = "Không thể tải chi tiết đăng ký: " + ex.Message;
                return RedirectToAction("Pending");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Approve(int id, string notes = "")
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            try
            {
                var res = await client.PostAsJsonAsync($"admin/registrations/{id}/approve", new { Notes = notes });
                if (res.IsSuccessStatusCode)
                {
                    TempData["Success"] = "Phê duyệt thành công";
                    return RedirectToAction("Pending");
                }
                else
                {
                    TempData["Error"] = "Phê duyệt thất bại";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to approve registration");
                TempData["Error"] = "Lỗi: " + ex.Message;
            }
            return RedirectToAction("Details", new { id });
        }

        [HttpPost]
        public async Task<IActionResult> Reject(int id, string notes = "")
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            try
            {
                var res = await client.PostAsJsonAsync($"admin/registrations/{id}/reject", new { Notes = notes });
                if (res.IsSuccessStatusCode)
                {
                    TempData["Success"] = "Từ chối thành công";
                    return RedirectToAction("Pending");
                }
                else
                {
                    TempData["Error"] = "Từ chối thất bại";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reject registration");
                TempData["Error"] = "Lỗi: " + ex.Message;
            }
            return RedirectToAction("Details", new { id });
        }
    }
}
