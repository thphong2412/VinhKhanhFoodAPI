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

        public async Task<IActionResult> Index(int page = 1)
        {
            try
            {
                var pageSize = 10;
                page = Math.Max(1, page);
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
                var logs = await client.GetFromJsonAsync<List<TraceLogRowDto>>("api/analytics/logs?limit=200&hours=24&includeHeartbeats=false");
                var list = logs ?? new List<TraceLogRowDto>();
                var totalCount = list.Count;
                var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
                page = Math.Min(page, totalPages);

                ViewBag.Page = page;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = totalPages;
                ViewBag.TotalCount = totalCount;

                var paged = list
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return View(paged);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Không thể tải lịch sử sử dụng: " + ex.Message;
                return View(new List<TraceLogRowDto>());
            }
        }
    }
}
