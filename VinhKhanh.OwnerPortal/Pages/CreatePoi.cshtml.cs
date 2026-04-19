using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Globalization;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class CreatePoiModel : PageModel
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<CreatePoiModel> _logger;

        [BindProperty]
        public string Name { get; set; }
        [BindProperty]
        public string Category { get; set; }
        [BindProperty]
        public double Latitude { get; set; }
        [BindProperty]
        public double Longitude { get; set; }
        [BindProperty]
        public double Radius { get; set; } = 50;
        [BindProperty]
        public int Priority { get; set; } = 1;
        [BindProperty]
        public int CooldownSeconds { get; set; } = 300;
        [BindProperty]
        public string? ImageUrl { get; set; }
        [BindProperty]
        public string? WebsiteUrl { get; set; }
        [BindProperty]
        public string? QrCode { get; set; }
        [BindProperty]
        public string? ContentTitle { get; set; }
        [BindProperty]
        public string? ContentSubtitle { get; set; }
        [BindProperty]
        public string? ContentDescription { get; set; }
        [BindProperty]
        public string? ContentPriceMin { get; set; }
        [BindProperty]
        public string? ContentPriceMax { get; set; }
        [BindProperty]
        public double? ContentRating { get; set; }
        [BindProperty]
        public string? ContentOpenTime { get; set; }
        [BindProperty]
        public string? ContentCloseTime { get; set; }
        [BindProperty]
        public string? ContentPhoneNumber { get; set; }
        [BindProperty]
        public string? ContentAddress { get; set; }

        public string? SuccessMessage { get; set; }

        public CreatePoiModel(IHttpClientFactory factory, ILogger<CreatePoiModel> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v)) return RedirectToPage("Login");
            if (!int.TryParse(v, out var uid)) return RedirectToPage("Login");

            try
            {
                string ReadField(params string[] keys)
                {
                    foreach (var key in keys)
                    {
                        var val = Request.Form[key].ToString();
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                    return string.Empty;
                }

                static double ParseDouble(string? raw, double defaultValue = 0)
                {
                    if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
                    var normalized = raw.Trim().Replace(',', '.');
                    return double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)
                        ? n
                        : defaultValue;
                }

                static int ParseInt(string? raw, int defaultValue = 0)
                {
                    if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
                    return int.TryParse(raw.Trim(), out var n) ? n : defaultValue;
                }

                var resolvedImageUrls = new List<string>();
                if (Request.Form.Files.Count > 0)
                {
                    var images = Request.Form.Files.Where(f => f.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var image in images)
                    {
                        var uploadClient = _factory.CreateClient("api");
                        using var form = new MultipartFormDataContent();
                        await using var stream = image.OpenReadStream();
                        form.Add(new StreamContent(stream), "file", image.FileName);
                        var uploadRes = await uploadClient.PostAsync("api/poiregistration/upload-image", form);
                        if (uploadRes.IsSuccessStatusCode)
                        {
                            var body = await uploadRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                            if (body.TryGetProperty("url", out var urlProp))
                            {
                                var imageUrl = urlProp.GetString();
                                if (!string.IsNullOrWhiteSpace(imageUrl))
                                {
                                    resolvedImageUrls.Add(imageUrl);
                                }
                            }
                        }
                    }
                }

                var resolvedImageUrl = resolvedImageUrls.Count > 0
                    ? string.Join(";", resolvedImageUrls.Distinct(StringComparer.OrdinalIgnoreCase))
                    : null;

                var name = ReadField("Name");
                var category = ReadField("Category");
                var latitudeRaw = ReadField("Latitude");
                var longitudeRaw = ReadField("Longitude");
                var titleVi = ReadField("ContentTitle_VI", "ContentTitle");
                var subtitleVi = ReadField("ContentSubtitle_VI", "ContentSubtitle");
                var descVi = ReadField("ContentDescription_VI", "ContentDescription");

                if (string.IsNullOrWhiteSpace(name)
                    || string.IsNullOrWhiteSpace(category)
                    || string.IsNullOrWhiteSpace(latitudeRaw)
                    || string.IsNullOrWhiteSpace(longitudeRaw))
                {
                    ModelState.AddModelError(string.Empty, "Vui lòng nhập đầy đủ thông tin cơ bản (Tên, Loại, Latitude, Longitude).");
                    return Page();
                }

                var registration = new
                {
                    OwnerId = uid,
                    Name = name,
                    Category = category,
                    Latitude = ParseDouble(latitudeRaw),
                    Longitude = ParseDouble(longitudeRaw),
                    Radius = ParseDouble(ReadField("Radius"), 50),
                    Priority = ParseInt(ReadField("Priority"), 1),
                    CooldownSeconds = ParseInt(ReadField("CooldownSeconds"), 300),
                    ImageUrl = resolvedImageUrl,
                    WebsiteUrl = ReadField("WebsiteUrl"),
                    ContentTitle = titleVi,
                    ContentSubtitle = subtitleVi,
                    ContentDescription = descVi,
                    ContentPriceMin = ReadField("ContentPriceMin_VI", "ContentPriceMin"),
                    ContentPriceMax = ReadField("ContentPriceMax_VI", "ContentPriceMax"),
                    ContentRating = ParseDouble(ReadField("ContentRating_VI", "ContentRating"), 0),
                    ContentOpenTime = ReadField("ContentOpenTime_VI", "ContentOpenTime"),
                    ContentCloseTime = ReadField("ContentCloseTime_VI", "ContentCloseTime"),
                    ContentPhoneNumber = ReadField("ContentPhoneNumber_VI", "ContentPhoneNumber"),
                    ContentAddress = ReadField("ContentAddress_VI", "ContentAddress"),
                    Status = "pending"
                };

                var client = _factory.CreateClient("api");
                var apiKey = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["ApiKey"] ?? "admin123";
                client.DefaultRequestHeaders.Remove("X-API-Key");
                client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
                var res = await client.PostAsJsonAsync("api/poiregistration/submit", registration);

                if (!res.IsSuccessStatusCode)
                {
                    var errorContent = await res.Content.ReadAsStringAsync();
                    _logger.LogWarning("POI registration failed: {Status} {Content}", res.StatusCode, errorContent);
                    ModelState.AddModelError("", "Tạo POI thất bại: " + res.StatusCode + (string.IsNullOrWhiteSpace(errorContent) ? string.Empty : $" - {errorContent}"));
                    return Page();
                }

                // Show success and redirect to MyPois
                TempData["SuccessMessage"] = "POI đã được gửi chờ duyệt! Admin sẽ xem xét sớm.";
                return RedirectToPage("MyPois");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating POI registration");
                ModelState.AddModelError("", "Lỗi: " + ex.Message);
                return Page();
            }
        }
    }
}
