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
            return "admin123";
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
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
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

        // Redirect to new AdminRegistrations controller
        public IActionResult Registrations()
        {
            return RedirectToAction("Pending", "AdminRegistrations");
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
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("ModelState invalid");
                return View(model);
            }
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            try
            {
                // 1. Tạo POI
                var res = await client.PostAsJsonAsync("api/poi", model);
                if (!res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync();
                    _logger.LogWarning("API returned non-success: {Status} {Body}", res.StatusCode, body);
                    TempData["Error"] = "Tạo POI thất bại: " + res.StatusCode;
                    return View(model);
                }

                // Lấy POI vừa tạo từ response
                var createdPoiJson = await res.Content.ReadAsStringAsync();
                var createdPoi = System.Text.Json.JsonSerializer.Deserialize<PoiModel>(createdPoiJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // 2. Tạo chi tiết POI (Content) - Tiếng Việt
                if (!string.IsNullOrEmpty(Request.Form["ContentTitle_VI"]))
                {
                    var content = new VinhKhanh.Shared.ContentModel
                    {
                        PoiId = createdPoi.Id,
                        LanguageCode = "vi",
                        Title = Request.Form["ContentTitle_VI"],
                        Subtitle = Request.Form["ContentSubtitle_VI"],
                        Description = Request.Form["ContentDescription_VI"],
                        PriceRange = Request.Form["ContentPriceRange_VI"],
                        Rating = double.TryParse(Request.Form["ContentRating_VI"], out var r) ? r : 0,
                        OpeningHours = Request.Form["ContentOpeningHours_VI"],
                        PhoneNumber = Request.Form["ContentPhoneNumber_VI"],
                        Address = Request.Form["ContentAddress_VI"],
                        AudioUrl = "",
                        IsTTS = false,
                        ShareUrl = ""
                    };

                    await client.PostAsJsonAsync("api/content", content);
                }

                TempData["Success"] = "Tạo POI thành công.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling API to create POI");
                TempData["Error"] = "Lỗi khi gửi yêu cầu tới API: " + ex.Message;
                return View(model);
            }
        }

        public async Task<IActionResult> Edit(int id)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{id}");
            if (poi == null) return NotFound();
            return View(poi);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(PoiModel model)
        {
            if (!ModelState.IsValid)
                return View(model);
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            var res = await client.PutAsJsonAsync($"api/poi/{model.Id}", model);
            if (!res.IsSuccessStatusCode)
            {
                TempData["Error"] = "Cập nhật POI thất bại.";
                return View(model);
            }
            TempData["Success"] = "Cập nhật POI thành công.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            var res = await client.DeleteAsync($"api/poi/{id}");
            if (!res.IsSuccessStatusCode)
            {
                TempData["Error"] = "Xóa POI thất bại.";
            }
            else
            {
                TempData["Success"] = "Đã xóa POI.";
            }
            return RedirectToAction("Index");
        }
    }
}
