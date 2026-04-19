using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class TranslationAdminController : Controller
    {
        private static readonly string[] SupportedTranslationLanguages = { "en", "ja", "ko", "zh", "ru", "th", "es", "fr" };
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
                var contents = await client.GetFromJsonAsync<List<ContentModel>>($"api/content/by-poi/{poiId}") ?? new List<ContentModel>();
                contents = await EnsureAllLanguageTranslationsAsync(client, poiId, contents);
                ViewData["PoiId"] = poiId;
                return View(contents);
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
                var normalizedFallback = BuildNormalizedContentViewModel(targetLang, vi.Title, vi.Subtitle, vi.Description, vi.PriceMin, vi.PriceMax, vi.Rating, vi.OpenTime, vi.CloseTime, vi.PhoneNumber, vi.Address);
                return Ok(new
                {
                    languageCode = normalizedFallback.LanguageCode,
                    title = normalizedFallback.Title ?? string.Empty,
                    subtitle = normalizedFallback.Subtitle ?? string.Empty,
                    description = normalizedFallback.Description ?? string.Empty,
                    priceMin = normalizedFallback.PriceMin ?? string.Empty,
                    priceMax = normalizedFallback.PriceMax ?? string.Empty,
                    rating = normalizedFallback.Rating,
                    openTime = normalizedFallback.OpenTime ?? string.Empty,
                    closeTime = normalizedFallback.CloseTime ?? string.Empty,
                    phoneNumber = normalizedFallback.PhoneNumber ?? string.Empty,
                    address = normalizedFallback.Address ?? string.Empty,
                    fallback = true
                });
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var normalized = BuildNormalizedContentViewModel(
                    targetLang,
                    ReadString(root, "title") ?? vi.Title,
                    ReadString(root, "subtitle") ?? vi.Subtitle,
                    ReadString(root, "description") ?? vi.Description,
                    ReadString(root, "priceMin") ?? vi.PriceMin,
                    ReadString(root, "priceMax") ?? vi.PriceMax,
                    ReadDouble(root, "rating") ?? vi.Rating,
                    ReadString(root, "openTime") ?? vi.OpenTime,
                    ReadString(root, "closeTime") ?? vi.CloseTime,
                    ReadString(root, "phoneNumber") ?? vi.PhoneNumber,
                    ReadString(root, "address") ?? vi.Address);

                return Ok(new
                {
                    languageCode = normalized.LanguageCode,
                    title = normalized.Title ?? string.Empty,
                    subtitle = normalized.Subtitle ?? string.Empty,
                    description = normalized.Description ?? string.Empty,
                    priceMin = normalized.PriceMin ?? string.Empty,
                    priceMax = normalized.PriceMax ?? string.Empty,
                    rating = normalized.Rating,
                    openTime = normalized.OpenTime ?? string.Empty,
                    closeTime = normalized.CloseTime ?? string.Empty,
                    phoneNumber = normalized.PhoneNumber ?? string.Empty,
                    address = normalized.Address ?? string.Empty,
                    fallback = root.TryGetProperty("fallback", out var fallbackProp) && fallbackProp.ValueKind == JsonValueKind.True
                });
            }
            catch
            {
                return Content(body, "application/json");
            }
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

            var existing = contents.FirstOrDefault(x => string.Equals(x.LanguageCode, targetLang, StringComparison.OrdinalIgnoreCase));
            var result = await TranslateAndUpsertLanguageAsync(client, poiId, targetLang, vi, existing);
            if (!result.ok)
            {
                return StatusCode(result.statusCode, new { ok = false, error = result.error, detail = result.detail });
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

        private static ContentModel BuildNormalizedContentViewModel(
            string languageCode,
            string? title,
            string? subtitle,
            string? description,
            string? priceMin,
            string? priceMax,
            double rating,
            string? openTime,
            string? closeTime,
            string? phoneNumber,
            string? address)
        {
            var model = new ContentModel
            {
                LanguageCode = languageCode,
                Title = title,
                Subtitle = subtitle,
                Description = description,
                PriceMin = priceMin,
                PriceMax = priceMax,
                Rating = rating,
                OpenTime = openTime,
                CloseTime = closeTime,
                PhoneNumber = phoneNumber,
                Address = address,
                IsTTS = false
            };
            model.NormalizeCompositeFields();
            return model;
        }

        private async Task<List<ContentModel>> EnsureAllLanguageTranslationsAsync(HttpClient client, int poiId, List<ContentModel> contents)
        {
            var current = contents ?? new List<ContentModel>();
            var vi = current.FirstOrDefault(x => string.Equals(x.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
            if (vi == null) return current;

            foreach (var lang in SupportedTranslationLanguages)
            {
                var targetLang = lang.Trim().ToLowerInvariant();
                if (targetLang == "vi") continue;

                var existing = current.FirstOrDefault(x => string.Equals(x.LanguageCode, targetLang, StringComparison.OrdinalIgnoreCase));
                if (!NeedsTranslationUpdate(existing, vi))
                {
                    continue;
                }

                var result = await TranslateAndUpsertLanguageAsync(client, poiId, targetLang, vi, existing);
                if (!result.ok || result.model == null) continue;

                if (existing == null)
                {
                    current.Add(result.model);
                }
                else
                {
                    existing.Title = result.model.Title;
                    existing.Subtitle = result.model.Subtitle;
                    existing.Description = result.model.Description;
                    existing.PriceRange = result.model.PriceRange;
                    existing.Rating = result.model.Rating;
                    existing.OpeningHours = result.model.OpeningHours;
                    existing.PhoneNumber = result.model.PhoneNumber;
                    existing.Address = result.model.Address;
                }
            }

            return current;
        }

        private static bool NeedsTranslationUpdate(ContentModel? existing, ContentModel vi)
        {
            if (existing == null) return true;

            var noMainData = string.IsNullOrWhiteSpace(existing.Title)
                             || string.IsNullOrWhiteSpace(existing.Subtitle)
                             || string.IsNullOrWhiteSpace(existing.Description)
                             || string.IsNullOrWhiteSpace(existing.Address);
            if (noMainData) return true;

            var unchanged = IsSameText(existing.Title, vi.Title)
                            && IsSameText(existing.Subtitle, vi.Subtitle)
                            && IsSameText(existing.Description, vi.Description)
                            && IsSameText(existing.Address, vi.Address);

            if (unchanged) return true;

            return ContainsVietnameseDiacritics(existing.Title)
                   || ContainsVietnameseDiacritics(existing.Subtitle)
                   || ContainsVietnameseDiacritics(existing.Description)
                   || ContainsVietnameseDiacritics(existing.Address);
        }

        private static bool IsSameText(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right)) return true;
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsVietnameseDiacritics(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            const string chars = "ăâđêôơưáàảãạắằẳẵặấầẩẫậéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ";
            return value.Any(c => chars.Contains(char.ToLowerInvariant(c)));
        }

        private async Task<(bool ok, int statusCode, string? error, string? detail, ContentModel? model)> TranslateAndUpsertLanguageAsync(
            HttpClient client,
            int poiId,
            string targetLang,
            ContentModel vi,
            ContentModel? existing)
        {
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
            {
                return (false, (int)transRes.StatusCode, "auto_translate_failed", body, null);
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var model = BuildNormalizedContentViewModel(
                targetLang,
                ReadString(root, "title") ?? vi.Title,
                ReadString(root, "subtitle") ?? vi.Subtitle,
                ReadString(root, "description") ?? vi.Description,
                ReadString(root, "priceMin") ?? vi.PriceMin,
                ReadString(root, "priceMax") ?? vi.PriceMax,
                ReadDouble(root, "rating") ?? vi.Rating,
                ReadString(root, "openTime") ?? vi.OpenTime,
                ReadString(root, "closeTime") ?? vi.CloseTime,
                ReadString(root, "phoneNumber") ?? vi.PhoneNumber,
                ReadString(root, "address") ?? vi.Address);

            model.PoiId = poiId;

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
                return (false, (int)saveRes.StatusCode, "save_failed", saveBody, null);
            }

            return (true, 200, null, null, model);
        }
    }
}
