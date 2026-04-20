using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Json;
using System.Text.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class OwnerTranslationsModel : PageModel
    {
        private readonly IHttpClientFactory _factory;
        private readonly IConfiguration _config;

        public int PoiId { get; set; }
        public List<ContentModel> Contents { get; set; } = new();
        public string ViSourceJson { get; set; } = "{}";

        public OwnerTranslationsModel(IHttpClientFactory factory, IConfiguration config)
        {
            _factory = factory;
            _config = config;
        }

        private void ConfigureApiClient(HttpClient client, int ownerId)
        {
            client.DefaultRequestHeaders.Remove("X-Owner-Id");
            client.DefaultRequestHeaders.Add("X-Owner-Id", ownerId.ToString());
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", (_config["ApiKey"] ?? "admin123").Trim());
        }

        public async Task<IActionResult> OnGetAsync(int poiId)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            PoiId = poiId;
            var client = _factory.CreateClient("api");
            ConfigureApiClient(client, uid);
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            Contents = await client.GetFromJsonAsync<List<ContentModel>>($"api/content/by-poi/{poiId}") ?? new();
            var vi = Contents.FirstOrDefault(x => string.Equals(x.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
            ViSourceJson = JsonSerializer.Serialize(new
            {
                title = vi?.Title,
                subtitle = vi?.Subtitle,
                description = vi?.Description,
                priceMin = vi?.PriceMin,
                priceMax = vi?.PriceMax,
                rating = vi?.Rating,
                openTime = vi?.OpenTime,
                closeTime = vi?.CloseTime,
                phoneNumber = vi?.PhoneNumber,
                address = vi?.Address
            });
            return Page();
        }

        public async Task<IActionResult> OnGetAutoTranslateAsync(int poiId, string languageCode)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return Unauthorized();

            var client = _factory.CreateClient("api");
            ConfigureApiClient(client, uid);
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            var normalizedLang = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant();

            var contents = await client.GetFromJsonAsync<List<ContentModel>>($"api/content/by-poi/{poiId}") ?? new List<ContentModel>();
            var vi = contents.FirstOrDefault(x => string.Equals(x.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
            if (vi == null)
            {
                return NotFound(new { error = "missing_vi_source" });
            }

            var payload = new
            {
                targetLanguageCode = normalizedLang,
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

            var response = await client.PostAsJsonAsync("api/ai/translate-content", payload);
            if (!response.IsSuccessStatusCode)
            {
                return BadRequest(new { error = "auto_translate_failed" });
            }

            var body = await response.Content.ReadAsStringAsync();
            return Content(body, "application/json");
        }

        public async Task<IActionResult> OnPostSubmitTranslationUpdateAsync(
            int poiId,
            string languageCode,
            string title,
            string? subtitle,
            string? description,
            string? priceMin,
            string? priceMax,
            double? rating,
            string? openTime,
            string? closeTime,
            string? phoneNumber,
            string? address)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            var client = _factory.CreateClient("api");
            ConfigureApiClient(client, uid);
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            var normalizedLang = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant();
            var existing = await client.GetFromJsonAsync<List<ContentModel>>($"api/content/by-poi/{poiId}") ?? new();
            var current = existing.FirstOrDefault(c => string.Equals(c.LanguageCode, normalizedLang, StringComparison.OrdinalIgnoreCase));

            var content = new ContentModel
            {
                Id = current?.Id ?? 0,
                PoiId = poiId,
                LanguageCode = normalizedLang,
                Title = title?.Trim(),
                Subtitle = subtitle?.Trim(),
                Description = description?.Trim(),
                PriceMin = priceMin,
                PriceMax = priceMax,
                Rating = rating ?? current?.Rating ?? 0,
                OpenTime = openTime?.Trim(),
                CloseTime = closeTime?.Trim(),
                PhoneNumber = phoneNumber?.Trim(),
                Address = address?.Trim(),
                AudioUrl = current?.AudioUrl,
                ShareUrl = current?.ShareUrl,
                IsTTS = current?.IsTTS ?? false
            };
            content.NormalizeCompositeFields();

            HttpResponseMessage res;
            if (current == null)
            {
                res = await client.PostAsJsonAsync("api/content", content);
            }
            else
            {
                res = await client.PutAsJsonAsync($"api/content/{current.Id}", content);
            }

            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                TempData["ErrorMessage"] = "Cập nhật bản dịch thất bại."
                    + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" {body}");
            }
            else
            {
                TempData["SuccessMessage"] = current == null
                    ? "Đã tạo mới bản dịch thành công."
                    : "Đã cập nhật bản dịch thành công.";
            }

            return RedirectToPage(new { poiId });
        }

        public async Task<IActionResult> OnPostAutoTranslateAndUpsertAsync(int poiId, string languageCode)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return Unauthorized();

            if (poiId <= 0 || string.IsNullOrWhiteSpace(languageCode))
                return BadRequest(new { ok = false, error = "invalid_request" });

            var client = _factory.CreateClient("api");
            ConfigureApiClient(client, uid);

            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            var normalizedLang = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant();
            if (normalizedLang == "vi")
            {
                return new JsonResult(new { ok = true, message = "Ngôn ngữ vi là dữ liệu gốc, không cần dịch." });
            }

            var contents = await client.GetFromJsonAsync<List<ContentModel>>($"api/content/by-poi/{poiId}") ?? new List<ContentModel>();
            var vi = contents.FirstOrDefault(x => string.Equals(x.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
            if (vi == null)
            {
                return NotFound(new { ok = false, error = "missing_vi_source", message = "Chưa có nội dung tiếng Việt để làm gốc." });
            }

            var payload = new
            {
                targetLanguageCode = normalizedLang,
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
                return StatusCode((int)transRes.StatusCode, new { ok = false, error = "auto_translate_failed", detail = body });
            }

            string? ReadString(JsonElement root, string name)
                => root.TryGetProperty(name, out var p) ? p.GetString() : null;

            double? ReadDouble(JsonElement root, string name)
            {
                if (!root.TryGetProperty(name, out var p)) return null;
                if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var n)) return n;
                if (p.ValueKind == JsonValueKind.String && double.TryParse(p.GetString(), out var parsed)) return parsed;
                return null;
            }

            var translated = vi;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;

                    translated = new ContentModel
                    {
                        PoiId = poiId,
                        LanguageCode = normalizedLang,
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
                }
                catch
                {
                    translated = new ContentModel
                    {
                        PoiId = poiId,
                        LanguageCode = normalizedLang,
                        Title = vi.Title,
                        Subtitle = vi.Subtitle,
                        Description = vi.Description,
                        PriceMin = vi.PriceMin,
                        PriceMax = vi.PriceMax,
                        Rating = vi.Rating,
                        OpenTime = vi.OpenTime,
                        CloseTime = vi.CloseTime,
                        PhoneNumber = vi.PhoneNumber,
                        Address = vi.Address,
                        IsTTS = false
                    };
                }
            }

            translated.NormalizeCompositeFields();

            var existing = contents.FirstOrDefault(c => string.Equals(c.LanguageCode, normalizedLang, StringComparison.OrdinalIgnoreCase));
            HttpResponseMessage saveRes;
            if (existing == null)
            {
                saveRes = await client.PostAsJsonAsync("api/content", translated);
            }
            else
            {
                translated.Id = existing.Id;
                saveRes = await client.PutAsJsonAsync($"api/content/{existing.Id}", translated);
            }

            if (!saveRes.IsSuccessStatusCode)
            {
                var saveBody = await saveRes.Content.ReadAsStringAsync();
                return StatusCode((int)saveRes.StatusCode, new { ok = false, error = "save_failed", detail = saveBody });
            }

            return new JsonResult(new
            {
                ok = true,
                updated = existing != null,
                message = existing == null
                    ? $"Đã dịch và tạo mới bản {normalizedLang.ToUpperInvariant()} thành công."
                    : $"Đã dịch và cập nhật bản {normalizedLang.ToUpperInvariant()} thành công."
            });
        }

        public async Task<IActionResult> OnPostDeleteAsync(int poiId, int id)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            var client = _factory.CreateClient("api");
            ConfigureApiClient(client, uid);
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{poiId}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            var deleteRes = await client.DeleteAsync($"api/content/{id}");
            if (!deleteRes.IsSuccessStatusCode)
            {
                var body = await deleteRes.Content.ReadAsStringAsync();
                TempData["ErrorMessage"] = "Xóa bản dịch thất bại."
                    + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" {body}");
            }
            else
            {
                TempData["SuccessMessage"] = "Đã xóa bản dịch thành công.";
            }

            return RedirectToPage(new { poiId });
        }
    }
}
