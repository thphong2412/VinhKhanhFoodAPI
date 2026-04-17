using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class PoiDetailsModel : PageModel
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<PoiDetailsModel> _logger;

        public PoiModel Poi { get; set; }
        [BindProperty]
        public string RawDescription { get; set; } = string.Empty;
        public string EnhancedDescription { get; set; } = string.Empty;
        public string AiError { get; set; } = string.Empty;
        public string AiInfo { get; set; } = string.Empty;
        [BindProperty]
        public bool ApplyToVietnameseContent { get; set; } = true;

        public PoiDetailsModel(IHttpClientFactory factory, ILogger<PoiDetailsModel> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v)) 
                return RedirectToPage("Login");

            if (!int.TryParse(v, out var uid)) 
                return RedirectToPage("Login");

            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-Owner-Id");
                client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
                var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{id}");

                if (poi == null || poi.OwnerId != uid)
                {
                    return NotFound();
                }

                Poi = poi;
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading POI details");
                return NotFound();
            }
        }

        public async Task<IActionResult> OnPostEnhanceAsync(int id)
        {
            var loadResult = await OnGetAsync(id);
            if (loadResult is NotFoundResult || loadResult is RedirectToPageResult)
                return loadResult;

            try
            {
                var client = _factory.CreateClient("api");
                if (Request.Cookies.TryGetValue("owner_userid", out var ownerValue)
                    && int.TryParse(ownerValue, out var ownerId))
                {
                    client.DefaultRequestHeaders.Remove("X-Owner-Id");
                    client.DefaultRequestHeaders.Add("X-Owner-Id", ownerId.ToString());
                }

                if (string.IsNullOrWhiteSpace(RawDescription))
                {
                    AiError = "Vui lòng nhập mô tả thô trước khi dùng AI.";
                    return Page();
                }

                var req = new
                {
                    Name = Poi.Name,
                    Category = Poi.Category,
                    Address = string.Empty,
                    RawDescription = RawDescription ?? string.Empty
                };

                var res = await client.PostAsJsonAsync("api/ai/enhance-description", req);
                var body = await res.Content.ReadAsStringAsync();

                if (!res.IsSuccessStatusCode)
                {
                    AiError = "Không thể gọi AI lúc này. Vui lòng kiểm tra cấu hình Gemini API key.";
                    return Page();
                }

                using var doc = System.Text.Json.JsonDocument.Parse(body);
                EnhancedDescription = doc.RootElement.GetProperty("enhancedDescription").GetString() ?? string.Empty;

                if (ApplyToVietnameseContent && !string.IsNullOrWhiteSpace(EnhancedDescription))
                {
                    var list = await client.GetFromJsonAsync<List<ContentModel>>($"api/content/by-poi/{id}") ?? new List<ContentModel>();
                    var vi = list.FirstOrDefault(x => (x.LanguageCode ?? "").Equals("vi", StringComparison.OrdinalIgnoreCase));

                    if (vi != null)
                    {
                        vi.Description = EnhancedDescription;
                        var updateRes = await client.PutAsJsonAsync($"api/content/{vi.Id}", vi);
                        AiInfo = updateRes.IsSuccessStatusCode
                            ? "Đã áp dụng mô tả AI vào nội dung tiếng Việt của POI."
                            : "Đã tạo mô tả AI nhưng chưa cập nhật được vào nội dung POI.";
                    }
                    else
                    {
                        var create = new ContentModel
                        {
                            PoiId = Poi.Id,
                            LanguageCode = "vi",
                            Title = Poi.Name,
                            Description = EnhancedDescription,
                            Address = string.Empty,
                            IsTTS = false
                        };
                        var createRes = await client.PostAsJsonAsync("api/content", create);
                        AiInfo = createRes.IsSuccessStatusCode
                            ? "Đã tạo mới nội dung tiếng Việt với mô tả AI."
                            : "Đã tạo mô tả AI nhưng chưa tạo được content tiếng Việt.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI enhance failed for owner poi details");
                AiError = "Lỗi khi xử lý AI. Vui lòng thử lại.";
            }

            return Page();
        }

        public async Task<IActionResult> OnPostRequestDeleteAsync(int id)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            try
            {
                var client = _factory.CreateClient("api");
                client.DefaultRequestHeaders.Remove("X-Owner-Id");
                client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
                var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{id}");
                if (poi == null || poi.OwnerId != uid)
                    return NotFound();

                var payload = new
                {
                    OwnerId = uid,
                    Name = poi.Name,
                    Category = poi.Category,
                    RequestType = "delete",
                    TargetPoiId = id,
                    Status = "pending",
                    ReviewNotes = "Owner yêu cầu xóa POI"
                };

                var res = await client.PostAsJsonAsync($"api/poiregistration/submit-delete/{id}", payload);
                if (!res.IsSuccessStatusCode)
                {
                    TempData["ErrorMessage"] = "Gửi yêu cầu xóa thất bại.";
                }
                else
                {
                    TempData["SuccessMessage"] = "Đã gửi yêu cầu xóa POI, chờ admin duyệt.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting delete request from owner portal");
                TempData["ErrorMessage"] = "Lỗi khi gửi yêu cầu xóa.";
            }

            return RedirectToPage("MyPois");
        }
    }
}
