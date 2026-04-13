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
        public TourAdminController(IHttpClientFactory factory)
        {
            _factory = factory;
        }
        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Quản lý tour";
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", "admin123");
                var tours = await client.GetFromJsonAsync<List<TourModel>>("api/tour");
                return View(tours ?? new List<TourModel>());
            }
            catch
            {
                return View(new List<TourModel>());
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
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", "admin123");
                var res = await client.PostAsJsonAsync("api/tour", model);
                if (res.IsSuccessStatusCode)
                {
                    return RedirectToAction("Index");
                }
                ModelState.AddModelError("", "Tạo tour thất bại: " + res.StatusCode);
            }
            catch (System.Exception ex)
            {
                ModelState.AddModelError("", "Lỗi: " + ex.Message);
            }
            return View(model);
        }
    }
}
