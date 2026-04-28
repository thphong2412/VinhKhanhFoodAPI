using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.AdminPortal.Models;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class TraceLogAdminController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly IConfiguration _config;

        public TraceLogAdminController(IHttpClientFactory factory, IConfiguration config)
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

        public async Task<IActionResult> Index()
        {
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
                var logs = await client.GetFromJsonAsync<List<TraceLogRowDto>>("api/analytics/logs?limit=200&hours=24&includeHeartbeats=false");
                return View(logs ?? new List<TraceLogRowDto>());
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Không thể tải lịch sử sử dụng: " + ex.Message;
                return View(new List<TraceLogRowDto>());
            }
        }
    }
}
