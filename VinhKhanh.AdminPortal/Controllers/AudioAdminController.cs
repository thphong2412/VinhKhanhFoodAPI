using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class AudioAdminController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

        public AudioAdminController(IHttpClientFactory factory, Microsoft.Extensions.Configuration.IConfiguration config)
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
                var audios = await client.GetFromJsonAsync<List<AudioModel>>($"api/audio/by-poi/{poiId}");
                ViewData["PoiId"] = poiId;
                return View(audios ?? new List<AudioModel>());
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Không thể tải audio: " + ex.Message;
                return View(new List<AudioModel>());
            }
        }

        [HttpPost]
        public async Task<IActionResult> Upload(int poiId, string language)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            if (Request.Form.Files.Count == 0)
            {
                TempData["Error"] = "Chưa chọn file";
                return RedirectToAction("Index", new { poiId });
            }

            var file = Request.Form.Files[0];
            using var ms = new System.IO.MemoryStream();
            await file.CopyToAsync(ms);
            ms.Position = 0;

            var content = new MultipartFormDataContent();
            content.Add(new StringContent(poiId.ToString()), "poiId");
            content.Add(new StringContent(language ?? "vi"), "language");
            content.Add(new StreamContent(ms), "file", file.FileName);

            var res = await client.PostAsync("api/audio/upload", content);
            if (!res.IsSuccessStatusCode)
            {
                TempData["Error"] = "Upload thất bại: " + res.StatusCode;
            }
            else
            {
                TempData["Success"] = "Upload thành công";
            }

            return RedirectToAction("Index", new { poiId });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id, int poiId)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            await client.DeleteAsync($"api/audio/{id}");
            return RedirectToAction("Index", new { poiId });
        }
    }
}
