using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class TraceLogAdminController : Controller
    {
        private readonly IHttpClientFactory _factory;

        public TraceLogAdminController(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", "admin123");
                var logs = await client.GetFromJsonAsync<List<TraceLog>>("api/analytics/logs?limit=200");
                return View(logs ?? new List<TraceLog>());
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Không thể tải lịch sử sử dụng: " + ex.Message;
                return View(new List<TraceLog>());
            }
        }
    }
}
