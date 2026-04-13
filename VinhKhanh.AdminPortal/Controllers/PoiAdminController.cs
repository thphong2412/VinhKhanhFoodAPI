    using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class PoiAdminController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly Microsoft.Extensions.Logging.ILogger<PoiAdminController> _logger;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

        public PoiAdminController(IHttpClientFactory factory, Microsoft.Extensions.Logging.ILogger<PoiAdminController> logger, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _factory = factory;
            _logger = logger;
            _config = config;
        }

        [HttpPost]
        public async Task<IActionResult> ApprovePoi(int id)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            await client.PostAsync($"admin/pois/{id}/approve", null);
            return RedirectToAction("Index");
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

        // Simple diagnostics endpoint to test connectivity to backend API
        public async Task<IActionResult> TestApi()
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            try
            {
                var pois = await client.GetFromJsonAsync<List<PoiModel>>("api/poi");
                var count = pois?.Count ?? 0;
                return Content($"API reachable. POI count={count}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TestApi: failed to call backend API");
                return Content($"API call failed: {ex.Message}");
            }
        }

        public async Task<IActionResult> Index()
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", "dev-key");
            try
            {
                // Correct endpoint path
                // If query string contains ownerId, filter by owner via API query param
                var qs = Request.Query["ownerId"].FirstOrDefault();
                var path = string.IsNullOrEmpty(qs) ? "api/poi" : $"api/poi?ownerId={qs}";
                var pois = await client.GetFromJsonAsync<List<PoiModel>>(path);
                return View(pois ?? new List<PoiModel>());
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                // API not available or connection refused — show friendly message and empty list
                TempData["Error"] = "Không thể kết nối tới API backend. Vui lòng khởi động VinhKhanh.API trước khi đăng nhập.";
                return View(new List<PoiModel>());
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi khi tải dữ liệu: " + ex.Message;
                return View(new List<PoiModel>());
            }
        }

        public async Task<IActionResult> Registrations()
        {
            try
            {
                var client = _factory.CreateClient("api");
                // Use configured ApiKey from environment or configuration. If not present fall back to dev-key.
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
                var regs = await client.GetFromJsonAsync<List<dynamic>>("admin/registrations?status=pending");
                return View("Registrations", regs ?? new List<dynamic>());
            }
            catch (System.Net.Http.HttpRequestException)
            {
                TempData["Error"] = "Không thể kết nối tới API backend. Vui lòng khởi động VinhKhanh.API.";
                return View("Registrations", new List<dynamic>());
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi khi tải đăng ký: " + ex.Message;
                return View("Registrations", new List<dynamic>());
            }
        }

        [HttpPost]
        public async Task<IActionResult> ApproveRegistration(int id)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", "dev-key");
            await client.PostAsync($"admin/registrations/{id}/approve", null);
            return RedirectToAction("Registrations");
        }

        public IActionResult RejectRegistration(int id)
        {
            ViewData["Id"] = id;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> DoReject(int id, string reason)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", "dev-key");
            await client.PostAsJsonAsync($"admin/registrations/{id}/reject", new { Reason = reason });
            return RedirectToAction("Registrations");
        }

        public IActionResult Create() => View();

        // Owner-facing create page — prefill ownerId if cookie present
        public IActionResult OwnerCreate()
        {
            if (HttpContext.Request.Cookies.TryGetValue("owner_userid", out var v) && int.TryParse(v, out var uid))
            {
                ViewData["OwnerId"] = uid;
            }
            return View("Create");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PoiModel model)
        {
            _logger.LogInformation("PoiAdminController.Create POST called");
            try
            {
                if (Request.HasFormContentType)
                {
                    var keys = Request.Form.Keys;
                    _logger.LogInformation("Form keys: {Keys}", string.Join(",", keys));
                    foreach (var k in keys)
                    {
                        _logger.LogInformation("Form[{Key}] = {Value}", k, Request.Form[k]);
                    }
                }
                else
                {
                    _logger.LogInformation("Request has no form content");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read Request.Form");
            }
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("ModelState invalid: {Errors}", string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                return View(model);
            }
            var client = _factory.CreateClient("api");
            // Ensure the API key header is present for non-GET requests
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", "dev-key");

            try
            {
                var res = await client.PostAsJsonAsync("api/poi", model);
                if (!res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync();
                    _logger.LogWarning("API returned non-success: {Status} {Body}", res.StatusCode, body);
                    TempData["Error"] = "Tạo POI thất bại: " + res.StatusCode;
                    return View(model);
                }

                TempData["Success"] = "Tạo POI thành công.";
                _logger.LogInformation("POI created successfully via API");
                // Redirect to Index so user sees the list including the new POI
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling API to create POI");
                TempData["Error"] = "Lỗi khi gửi yêu cầu tới API: " + ex.Message;
                return View(model);
            }
        }
    }
}
