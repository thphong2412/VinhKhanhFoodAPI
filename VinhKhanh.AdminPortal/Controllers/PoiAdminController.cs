using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.AdminPortal.Models;
using VinhKhanh.Shared;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class PoiAdminController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly Microsoft.Extensions.Logging.ILogger<PoiAdminController> _logger;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

        public PoiAdminController(IHttpClientFactory factory, Microsoft.Extensions.Logging.ILogger<PoiAdminController> logger, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _factory = factory;
            _logger = logger;
            _config = config;
        }

        [HttpPost]
        public async Task<IActionResult> ApprovePoi(int id)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            await client.PostAsync($"admin/pois/{id}/approve", null);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegenerateQrAll()
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            var res = await client.PostAsync("admin/pois/regen-qr-all", null);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                TempData["Error"] = "Regen QR thất bại: " + res.StatusCode + (string.IsNullOrWhiteSpace(body) ? string.Empty : $" - {body}");
            }
            else
            {
                TempData["Success"] = "Đã cập nhật lại QR cho toàn bộ POI theo base URL hiện tại.";
            }

            return RedirectToAction("Index");
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

        // Simple diagnostics endpoint to test connectivity to backend API
        public async Task<IActionResult> TestApi()
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            try
            {
                var pois = await client.GetFromJsonAsync<List<PoiModel>>("api/poi");
                var count = pois?.Count ?? 0;
                return Content($"API reachable. POI count={count}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TestApi: failed to call backend API");
                return Content($"API call failed: {ex.Message}");
            }
        }

        public async Task<IActionResult> Index()
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            try
            {
                var pois = await client.GetFromJsonAsync<List<AdminPoiOverviewDto>>("admin/pois/overview");
                var list = pois ?? new List<AdminPoiOverviewDto>();

                var publishedFilter = Request.Query["published"].FirstOrDefault();
                if (bool.TryParse(publishedFilter, out var published))
                {
                    list = list.Where(x => x.IsPublished == published).ToList();
                }

                var ownerFilter = Request.Query["owner"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(ownerFilter))
                {
                    var keyword = ownerFilter.Trim();
                    list = list.Where(x =>
                            (!string.IsNullOrWhiteSpace(x.OwnerName) && x.OwnerName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrWhiteSpace(x.OwnerEmail) && x.OwnerEmail.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                }

                var categoryFilter = Request.Query["category"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(categoryFilter))
                {
                    var keyword = categoryFilter.Trim();
                    list = list.Where(x => !string.IsNullOrWhiteSpace(x.Category) && x.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                var hasContentFilter = Request.Query["hasContent"].FirstOrDefault();
                if (bool.TryParse(hasContentFilter, out var hasContent))
                {
                    list = list.Where(x => x.HasAnyContent == hasContent).ToList();
                }

                var poiIdFilter = Request.Query["poiId"].FirstOrDefault();
                if (int.TryParse(poiIdFilter, out var poiId))
                {
                    list = list.Where(x => x.Id == poiId).ToList();
                }

                return View(list);
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                // API not available or connection refused — show friendly message and empty list
                TempData["Error"] = "Không thể kết nối tới API backend. Vui lòng khởi động VinhKhanh.API trước khi đăng nhập.";
                return View(new List<AdminPoiOverviewDto>());
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi khi tải dữ liệu: " + ex.Message;
                return View(new List<AdminPoiOverviewDto>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePublish(int id, bool publish)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            var endpoint = publish ? $"admin/pois/{id}/publish" : $"admin/pois/{id}/unpublish";
            await client.PostAsync(endpoint, null);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkAction(string actionType, List<int> selectedPoiIds)
        {
            var ids = selectedPoiIds?.Distinct().ToList() ?? new List<int>();
            if (!ids.Any())
            {
                TempData["Error"] = "Bạn chưa chọn POI nào.";
                return RedirectToAction("Index");
            }

            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            var payload = new { poiIds = ids };
            HttpResponseMessage res;
            switch ((actionType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "publish":
                    res = await client.PostAsJsonAsync("admin/pois/bulk/publish", payload);
                    break;
                case "unpublish":
                    res = await client.PostAsJsonAsync("admin/pois/bulk/unpublish", payload);
                    break;
                case "delete":
                    res = await client.PostAsJsonAsync("admin/pois/bulk/delete", payload);
                    break;
                default:
                    TempData["Error"] = "Hành động bulk không hợp lệ.";
                    return RedirectToAction("Index");
            }

            if (!res.IsSuccessStatusCode)
            {
                TempData["Error"] = $"Bulk action thất bại: {res.StatusCode}";
            }
            else
            {
                TempData["Success"] = "Thực hiện bulk action thành công.";
            }

            return RedirectToAction("Index");
        }

        // Redirect to new AdminRegistrations controller
        public IActionResult Registrations()
        {
            return RedirectToAction("Pending", "AdminRegistrations");
        }

        public IActionResult Create() => View();

        // Owner-facing create page — prefill ownerId if cookie present
        public IActionResult OwnerCreate()
        {
            if (HttpContext.Request.Cookies.TryGetValue("owner_userid", out var v) && int.TryParse(v, out var uid))
            {
                ViewData["OwnerId"] = uid;
            }
            return View("Create");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PoiModel model)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("ModelState invalid");
                return View(model);
            }
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            try
            {
                // 1. Tạo POI
                var res = await client.PostAsJsonAsync("api/poi", model);
                if (!res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync();
                    _logger.LogWarning("API returned non-success: {Status} {Body}", res.StatusCode, body);
                    TempData["Error"] = "Tạo POI thất bại: " + res.StatusCode;
                    return View(model);
                }

                // Lấy POI vừa tạo từ response
                var createdPoiJson = await res.Content.ReadAsStringAsync();
                var createdPoi = System.Text.Json.JsonSerializer.Deserialize<PoiModel>(createdPoiJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // ✅ 2. Upload hình ảnh nếu có
                var uploadedImageUrls = new List<string>();
                if (Request.Form.Files.Count > 0)
                {
                    foreach (var file in Request.Form.Files)
                    {
                        if (file.ContentType.StartsWith("image/"))
                        {
                            using (var stream = file.OpenReadStream())
                            {
                                var content = new MultipartFormDataContent();
                                content.Add(new StreamContent(stream), "file", file.FileName);
                                content.Add(new StringContent(createdPoi.Id.ToString()), "poiId");

                                var uploadRes = await client.PostAsync("api/poi/upload-image", content);
                                _logger.LogInformation("Image upload status: {Status}", uploadRes.StatusCode);

                                if (uploadRes.IsSuccessStatusCode)
                                {
                                    var uploadJson = await uploadRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                                    if (uploadJson.TryGetProperty("url", out var urlProp))
                                    {
                                        var url = urlProp.GetString();
                                        if (!string.IsNullOrWhiteSpace(url)) uploadedImageUrls.Add(url);
                                    }
                                }
                            }
                        }
                    }
                }

                if (uploadedImageUrls.Count > 0)
                {
                    createdPoi.ImageUrl = string.Join(";", uploadedImageUrls.Distinct(StringComparer.OrdinalIgnoreCase));
                    await client.PutAsJsonAsync($"api/poi/{createdPoi.Id}", createdPoi);
                }

                // ✅ 3. Upload audio files nếu có
                var audioFile_VI = Request.Form.Files.FirstOrDefault(f => f.Name == "AudioFile_VI");
                var audioFile_EN = Request.Form.Files.FirstOrDefault(f => f.Name == "AudioFile_EN");

                if (audioFile_VI != null && audioFile_VI.ContentType == "audio/mpeg")
                {
                    await UploadAudioAsync(client, createdPoi.Id, audioFile_VI, "vi");
                }

                if (audioFile_EN != null && audioFile_EN.ContentType == "audio/mpeg")
                {
                    await UploadAudioAsync(client, createdPoi.Id, audioFile_EN, "en");
                }

                // ✅ 4. Tạo chi tiết POI (Content) - Tiếng Việt
                if (!string.IsNullOrEmpty(Request.Form["ContentTitle_VI"]))
                {
                    var content = new VinhKhanh.Shared.ContentModel
                    {
                        PoiId = createdPoi.Id,
                        LanguageCode = "vi",
                        Title = Request.Form["ContentTitle_VI"],
                        Subtitle = Request.Form["ContentSubtitle_VI"],
                        Description = Request.Form["ContentDescription_VI"],
                        PriceMin = Request.Form["ContentPriceMin_VI"],
                        PriceMax = Request.Form["ContentPriceMax_VI"],
                        Rating = double.TryParse(Request.Form["ContentRating_VI"], out var r) ? r : 0,
                        OpenTime = Request.Form["ContentOpenTime_VI"],
                        CloseTime = Request.Form["ContentCloseTime_VI"],
                        PhoneNumber = Request.Form["ContentPhoneNumber_VI"],
                        Address = Request.Form["ContentAddress_VI"],
                        AudioUrl = audioFile_VI != null ? $"/api/audio/{createdPoi.Id}/vi" : "",
                        IsTTS = false,
                        ShareUrl = ""
                    };

                    content.NormalizeCompositeFields();

                    await client.PostAsJsonAsync("api/content", content);
                }

                // ✅ 5. Tạo chi tiết POI (Content) - Tiếng Anh (nếu có)
                if (!string.IsNullOrEmpty(Request.Form["ContentTitle_EN"]))
                {
                    var content = new VinhKhanh.Shared.ContentModel
                    {
                        PoiId = createdPoi.Id,
                        LanguageCode = "en",
                        Title = Request.Form["ContentTitle_EN"],
                        Subtitle = Request.Form["ContentSubtitle_EN"],
                        Description = Request.Form["ContentDescription_EN"],
                        PriceMin = Request.Form["ContentPriceMin_EN"],
                        PriceMax = Request.Form["ContentPriceMax_EN"],
                        Rating = double.TryParse(Request.Form["ContentRating_EN"], out var r) ? r : 0,
                        OpenTime = Request.Form["ContentOpenTime_EN"],
                        CloseTime = Request.Form["ContentCloseTime_EN"],
                        PhoneNumber = Request.Form["ContentPhoneNumber_EN"],
                        Address = Request.Form["ContentAddress_EN"],
                        AudioUrl = audioFile_EN != null ? $"/api/audio/{createdPoi.Id}/en" : "",
                        IsTTS = false,
                        ShareUrl = ""
                    };

                    content.NormalizeCompositeFields();

                    await client.PostAsJsonAsync("api/content", content);
                }

                TempData["Success"] = $"✅ Tạo POI thành công! ID: {createdPoi.Id}";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating POI");
                TempData["Error"] = $"❌ Lỗi: {ex.Message}";
                return View(model);
            }
        }

        // ✅ Simplified version - tạo POI + Content mà không cần audio
        private async Task<bool> CreatePoiWithContentAsync(HttpClient client, PoiModel model)
        {
            try
            {
                // 1. Tạo POI
                var res = await client.PostAsJsonAsync("api/poi", model);
                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to create POI: {Status}", res.StatusCode);
                    return false;
                }

                var responseJson = await res.Content.ReadAsStringAsync();
                var createdPoi = System.Text.Json.JsonSerializer.Deserialize<PoiModel>(
                    responseJson, 
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (createdPoi?.Id == 0) return false;

                // 2. Tạo Content - VI
                if (!string.IsNullOrEmpty(Request.Form["ContentTitle_VI"]))
                {
                    var content = new VinhKhanh.Shared.ContentModel
                    {
                        PoiId = createdPoi.Id,
                        LanguageCode = "vi",
                        Title = Request.Form["ContentTitle_VI"],
                        Subtitle = Request.Form["ContentSubtitle_VI"],
                        Description = Request.Form["ContentDescription_VI"],
                        PriceRange = Request.Form["ContentPriceRange_VI"],
                        Rating = double.TryParse(Request.Form["ContentRating_VI"], out var r) ? r : 0,
                        OpeningHours = Request.Form["ContentOpeningHours_VI"],
                        PhoneNumber = Request.Form["ContentPhoneNumber_VI"],
                        Address = Request.Form["ContentAddress_VI"],
                        AudioUrl = "",
                        IsTTS = false,
                        ShareUrl = ""
                    };
                    await client.PostAsJsonAsync("api/content", content);
                }

                // 3. Tạo Content - EN (optional)
                if (!string.IsNullOrEmpty(Request.Form["ContentTitle_EN"]))
                {
                    var content = new VinhKhanh.Shared.ContentModel
                    {
                        PoiId = createdPoi.Id,
                        LanguageCode = "en",
                        Title = Request.Form["ContentTitle_EN"],
                        Subtitle = Request.Form["ContentSubtitle_EN"],
                        Description = Request.Form["ContentDescription_EN"],
                        PriceRange = Request.Form["ContentPriceRange_EN"],
                        Rating = double.TryParse(Request.Form["ContentRating_EN"], out var r) ? r : 0,
                        OpeningHours = Request.Form["ContentOpeningHours_EN"],
                        PhoneNumber = Request.Form["ContentPhoneNumber_EN"],
                        Address = Request.Form["ContentAddress_EN"],
                        AudioUrl = "",
                        IsTTS = false,
                        ShareUrl = ""
                    };
                    await client.PostAsJsonAsync("api/content", content);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreatePoiWithContentAsync");
                return false;
            }
        }

        private async Task UploadAudioAsync(HttpClient client, int poiId, Microsoft.AspNetCore.Http.IFormFile audioFile, string languageCode)
        {
            try
            {
                using (var stream = audioFile.OpenReadStream())
                {
                    var content = new MultipartFormDataContent();
                    content.Add(new StreamContent(stream), "file", audioFile.FileName);
                    content.Add(new StringContent(poiId.ToString()), "poiId");
                    content.Add(new StringContent(languageCode), "languageCode");

                    var res = await client.PostAsync("api/audio/upload", content);
                    _logger.LogInformation("Audio upload ({Lang}) status: {Status}", languageCode, res.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading audio for POI {PoiId} ({Lang})", poiId, languageCode);
            }
        }

        public async Task<IActionResult> Edit(int id)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{id}");
            if (poi == null) return NotFound();

            var viContent = poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
            ViewBag.ViContent = viContent ?? new ContentModel { PoiId = poi.Id, LanguageCode = "vi", Title = poi.Name };
            ViewBag.ApiBaseUrl = client.BaseAddress?.ToString().TrimEnd('/');
            return View(poi);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(PoiModel model)
        {
            var client = _factory.CreateClient("api");

            if (!ModelState.IsValid)
            {
                ViewBag.ViContent = new ContentModel { PoiId = model.Id, LanguageCode = "vi", Title = model.Name };
                ViewBag.ApiBaseUrl = client.BaseAddress?.ToString().TrimEnd('/');
                return View(model);
            }

            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

            var currentPoi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{model.Id}");
            if (currentPoi == null)
            {
                TempData["Error"] = "Không tìm thấy POI để cập nhật.";
                return RedirectToAction("Index");
            }

            model.ImageUrl = currentPoi.ImageUrl;
            var res = await client.PutAsJsonAsync($"api/poi/{model.Id}", model);
            if (!res.IsSuccessStatusCode)
            {
                TempData["Error"] = "Cập nhật POI thất bại.";
                ViewBag.ViContent = new ContentModel { PoiId = model.Id, LanguageCode = "vi", Title = model.Name };
                ViewBag.ApiBaseUrl = client.BaseAddress?.ToString().TrimEnd('/');
                return View(model);
            }

            var uploadedImageUrls = new List<string>();
            var imageUploadFailed = false;
            if (Request.Form.Files.Count > 0)
            {
                var imageFiles = Request.Form.Files.Where(IsImageFile).ToList();
                foreach (var file in imageFiles)
                {
                    await using var stream = file.OpenReadStream();
                    using var content = new MultipartFormDataContent();
                    var streamContent = new StreamContent(stream);
                    var mediaType = ResolveImageContentType(file.FileName, file.ContentType);
                    if (!string.IsNullOrWhiteSpace(mediaType))
                    {
                        streamContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
                    }

                    content.Add(streamContent, "file", file.FileName);
                    content.Add(new StringContent(model.Id.ToString()), "poiId");

                    var uploadRes = await client.PostAsync("api/poi/upload-image", content);
                    if (!uploadRes.IsSuccessStatusCode)
                    {
                        imageUploadFailed = true;
                        var uploadBody = await uploadRes.Content.ReadAsStringAsync();
                        _logger.LogWarning("Upload image failed for POI {PoiId}: {Status} {Body}", model.Id, uploadRes.StatusCode, uploadBody);
                        continue;
                    }

                    var body = await uploadRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                    if (body.TryGetProperty("url", out var urlProp))
                    {
                        var url = urlProp.GetString();
                        if (!string.IsNullOrWhiteSpace(url)) uploadedImageUrls.Add(url);
                    }
                }

                if (imageFiles.Count > 0 && uploadedImageUrls.Count == 0)
                {
                    TempData["Error"] = "Bạn đã chọn ảnh mới nhưng upload thất bại. Vui lòng thử ảnh JPG/PNG khác.";
                }
            }

            var existingImages = (currentPoi.ImageUrl ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var deleteImages = Request.Form["DeleteImageUrls"].ToList();
            if (deleteImages.Any())
            {
                existingImages = existingImages
                    .Where(x => !deleteImages.Contains(x, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }

            var finalImages = existingImages
                .Concat(uploadedImageUrls)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            model.ImageUrl = finalImages.Any()
                ? string.Join(";", finalImages)
                : null;

            var imageSaveRes = await client.PutAsJsonAsync($"api/poi/{model.Id}", model);
            if (!imageSaveRes.IsSuccessStatusCode)
            {
                TempData["Error"] = "Lưu ảnh mới thất bại, vui lòng thử lại.";
                ViewBag.ViContent = new ContentModel { PoiId = model.Id, LanguageCode = "vi", Title = model.Name };
                ViewBag.ApiBaseUrl = client.BaseAddress?.ToString().TrimEnd('/');
                return View(model);
            }

            await UpsertPoiContentAsync(client, model.Id, "vi");

            TempData["Success"] = imageUploadFailed
                ? "Đã cập nhật POI, nhưng có một số ảnh upload lỗi."
                : "Cập nhật POI thành công.";
            return RedirectToAction("Index");
        }

        private static bool IsImageFile(Microsoft.AspNetCore.Http.IFormFile file)
        {
            if (file == null || file.Length <= 0) return false;

            if (!string.IsNullOrWhiteSpace(file.ContentType)
                && file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext)) return false;

            return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".heic", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".heif", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveImageContentType(string? fileName, string? originalContentType)
        {
            if (!string.IsNullOrWhiteSpace(originalContentType)
                && originalContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return originalContentType;
            }

            var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                ".heic" => "image/heic",
                ".heif" => "image/heif",
                _ => "application/octet-stream"
            };
        }

        private async Task UpsertPoiContentAsync(HttpClient client, int poiId, string languageCode)
        {
            var suffix = (languageCode ?? "vi").Trim().ToUpperInvariant();
            var title = Request.Form[$"ContentTitle_{suffix}"].ToString();
            var subtitle = Request.Form[$"ContentSubtitle_{suffix}"].ToString();
            var description = Request.Form[$"ContentDescription_{suffix}"].ToString();
            var priceMin = Request.Form[$"ContentPriceMin_{suffix}"].ToString();
            var priceMax = Request.Form[$"ContentPriceMax_{suffix}"].ToString();
            var ratingRaw = Request.Form[$"ContentRating_{suffix}"].ToString();
            var openTime = Request.Form[$"ContentOpenTime_{suffix}"].ToString();
            var closeTime = Request.Form[$"ContentCloseTime_{suffix}"].ToString();
            var phone = Request.Form[$"ContentPhoneNumber_{suffix}"].ToString();
            var address = Request.Form[$"ContentAddress_{suffix}"].ToString();

            if (string.IsNullOrWhiteSpace(title)
                && string.IsNullOrWhiteSpace(subtitle)
                && string.IsNullOrWhiteSpace(description)
                && string.IsNullOrWhiteSpace(priceMin)
                && string.IsNullOrWhiteSpace(priceMax)
                && string.IsNullOrWhiteSpace(openTime)
                && string.IsNullOrWhiteSpace(closeTime)
                && string.IsNullOrWhiteSpace(phone)
                && string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            var list = await client.GetFromJsonAsync<List<ContentModel>>($"api/content/by-poi/{poiId}") ?? new List<ContentModel>();
            var existing = list.FirstOrDefault(x => string.Equals(x.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase));

            var content = existing ?? new ContentModel
            {
                PoiId = poiId,
                LanguageCode = languageCode,
                IsTTS = false
            };

            content.Title = title;
            content.Subtitle = subtitle;
            content.Description = description;
            content.PriceMin = priceMin;
            content.PriceMax = priceMax;
            content.Rating = double.TryParse(ratingRaw, out var rating) ? rating : 0;
            content.OpenTime = openTime;
            content.CloseTime = closeTime;
            content.PhoneNumber = phone;
            content.Address = address;
            content.NormalizeCompositeFields();

            if (existing == null)
            {
                await client.PostAsJsonAsync("api/content", content);
            }
            else
            {
                await client.PutAsJsonAsync($"api/content/{content.Id}", content);
            }
        }

        public async Task<IActionResult> Details(int id)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            try
            {
                var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{id}");
                if (poi == null) return NotFound();

                var ownerInfo = await client.GetFromJsonAsync<List<UserDto>>("admin/users");
                var owner = ownerInfo?.FirstOrDefault(u => u.Id == poi.OwnerId);
                ViewBag.OwnerEmail = owner?.Email;

                // Lấy thông tin tên quán/owner name + thời điểm duyệt từ endpoint overview
                var overview = await client.GetFromJsonAsync<List<AdminPoiOverviewDto>>("admin/pois/overview");
                var overviewItem = overview?.FirstOrDefault(x => x.Id == poi.Id);
                ViewBag.OwnerName = overviewItem?.OwnerName;
                ViewBag.ApprovedAtUtc = overviewItem?.ApprovedAtUtc;
                ViewBag.ApiBaseUrl = client.BaseAddress?.ToString().TrimEnd('/');

                return View(poi);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching POI details");
                TempData["Error"] = "Lỗi khi tải chi tiết POI: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());
            var res = await client.DeleteAsync($"api/poi/{id}");
            if (!res.IsSuccessStatusCode)
            {
                TempData["Error"] = "Xóa POI thất bại.";
            }
            else
            {
                TempData["Success"] = "Đã xóa POI.";
            }
            return RedirectToAction("Index");
        }
    }
}
