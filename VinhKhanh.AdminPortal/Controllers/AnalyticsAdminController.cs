using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class AnalyticsAdminController : Controller
    {
        private readonly IHttpClientFactory _factory;

        public AnalyticsAdminController(IHttpClientFactory factory)
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

                var top = await client.GetFromJsonAsync<List<object>>("api/analytics/topPois?top=10");
                ViewData["TopPois"] = top ?? new List<object>();
                return View();
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Không thể tải analytics: " + ex.Message;
                return View();
            }
        }
    }
}
