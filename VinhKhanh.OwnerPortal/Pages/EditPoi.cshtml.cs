using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using VinhKhanh.Shared;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class EditPoiModel : PageModel
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<EditPoiModel> _logger;

        [BindProperty]
        public PoiModel Poi { get; set; } = new();

        [BindProperty] public string? ContentTitle_VI { get; set; }
        [BindProperty] public string? ContentSubtitle_VI { get; set; }
        [BindProperty] public string? ContentDescription_VI { get; set; }
        [BindProperty] public string? ContentPriceMin_VI { get; set; }
        [BindProperty] public string? ContentPriceMax_VI { get; set; }
        [BindProperty] public double? ContentRating_VI { get; set; }
        [BindProperty] public string? ContentOpenTime_VI { get; set; }
        [BindProperty] public string? ContentCloseTime_VI { get; set; }
        [BindProperty] public string? ContentPhoneNumber_VI { get; set; }
        [BindProperty] public string? ContentAddress_VI { get; set; }

        public string ApiBaseUrl { get; set; } = string.Empty;

        public EditPoiModel(IHttpClientFactory factory, ILogger<EditPoiModel> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            var client = _factory.CreateClient("api");
            ApiBaseUrl = client.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;
            client.DefaultRequestHeaders.Remove("X-Owner-Id");
            client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
            var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{id}");
            if (poi == null || poi.OwnerId != uid) return NotFound();

            Poi = poi;
            var vi = poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
            if (vi != null)
            {
                ContentTitle_VI = vi.Title;
                ContentSubtitle_VI = vi.Subtitle;
                ContentDescription_VI = vi.Description;
                ContentPriceMin_VI = vi.PriceMin;
                ContentPriceMax_VI = vi.PriceMax;
                ContentRating_VI = vi.Rating;
                ContentOpenTime_VI = vi.OpenTime;
                ContentCloseTime_VI = vi.CloseTime;
                ContentPhoneNumber_VI = vi.PhoneNumber;
                ContentAddress_VI = vi.Address;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v) || !int.TryParse(v, out var uid))
                return RedirectToPage("Login");

            var client = _factory.CreateClient("api");
            client.DefaultRequestHeaders.Remove("X-Owner-Id");
            client.DefaultRequestHeaders.Add("X-Owner-Id", uid.ToString());
            var existing = await client.GetFromJsonAsync<PoiModel>($"api/poi/{id}");
            if (existing == null || existing.OwnerId != uid) return NotFound();

            Poi.ImageUrl = existing.ImageUrl;

            var uploadedImageUrls = new List<string>();
            if (Request.Form.Files.Count > 0)
            {
                foreach (var image in Request.Form.Files.Where(IsImageFile))
                {
                    await using var stream = image.OpenReadStream();
                    using var uploadContent = new MultipartFormDataContent();
                    var streamContent = new StreamContent(stream);
                    var mediaType = ResolveImageContentType(image.FileName, image.ContentType);
                    if (!string.IsNullOrWhiteSpace(mediaType))
                    {
                        streamContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
                    }

                    uploadContent.Add(streamContent, "file", image.FileName);
                    uploadContent.Add(new StringContent(id.ToString()), "poiId");

                    // Owner edit uses registration upload endpoint (không yêu cầu quyền admin API key)
                    var uploadRes = await client.PostAsync("api/poiregistration/upload-image", uploadContent);
                    if (!uploadRes.IsSuccessStatusCode)
                    {
                        var uploadErrorBody = await uploadRes.Content.ReadAsStringAsync();
                        _logger.LogWarning("Owner upload image failed for POI {PoiId}: {Status} {Body}", id, uploadRes.StatusCode, uploadErrorBody);
                        continue;
                    }

                    var uploadBody = await uploadRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                    if (uploadBody.TryGetProperty("url", out var urlProp))
                    {
                        var url = urlProp.GetString();
                        if (!string.IsNullOrWhiteSpace(url)) uploadedImageUrls.Add(url);
                    }
                }
            }

            var existingImages = (existing.ImageUrl ?? string.Empty)
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

            var mergedImages = existingImages
                .Concat(uploadedImageUrls)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var finalImageUrl = mergedImages.Any()
                ? string.Join(";", mergedImages)
                : null;

            var payload = new
            {
                OwnerId = uid,
                Name = Poi.Name,
                Category = Poi.Category,
                Latitude = Poi.Latitude,
                Longitude = Poi.Longitude,
                Radius = Poi.Radius,
                Priority = Poi.Priority,
                CooldownSeconds = Poi.CooldownSeconds,
                ImageUrl = finalImageUrl,
                WebsiteUrl = Poi.WebsiteUrl,
                QrCode = Poi.QrCode,
                ContentTitle = ContentTitle_VI,
                ContentSubtitle = ContentSubtitle_VI,
                ContentDescription = ContentDescription_VI,
                ContentPriceMin = ContentPriceMin_VI,
                ContentPriceMax = ContentPriceMax_VI,
                ContentRating = ContentRating_VI,
                ContentOpenTime = ContentOpenTime_VI,
                ContentCloseTime = ContentCloseTime_VI,
                ContentPhoneNumber = ContentPhoneNumber_VI,
                ContentAddress = ContentAddress_VI,
                RequestType = "update",
                TargetPoiId = id,
                Status = "pending"
            };

            var res = await client.PostAsJsonAsync($"api/poiregistration/submit-update/{id}", payload);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                _logger.LogWarning("Submit update failed: {Status} {Body}", res.StatusCode, body);
                ModelState.AddModelError(string.Empty, "Gửi yêu cầu chỉnh sửa thất bại.");
                return await OnGetAsync(id);
            }

            TempData["SuccessMessage"] = "Đã gửi yêu cầu chỉnh sửa POI, chờ admin duyệt.";
            return RedirectToPage("MyPois");
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
    }
}
