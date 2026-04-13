using Microsoft.AspNetCore.Mvc;
using VinhKhanh.Shared;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Linq;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class TourAdminController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly IConfiguration _config;
        private readonly ILogger<TourAdminController> _logger;

        public TourAdminController(IHttpClientFactory factory, IConfiguration config, ILogger<TourAdminController> logger)
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
            ViewData["Title"] = "Quản lý tour";
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
                var tours = await client.GetFromJsonAsync<List<TourModel>>("api/tour");
                return View(tours ?? new List<TourModel>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching tours");
                TempData["Error"] = "Lỗi khi tải danh sách tour: " + ex.Message;
                return View(new List<TourModel>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
                var tour = await client.GetFromJsonAsync<TourModel>($"api/tour/{id}");
                if (tour == null) return NotFound();
                return View(tour);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching tour details");
                TempData["Error"] = "Lỗi khi tải chi tiết tour: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new TourModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TourModel model)
        {
            // Lấy danh sách POI từ form (chuỗi id, cách nhau bởi dấu phẩy)
            var poiIdsRaw = Request.Form["PoiIdsRaw"].ToString();
            if (!string.IsNullOrWhiteSpace(poiIdsRaw))
            {
                model.PoiIds = poiIdsRaw.Split(',').Select(s => int.TryParse(s.Trim(), out var id) ? id : 0).Where(id => id > 0).ToList();
            }
            else
            {
                model.PoiIds = new List<int>();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
                var res = await client.PostAsJsonAsync("api/tour", model);
                if (res.IsSuccessStatusCode)
                {
                    TempData["Success"] = "Tạo tour thành công.";
                    return RedirectToAction("Index");
                }
                var errorContent = await res.Content.ReadAsStringAsync();
                _logger.LogWarning("Tour creation failed: {Status} {Content}", res.StatusCode, errorContent);
                TempData["Error"] = "Tạo tour thất bại: " + res.StatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating tour");
                TempData["Error"] = "Lỗi: " + ex.Message;
            }
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
                var tour = await client.GetFromJsonAsync<TourModel>($"api/tour/{id}");
                if (tour == null) return NotFound();
                return View(tour);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching tour for edit");
                TempData["Error"] = "Lỗi khi tải tour: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(TourModel model)
        {
            var poiIdsRaw = Request.Form["PoiIdsRaw"].ToString();
            if (!string.IsNullOrWhiteSpace(poiIdsRaw))
            {
                model.PoiIds = poiIdsRaw.Split(',').Select(s => int.TryParse(s.Trim(), out var id) ? id : 0).Where(id => id > 0).ToList();
            }
            else
            {
                model.PoiIds = new List<int>();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
                var res = await client.PutAsJsonAsync($"api/tour/{model.Id}", model);
                if (res.IsSuccessStatusCode)
                {
                    TempData["Success"] = "Cập nhật tour thành công.";
                    return RedirectToAction("Index");
                }
                TempData["Error"] = "Cập nhật tour thất bại: " + res.StatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating tour");
                TempData["Error"] = "Lỗi: " + ex.Message;
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
                var res = await client.DeleteAsync($"api/tour/{id}");
                if (res.IsSuccessStatusCode)
                {
                    TempData["Success"] = "Xóa tour thành công.";
                }
                else
                {
                    TempData["Error"] = "Xóa tour thất bại: " + res.StatusCode;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting tour");
                TempData["Error"] = "Lỗi: " + ex.Message;
            }
            return RedirectToAction("Index");
        }
    }
}
