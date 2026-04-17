using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;
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
            model.NormalizeCompositeFields();
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
            model.NormalizeCompositeFields();
            var res = await client.PutAsJsonAsync($"api/content/{id}", model);
            if (!res.IsSuccessStatusCode) TempData["Error"] = "Cập nhật thất bại";
            return RedirectToAction("Index", new { poiId });
        }

        [HttpGet]
        public async Task<IActionResult> AutoTranslate(int poiId, string languageCode)
        {
            if (poiId <= 0 || string.IsNullOrWhiteSpace(languageCode))
                return BadRequest(new { error = "invalid_request" });

            var targetLang = languageCode.Trim().ToLowerInvariant();
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            var contents = await client.GetFromJsonAsync<List<ContentModel>>($"api/content/by-poi/{poiId}") ?? new List<ContentModel>();
            var vi = contents.FirstOrDefault(x => string.Equals(x.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
            if (vi == null)
            {
                return NotFound(new { error = "missing_vi_source", message = "Chưa có nội dung tiếng Việt để làm gốc." });
            }

            var payload = new
            {
                targetLanguageCode = targetLang,
                source = new
                {
                    title = vi.Title,
                    subtitle = vi.Subtitle,
                    description = vi.Description,
                    priceMin = vi.PriceMin,
                    priceMax = vi.PriceMax,
                    rating = vi.Rating,
                    openTime = vi.OpenTime,
                    closeTime = vi.CloseTime,
                    phoneNumber = vi.PhoneNumber,
                    address = vi.Address
                }
            };

            var res = await client.PostAsJsonAsync("api/ai/translate-content", payload);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                return StatusCode((int)res.StatusCode, new { error = "auto_translate_failed", detail = body });
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return Ok(new
                {
                    languageCode = targetLang,
                    title = vi.Title ?? string.Empty,
                    subtitle = vi.Subtitle ?? string.Empty,
                    description = vi.Description ?? string.Empty,
                    priceMin = vi.PriceMin ?? string.Empty,
                    priceMax = vi.PriceMax ?? string.Empty,
                    rating = vi.Rating,
                    openTime = vi.OpenTime ?? string.Empty,
                    closeTime = vi.CloseTime ?? string.Empty,
                    phoneNumber = vi.PhoneNumber ?? string.Empty,
                    address = vi.Address ?? string.Empty,
                    fallback = true
                });
            }

            return Content(body, "application/json");
        }

        [HttpPost]
        public async Task<IActionResult> AutoTranslateAndUpsert(int poiId, string languageCode)
        {
            if (poiId <= 0 || string.IsNullOrWhiteSpace(languageCode))
                return BadRequest(new { ok = false, error = "invalid_request" });

            var targetLang = languageCode.Trim().ToLowerInvariant();
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            var contents = await client.GetFromJsonAsync<List<ContentModel>>($"api/content/by-poi/{poiId}") ?? new List<ContentModel>();
            var vi = contents.FirstOrDefault(x => string.Equals(x.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
            if (vi == null)
                return NotFound(new { ok = false, error = "missing_vi_source", message = "Chưa có nội dung tiếng Việt để làm gốc." });

            if (targetLang == "vi")
                return Ok(new { ok = true, message = "Ngôn ngữ vi là dữ liệu gốc, không cần dịch." });

            var payload = new
            {
                targetLanguageCode = targetLang,
                source = new
                {
                    title = vi.Title,
                    subtitle = vi.Subtitle,
                    description = vi.Description,
                    priceMin = vi.PriceMin,
                    priceMax = vi.PriceMax,
                    rating = vi.Rating,
                    openTime = vi.OpenTime,
                    closeTime = vi.CloseTime,
                    phoneNumber = vi.PhoneNumber,
                    address = vi.Address
                }
            };

            var transRes = await client.PostAsJsonAsync("api/ai/translate-content", payload);
            var body = await transRes.Content.ReadAsStringAsync();
            if (!transRes.IsSuccessStatusCode)
                return StatusCode((int)transRes.StatusCode, new { ok = false, error = "auto_translate_failed", detail = body });

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var model = new ContentModel
            {
                PoiId = poiId,
                LanguageCode = targetLang,
                Title = ReadString(root, "title") ?? vi.Title,
                Subtitle = ReadString(root, "subtitle") ?? vi.Subtitle,
                Description = ReadString(root, "description") ?? vi.Description,
                PriceMin = ReadString(root, "priceMin") ?? vi.PriceMin,
                PriceMax = ReadString(root, "priceMax") ?? vi.PriceMax,
                Rating = ReadDouble(root, "rating") ?? vi.Rating,
                OpenTime = ReadString(root, "openTime") ?? vi.OpenTime,
                CloseTime = ReadString(root, "closeTime") ?? vi.CloseTime,
                PhoneNumber = ReadString(root, "phoneNumber") ?? vi.PhoneNumber,
                Address = ReadString(root, "address") ?? vi.Address,
                IsTTS = false
            };
            model.NormalizeCompositeFields();

            var existing = contents.FirstOrDefault(x => string.Equals(x.LanguageCode, targetLang, StringComparison.OrdinalIgnoreCase));
            HttpResponseMessage saveRes;
            if (existing == null)
            {
                saveRes = await client.PostAsJsonAsync("api/content", model);
            }
            else
            {
                model.Id = existing.Id;
                saveRes = await client.PutAsJsonAsync($"api/content/{existing.Id}", model);
            }

            if (!saveRes.IsSuccessStatusCode)
            {
                var saveBody = await saveRes.Content.ReadAsStringAsync();
                return StatusCode((int)saveRes.StatusCode, new { ok = false, error = "save_failed", detail = saveBody });
            }

            return Ok(new
            {
                ok = true,
                updated = existing != null,
                message = existing == null
                    ? $"Đã dịch và tạo mới bản {targetLang.ToUpperInvariant()} thành công."
                    : $"Đã dịch và cập nhật bản {targetLang.ToUpperInvariant()} thành công."
            });
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

        private static string? ReadString(JsonElement root, string name)
            => root.TryGetProperty(name, out var p) ? p.GetString() : null;

        private static double? ReadDouble(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var p)) return null;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var n)) return n;
            if (p.ValueKind == JsonValueKind.String && double.TryParse(p.GetString(), out var parsed)) return parsed;
            return null;
        }
    }
}
