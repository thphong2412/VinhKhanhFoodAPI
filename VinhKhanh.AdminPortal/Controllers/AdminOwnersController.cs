using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.AdminPortal.Models;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class AdminOwnersController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;
        private readonly Microsoft.Extensions.Logging.ILogger<AdminOwnersController> _logger;

        public AdminOwnersController(IHttpClientFactory factory, Microsoft.Extensions.Configuration.IConfiguration config, Microsoft.Extensions.Logging.ILogger<AdminOwnersController> logger)
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
                var users = await client.GetFromJsonAsync<List<UserDto>>("admin/users");
                return View(users ?? new List<UserDto>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load owners");
                TempData["Error"] = "Không thể tải danh sách owners: " + ex.Message;
                return View(new List<UserDto>());
            }
        }

        [HttpPost]
        public async Task<IActionResult> ToggleVerified(int id)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            await client.PostAsync($"admin/users/{id}/toggle-verified", null);
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Edit(int id)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            try
            {
                var user = await client.GetFromJsonAsync<UserDto>($"admin/users/{id}");
                return View(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load owner");
                TempData["Error"] = "Không thể tải thông tin owner: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        public async Task<IActionResult> Details(int id)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            try
            {
                var detail = await client.GetFromJsonAsync<OwnerDetailDto>($"admin/users/{id}/detail");
                if (detail == null)
                {
                    TempData["Error"] = "Không tìm thấy owner.";
                    return RedirectToAction("Index");
                }

                return View(detail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load owner details");
                TempData["Error"] = "Không thể tải chi tiết owner: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Save(int id, string email)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            await client.PostAsJsonAsync($"admin/users/{id}/update", new { Email = email });
            return RedirectToAction("Index");
        }
    }
}
