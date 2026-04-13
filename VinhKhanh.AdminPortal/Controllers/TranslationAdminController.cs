using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class TranslationAdminController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

        public TranslationAdminController(IHttpClientFactory factory, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _factory = factory;
            _config = config;
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

        public async Task<IActionResult> Index(int poiId)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            try
            {
                var contents = await client.GetFromJsonAsync<List<ContentModel>>($"api/content/by-poi/{poiId}");
                ViewData["PoiId"] = poiId;
                return View(contents ?? new List<ContentModel>());
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Không thể tải nội dung: " + ex.Message;
                return View(new List<ContentModel>());
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create(int poiId, ContentModel model)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            model.PoiId = poiId;
            var res = await client.PostAsJsonAsync("api/content", model);
            if (!res.IsSuccessStatusCode)
            {
                TempData["Error"] = "Tạo thất bại: " + res.StatusCode;
            }
            return RedirectToAction("Index", new { poiId });
        }

        [HttpPost]
        public async Task<IActionResult> Update(int id, int poiId, ContentModel model)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            var res = await client.PutAsJsonAsync($"api/content/{id}", model);
            if (!res.IsSuccessStatusCode) TempData["Error"] = "Cập nhật thất bại";
            return RedirectToAction("Index", new { poiId });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id, int poiId)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            await client.DeleteAsync($"api/content/{id}");
            return RedirectToAction("Index", new { poiId });
        }
    }
}
