using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;
using VinhKhanh.AdminPortal.Models;
using VinhKhanh.Shared;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class AdminPoiRegistrationsController : Controller
    {
        private readonly IHttpClientFactory _factory;
        private readonly IConfiguration _config;
        private readonly ILogger<AdminPoiRegistrationsController> _logger;

        public AdminPoiRegistrationsController(IHttpClientFactory factory, IConfiguration config, ILogger<AdminPoiRegistrationsController> logger)
        {
            _factory = factory;
            _config = config;
            _logger = logger;
        }

        private string GetApiKey()
        {
            var configured = _config?["ApiKey"];
            return !string.IsNullOrEmpty(configured) ? configured : "admin123";
        }

        /// <summary>
        /// View all pending POI registrations
        /// </summary>
        public async Task<IActionResult> Pending()
        {
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

                var response = await client.GetAsync("api/poiregistration/pending");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"API returned {response.StatusCode}: {errorContent}");
                    TempData["Error"] = $"Lỗi khi tải danh sách chờ duyệt: Response status code does not indicate success: {(int)response.StatusCode} ({response.StatusCode}).";
                    return View(new List<PoiRegistrationDto>());
                }

                var registrations = await response.Content.ReadFromJsonAsync<List<PoiRegistrationDto>>();
                var items = registrations ?? new List<PoiRegistrationDto>();
                foreach (var registration in items)
                {
                    registration.ChangeSummary = await BuildChangeSummaryAsync(client, registration);
                }

                return View(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching pending registrations");
                TempData["Error"] = "Lỗi khi tải danh sách chờ duyệt: " + ex.Message;
                return View(new List<PoiRegistrationDto>());
            }
        }

        /// <summary>
        /// View details of a pending POI registration
        /// </summary>
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

                var registration = await client.GetFromJsonAsync<PoiRegistrationDto>($"api/poiregistration/{id}");
                if (registration == null) return NotFound();

                registration.ChangeSummary = await BuildChangeSummaryAsync(client, registration);

                return View(registration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching registration details");
                TempData["Error"] = "Lỗi khi tải chi tiết: " + ex.Message;
                return RedirectToAction("Pending");
            }
        }

        /// <summary>
        /// Admin approves a POI registration
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id)
        {
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

                var notes = Request.Form["Notes"].ToString();
                var request = new { Notes = notes, ReviewedBy = 1 };

                var res = await client.PostAsJsonAsync($"api/poiregistration/{id}/approve", request);
                if (res.IsSuccessStatusCode)
                {
                    TempData["Success"] = "POI đã được duyệt thành công!";
                    return RedirectToAction("Pending");
                }

                TempData["Error"] = "Duyệt POI thất bại";
                return RedirectToAction("Details", new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving POI");
                TempData["Error"] = "Lỗi: " + ex.Message;
                return RedirectToAction("Details", new { id });
            }
        }

        /// <summary>
        /// Admin rejects a POI registration
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id)
        {
            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

                var notes = Request.Form["RejectReason"].ToString();
                var request = new { Notes = notes, ReviewedBy = 1 };

                var res = await client.PostAsJsonAsync($"api/poiregistration/{id}/reject", request);
                if (res.IsSuccessStatusCode)
                {
                    TempData["Success"] = "POI đã bị từ chối.";
                    return RedirectToAction("Pending");
                }

                TempData["Error"] = "Từ chối POI thất bại";
                return RedirectToAction("Details", new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting POI");
                TempData["Error"] = "Lỗi: " + ex.Message;
                return RedirectToAction("Details", new { id });
            }
        }

        private static bool Different(string? source, string? target)
            => !string.Equals((source ?? string.Empty).Trim(), (target ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

        private static string TryGetString(JsonElement root, string property)
        {
            if (!root.TryGetProperty(property, out var node)) return string.Empty;
            return node.ValueKind == JsonValueKind.String ? (node.GetString() ?? string.Empty) : node.ToString();
        }

        private async Task<List<string>> BuildChangeSummaryAsync(HttpClient client, PoiRegistrationDto registration)
        {
            var result = new List<string>();
            var reqType = (registration.RequestType ?? "create").Trim().ToLowerInvariant();

            if (reqType == "create")
            {
                result.Add("Tạo POI mới");
                return result;
            }

            if (reqType == "delete")
            {
                result.Add("Yêu cầu xóa POI");
                return result;
            }

            if (registration.TargetPoiId is null)
            {
                result.Add("Yêu cầu cập nhật (thiếu TargetPoiId)");
                return result;
            }

            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{registration.TargetPoiId.Value}");
            if (poi == null)
            {
                result.Add("Yêu cầu cập nhật (không tải được POI hiện tại)");
                return result;
            }

            if (Different(poi.Name, registration.Name)) result.Add("Tên POI");
            if (Different(poi.Category, registration.Category)) result.Add("Danh mục");
            if (Math.Abs(poi.Latitude - registration.Latitude) > 0.000001) result.Add("Vĩ độ");
            if (Math.Abs(poi.Longitude - registration.Longitude) > 0.000001) result.Add("Kinh độ");
            if (Math.Abs(poi.Radius - registration.Radius) > 0.001) result.Add("Bán kính");
            if (poi.Priority != registration.Priority) result.Add("Độ ưu tiên");
            if (poi.CooldownSeconds != registration.CooldownSeconds) result.Add("Cooldown");
            if (Different(poi.ImageUrl, registration.ImageUrl)) result.Add("Ảnh đại diện");
            if (Different(poi.WebsiteUrl, registration.WebsiteUrl)) result.Add("Website");

            if (!string.IsNullOrWhiteSpace(registration.ContentTitle)
                || !string.IsNullOrWhiteSpace(registration.ContentSubtitle)
                || !string.IsNullOrWhiteSpace(registration.ContentDescription)
                || !string.IsNullOrWhiteSpace(registration.ContentPriceMin)
                || !string.IsNullOrWhiteSpace(registration.ContentPriceMax)
                || registration.ContentRating.HasValue
                || !string.IsNullOrWhiteSpace(registration.ContentOpenTime)
                || !string.IsNullOrWhiteSpace(registration.ContentCloseTime)
                || !string.IsNullOrWhiteSpace(registration.ContentPhoneNumber)
                || !string.IsNullOrWhiteSpace(registration.ContentAddress))
            {
                result.Add("Nội dung tiếng Việt");
            }

            var note = registration.ReviewNotes?.Trim() ?? string.Empty;
            if (note.StartsWith("owner_audio_update::", StringComparison.OrdinalIgnoreCase))
            {
                var parts = note.Split(new[] { "::" }, 4, StringSplitOptions.None);
                var lang = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim().ToLowerInvariant() : "vi";
                var fileName = parts.Length > 2 ? parts[2] : "audio";
                result.Add($"Audio ({lang}) - {fileName}");
            }
            else if (note.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    using var doc = JsonDocument.Parse(note);
                    var root = doc.RootElement;
                    var eventType = TryGetString(root, "eventType");
                    if (string.Equals(eventType, "owner_translation_update", StringComparison.OrdinalIgnoreCase))
                    {
                        var lang = TryGetString(root, "languageCode");
                        var translationFields = new List<string>();

                        if (!string.IsNullOrWhiteSpace(TryGetString(root, "title"))) translationFields.Add("Title");
                        if (!string.IsNullOrWhiteSpace(TryGetString(root, "subtitle"))) translationFields.Add("Subtitle");
                        if (!string.IsNullOrWhiteSpace(TryGetString(root, "description"))) translationFields.Add("Description");
                        if (!string.IsNullOrWhiteSpace(TryGetString(root, "priceMin")) || !string.IsNullOrWhiteSpace(TryGetString(root, "priceMax"))) translationFields.Add("Giá");
                        if (root.TryGetProperty("rating", out _)) translationFields.Add("Rating");
                        if (!string.IsNullOrWhiteSpace(TryGetString(root, "openTime")) || !string.IsNullOrWhiteSpace(TryGetString(root, "closeTime"))) translationFields.Add("Giờ mở cửa");
                        if (!string.IsNullOrWhiteSpace(TryGetString(root, "phoneNumber"))) translationFields.Add("Số điện thoại");
                        if (!string.IsNullOrWhiteSpace(TryGetString(root, "address"))) translationFields.Add("Địa chỉ");

                        var detail = translationFields.Count > 0
                            ? string.Join(", ", translationFields)
                            : "Nội dung bản dịch";

                        result.Add($"Bản dịch ({lang}) - {detail}");
                    }
                }
                catch
                {
                    result.Add("Payload cập nhật đặc biệt");
                }
            }

            if (result.Count == 0)
            {
                result.Add("Yêu cầu cập nhật chung");
            }

            return result;
        }
    }
}
