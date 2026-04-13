using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;

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
            return "dev-key";
        }

        public async Task<IActionResult> Index()
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            try
            {
                var users = await client.GetFromJsonAsync<List<dynamic>>("admin/users");
                return View(users ?? new List<dynamic>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load owners");
                TempData["Error"] = "Không thể tải danh sách owners: " + ex.Message;
                return View(new List<dynamic>());
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
            var user = await client.GetFromJsonAsync<dynamic>($"admin/users/{id}");
            return View(user);
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
