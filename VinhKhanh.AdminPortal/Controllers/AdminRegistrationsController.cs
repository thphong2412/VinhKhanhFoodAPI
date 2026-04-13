using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.AdminPortal.Models;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class AdminRegistrationsController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly IConfiguration _config;

        public AdminRegistrationsController(IHttpClientFactory factory, IConfiguration config)
        {
            _factory = factory;
            _config = config;
        }

        private void AddAuthHeader(HttpClient client)
        {
            var key = _config["ApiKey"] ?? "admin123";
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", key);
        }

        // Trang danh sách đơn chờ duyệt
        public async Task<IActionResult> Pending()
        {
            var client = _factory.CreateClient("api");
            AddAuthHeader(client);
            try
            {
                var list = await client.GetFromJsonAsync<List<OwnerRegistrationDto>>("api/admin/registrations/pending");
                return View(list ?? new List<OwnerRegistrationDto>());
            }
            catch { return View(new List<OwnerRegistrationDto>()); }
        }

        // FIX LỖI 404: Trang chi tiết (Dùng cho cái View .cshtml ông gửi)
        public async Task<IActionResult> Details(int id)
        {
            var client = _factory.CreateClient("api");
            AddAuthHeader(client);
            try
            {
                // Gọi API lấy thông tin theo ID
                var reg = await client.GetFromJsonAsync<OwnerRegistrationDto>($"api/admin/registrations/{id}");
                if (reg == null) return NotFound();
                return View(reg);
            }
            catch { return RedirectToAction("Pending"); }
        }

        [HttpPost]
        public async Task<IActionResult> Approve(int id, string notes)
        {
            var client = _factory.CreateClient("api");
            AddAuthHeader(client);
            // Gửi ghi chú lên API
            var res = await client.PostAsJsonAsync($"api/admin/registrations/{id}/approve", new { Notes = notes });
            if (res.IsSuccessStatusCode) TempData["Success"] = "Đã phê duyệt thành công!";
            return RedirectToAction("Pending");
        }

        [HttpPost]
        public async Task<IActionResult> Reject(int id, string notes)
        {
            var client = _factory.CreateClient("api");
            AddAuthHeader(client);
            var res = await client.PostAsJsonAsync($"api/admin/registrations/{id}/reject", new { Notes = notes });
            if (res.IsSuccessStatusCode) TempData["Success"] = "Đã từ chối đơn đăng ký!";
            return RedirectToAction("Pending");
        }
    }
}