using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;
using VinhKhanh.API.Models;

namespace VinhKhanh.API.Controllers
{
    [Route("api/ai")]
    [ApiController]
    public class AiController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpFactory;
        private readonly AppDbContext _db;
        private static readonly string[] _blockedKeywords = new[]
        {
            "lừa đảo", "giả mạo", "đánh bạc", "ma túy"
        };

        public AiController(IConfiguration config, IHttpClientFactory httpFactory, AppDbContext db)
        {
            _config = config;
            _httpFactory = httpFactory;
            _db = db;
        }

        [HttpPost("translate-content")]
        public async Task<IActionResult> TranslateContent([FromBody] TranslateContentRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.TargetLanguageCode))
                return BadRequest(new { error = "missing_target_language" });

            var targetLang = req.TargetLanguageCode.Trim().ToLowerInvariant();
            var source = req.Source ?? new TranslateContentPayload();

            var apiKey = _config["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            var model = _config["Gemini:Model"] ?? "gemini-1.5-flash";

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                var fallbackTranslated = await TranslateWithFreeApiFallbackAsync(targetLang, source);
                return Ok(new
                {
                    languageCode = targetLang,
                    title = fallbackTranslated.Title ?? string.Empty,
                    subtitle = fallbackTranslated.Subtitle ?? string.Empty,
                    description = fallbackTranslated.Description ?? string.Empty,
                    priceMin = source.PriceMin ?? string.Empty,
                    priceMax = source.PriceMax ?? string.Empty,
                    rating = source.Rating,
                    openTime = source.OpenTime ?? string.Empty,
                    closeTime = source.CloseTime ?? string.Empty,
                    phoneNumber = source.PhoneNumber ?? string.Empty,
                    address = fallbackTranslated.Address ?? string.Empty,
                    fallback = true
                });
            }

            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            var prompt = BuildTranslatePrompt(targetLang, source, strictOnlyTargetLanguage: true);

            string title = source.Title ?? string.Empty;
            string subtitle = source.Subtitle ?? string.Empty;
            string description = source.Description ?? string.Empty;
            string address = source.Address ?? string.Empty;

            try
            {
                var client = _httpFactory.CreateClient();
                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[] { new { text = prompt } }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var response = await client.PostAsync(endpoint, new StringContent(json, Encoding.UTF8, "application/json"));
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, new { error = "translate_failed", detail = body });
                }

                var translatedJson = ExtractTranslatedJson(body);

                try
                {
                    using var translatedDoc = JsonDocument.Parse(translatedJson);
                    var root = translatedDoc.RootElement;
                    if (root.TryGetProperty("title", out var t)) title = t.GetString() ?? title;
                    if (root.TryGetProperty("subtitle", out var s)) subtitle = s.GetString() ?? subtitle;
                    if (root.TryGetProperty("description", out var d)) description = d.GetString() ?? description;
                    if (root.TryGetProperty("address", out var a)) address = a.GetString() ?? address;
                }
                catch
                {
                    // fallback giữ nguyên source nếu parse lỗi
                }

                if (targetLang != "vi" && LooksLikeVietnameseLeak(source, title, subtitle, description))
                {
                    var retryPrompt = BuildTranslatePrompt(targetLang, source, strictOnlyTargetLanguage: true, retryMode: true);
                    var retryPayload = new
                    {
                        contents = new[]
                        {
                            new
                            {
                                parts = new[] { new { text = retryPrompt } }
                            }
                        }
                    };

                    var retryJson = JsonSerializer.Serialize(retryPayload);
                    var retryResponse = await client.PostAsync(endpoint, new StringContent(retryJson, Encoding.UTF8, "application/json"));
                    var retryBody = await retryResponse.Content.ReadAsStringAsync();

                    if (retryResponse.IsSuccessStatusCode)
                    {
                        var retryTranslatedJson = ExtractTranslatedJson(retryBody);
                        try
                        {
                            using var retryDoc = JsonDocument.Parse(retryTranslatedJson);
                            var retryRoot = retryDoc.RootElement;
                            if (retryRoot.TryGetProperty("title", out var t)) title = t.GetString() ?? title;
                            if (retryRoot.TryGetProperty("subtitle", out var s)) subtitle = s.GetString() ?? subtitle;
                            if (retryRoot.TryGetProperty("description", out var d)) description = d.GetString() ?? description;
                            if (retryRoot.TryGetProperty("address", out var a)) address = a.GetString() ?? address;
                        }
                        catch
                        {
                            // giữ giá trị hiện tại nếu retry parse lỗi
                        }
                    }
                }

                var usedFallback = false;
                if (targetLang != "vi" && LooksLikeVietnameseLeak(source, title, subtitle, description))
                {
                    var fallbackTranslated = await TranslateWithFreeApiFallbackAsync(targetLang, source);
                    title = fallbackTranslated.Title ?? title;
                    subtitle = fallbackTranslated.Subtitle ?? subtitle;
                    description = fallbackTranslated.Description ?? description;
                    address = fallbackTranslated.Address ?? address;
                    usedFallback = true;
                }

                return Ok(new
                {
                    languageCode = targetLang,
                    title,
                    subtitle,
                    description,
                    priceMin = source.PriceMin ?? string.Empty,
                    priceMax = source.PriceMax ?? string.Empty,
                    rating = source.Rating,
                    openTime = source.OpenTime ?? string.Empty,
                    closeTime = source.CloseTime ?? string.Empty,
                    phoneNumber = source.PhoneNumber ?? string.Empty,
                    address,
                    fallback = usedFallback
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "translate_exception", detail = ex.Message });
            }
        }

        private static string BuildTranslatePrompt(string targetLang, TranslateContentPayload source, bool strictOnlyTargetLanguage, bool retryMode = false)
        {
            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("Bạn là trợ lý dịch nội dung POI du lịch/ẩm thực.");
            promptBuilder.AppendLine($"Dịch từ tiếng Việt sang ngôn ngữ đích: {targetLang}.");
            promptBuilder.AppendLine("Giữ nguyên ý nghĩa, không thêm bớt thông tin.");
            promptBuilder.AppendLine("Dịch theo ngữ cảnh địa điểm (quán ăn/cửa hàng/điểm tham quan), dùng từ tự nhiên của ngôn ngữ đích cho tiêu đề.");
            if (strictOnlyTargetLanguage)
            {
                promptBuilder.AppendLine("BẮT BUỘC: title/subtitle/description/address phải viết bằng NGÔN NGỮ ĐÍCH, không giữ nguyên tiếng Việt.");
            }
            if (retryMode)
            {
                promptBuilder.AppendLine("Đây là lần dịch lại do bản trước còn tiếng Việt. Hãy đảm bảo kết quả đã được dịch hoàn toàn.");
            }

            promptBuilder.AppendLine();
            promptBuilder.AppendLine("BẮT BUỘC trả về JSON object hợp lệ đúng cấu trúc sau (không markdown, không text thừa):");
            promptBuilder.AppendLine("{");
            promptBuilder.AppendLine("  \"title\": \"...\",");
            promptBuilder.AppendLine("  \"subtitle\": \"...\",");
            promptBuilder.AppendLine("  \"description\": \"...\",");
            promptBuilder.AppendLine("  \"address\": \"...\"");
            promptBuilder.AppendLine("}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Không dịch các trường số liệu/ký hiệu: giá, giờ, rating, phone.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Input:");
            promptBuilder.AppendLine("title: " + (source.Title ?? string.Empty));
            promptBuilder.AppendLine("subtitle: " + (source.Subtitle ?? string.Empty));
            promptBuilder.AppendLine("description: " + (source.Description ?? string.Empty));
            promptBuilder.AppendLine("address: " + (source.Address ?? string.Empty));
            return promptBuilder.ToString();
        }

        private static string ExtractTranslatedJson(string responseBody)
        {
            using var outerDoc = JsonDocument.Parse(responseBody);
            var text = outerDoc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "{}";

            var jsonStart = text.IndexOf('{');
            var jsonEnd = text.LastIndexOf('}');
            return (jsonStart >= 0 && jsonEnd > jsonStart)
                ? text.Substring(jsonStart, jsonEnd - jsonStart + 1)
                : "{}";
        }

        private static bool LooksLikeVietnameseLeak(TranslateContentPayload source, string title, string subtitle, string description)
        {
            return IsSameIgnoringSpaces(source.Title, title)
                || IsSameIgnoringSpaces(source.Subtitle, subtitle)
                || IsSameIgnoringSpaces(source.Description, description)
                || (ContainsVietnameseDiacritics(title) && ContainsVietnameseDiacritics(description));
        }

        private static bool IsSameIgnoringSpaces(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            var a = new string(left.Where(c => !char.IsWhiteSpace(c)).ToArray());
            var b = new string(right.Where(c => !char.IsWhiteSpace(c)).ToArray());
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsVietnameseDiacritics(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            const string chars = "ăâđêôơưáàảãạắằẳẵặấầẩẫậéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ";
            return value.Any(c => chars.Contains(char.ToLowerInvariant(c)));
        }

        private async Task<TranslateContentPayload> TranslateWithFreeApiFallbackAsync(string targetLang, TranslateContentPayload source)
        {
            if (string.Equals(targetLang, "vi", StringComparison.OrdinalIgnoreCase))
            {
                return new TranslateContentPayload
                {
                    Title = source.Title,
                    Subtitle = source.Subtitle,
                    Description = source.Description,
                    Address = source.Address
                };
            }

            return new TranslateContentPayload
            {
                Title = await TranslateTextFreeAsync(source.Title, targetLang),
                Subtitle = await TranslateTextFreeAsync(source.Subtitle, targetLang),
                Description = await TranslateTextFreeAsync(source.Description, targetLang),
                Address = await TranslateTextFreeAsync(source.Address, targetLang)
            };
        }

        private async Task<string?> TranslateTextFreeAsync(string? text, string targetLang)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            try
            {
                var client = _httpFactory.CreateClient();
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=vi&tl={Uri.EscapeDataString(targetLang)}&dt=t&q={Uri.EscapeDataString(text)}";
                var res = await client.GetAsync(url);
                if (!res.IsSuccessStatusCode) return text;

                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);

                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    return text;

                var segments = doc.RootElement[0];
                if (segments.ValueKind != JsonValueKind.Array) return text;

                var translated = new StringBuilder();
                foreach (var segment in segments.EnumerateArray())
                {
                    if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0) continue;
                    var part = segment[0].GetString();
                    if (!string.IsNullOrWhiteSpace(part)) translated.Append(part);
                }

                var translatedText = translated.ToString().Trim();
                return string.IsNullOrWhiteSpace(translatedText) ? text : translatedText;
            }
            catch
            {
                return text;
            }
        }

        [HttpPost("enhance-description")]
        public async Task<IActionResult> EnhanceDescription([FromBody] EnhanceDescriptionRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Name))
                return BadRequest(new { error = "missing_name" });

            var userId = 0;
            if (Request.Headers.TryGetValue("X-User-Id", out var uidRaw))
            {
                int.TryParse(uidRaw.FirstOrDefault(), out userId);
            }

            var dailyLimit = int.TryParse(_config["Gemini:OwnerDailyLimit"], out var cfgLimit) ? cfgLimit : 10;
            if (userId > 0)
            {
                var since = DateTime.UtcNow.Date;
                var usageToday = await _db.AiUsageLogs.CountAsync(x => x.UserId == userId && x.Feature == "enhance_description" && x.TimestampUtc >= since && x.Status == "success");
                if (usageToday >= dailyLimit)
                {
                    await _db.AiUsageLogs.AddAsync(new AiUsageLog
                    {
                        UserId = userId,
                        Feature = "enhance_description",
                        Status = "quota_exceeded",
                        PromptLength = req.RawDescription?.Length ?? 0,
                        OutputLength = 0,
                        Model = _config["Gemini:Model"] ?? "gemini-1.5-flash"
                    });
                    await _db.SaveChangesAsync();

                    return StatusCode(429, new { error = "daily_quota_exceeded", limit = dailyLimit });
                }
            }

            var rawLower = (req.RawDescription ?? string.Empty).ToLowerInvariant();
            if (_blockedKeywords.Any(k => rawLower.Contains(k)))
            {
                if (userId > 0)
                {
                    await _db.AiUsageLogs.AddAsync(new AiUsageLog
                    {
                        UserId = userId,
                        Feature = "enhance_description",
                        Status = "moderation_blocked",
                        PromptLength = req.RawDescription?.Length ?? 0,
                        OutputLength = 0,
                        Model = _config["Gemini:Model"] ?? "gemini-1.5-flash"
                    });
                    await _db.SaveChangesAsync();
                }

                return BadRequest(new { error = "content_blocked_by_policy" });
            }

            var apiKey = _config["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                return StatusCode(500, new { error = "gemini_not_configured" });

            var model = _config["Gemini:Model"] ?? "gemini-1.5-flash";
            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            var maxRetries = int.TryParse(_config["Gemini:RetryCount"], out var cfgRetries) ? Math.Clamp(cfgRetries, 0, 5) : 2;

            var prompt = $"""
Bạn là trợ lý viết mô tả marketing cho POI ẩm thực tại Việt Nam.
Hãy viết lại mô tả hấp dẫn, trung thực, dễ hiểu cho khách du lịch.

Tên quán: {req.Name}
Danh mục: {req.Category}
Địa chỉ: {req.Address}
Mô tả thô: {req.RawDescription}

Yêu cầu:
- 120-180 từ tiếng Việt tự nhiên.
- Không bịa thông tin không có trong dữ liệu.
- Giọng văn thân thiện, gợi cảm giác muốn ghé quán.
""";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                }
            };

            try
            {
                var client = _httpFactory.CreateClient();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var json = JsonSerializer.Serialize(payload);
                HttpResponseMessage? response = null;
                var body = string.Empty;
                var retryCount = 0;

                for (var attempt = 0; attempt <= maxRetries; attempt++)
                {
                    response = await client.PostAsync(endpoint, new StringContent(json, Encoding.UTF8, "application/json"));
                    body = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode) break;

                    if (!IsRetryableStatus(response.StatusCode) || attempt >= maxRetries)
                    {
                        break;
                    }

                    retryCount++;
                    await Task.Delay(GetRetryDelay(attempt));
                }

                if (response == null)
                {
                    return StatusCode(500, new { error = "gemini_empty_response" });
                }

                if (!response.IsSuccessStatusCode)
                {
                    await LogUsageAsync(new AiUsageLog
                    {
                        UserId = userId,
                        Feature = "enhance_description",
                        Status = "failed",
                        RetryCount = retryCount,
                        PromptLength = req.RawDescription?.Length ?? 0,
                        OutputLength = 0,
                        EstimatedInputTokens = EstimateTokens(prompt),
                        EstimatedOutputTokens = 0,
                        EstimatedCostUsd = EstimateCostUsd(EstimateTokens(prompt), 0),
                        Model = model
                    });

                    return StatusCode((int)response.StatusCode, new { error = "gemini_failed", detail = body });
                }

                using var doc = JsonDocument.Parse(body);
                var enhanced = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                var result = new EnhanceDescriptionResponse
                {
                    EnhancedDescription = enhanced ?? string.Empty,
                    Model = model
                };

                var estimatedInputTokens = EstimateTokens(prompt);
                var estimatedOutputTokens = EstimateTokens(result.EnhancedDescription);
                await LogUsageAsync(new AiUsageLog
                {
                    UserId = userId,
                    Feature = "enhance_description",
                    Status = "success",
                    RetryCount = retryCount,
                    PromptLength = req.RawDescription?.Length ?? 0,
                    OutputLength = result.EnhancedDescription?.Length ?? 0,
                    EstimatedInputTokens = estimatedInputTokens,
                    EstimatedOutputTokens = estimatedOutputTokens,
                    EstimatedCostUsd = EstimateCostUsd(estimatedInputTokens, estimatedOutputTokens),
                    Model = model
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                await LogUsageAsync(new AiUsageLog
                {
                    UserId = userId,
                    Feature = "enhance_description",
                    Status = "failed",
                    RetryCount = maxRetries,
                    PromptLength = req.RawDescription?.Length ?? 0,
                    OutputLength = 0,
                    EstimatedInputTokens = EstimateTokens(req.RawDescription),
                    EstimatedOutputTokens = 0,
                    EstimatedCostUsd = EstimateCostUsd(EstimateTokens(req.RawDescription), 0),
                    Model = _config["Gemini:Model"] ?? "gemini-1.5-flash"
                });
                return StatusCode(500, new { error = "gemini_exception", detail = ex.Message });
            }
        }

        private async Task LogUsageAsync(AiUsageLog log)
        {
            if (log.UserId <= 0) return;

            await _db.AiUsageLogs.AddAsync(log);
            await _db.SaveChangesAsync();
        }

        private static bool IsRetryableStatus(System.Net.HttpStatusCode statusCode)
        {
            return statusCode == System.Net.HttpStatusCode.TooManyRequests
                || statusCode == System.Net.HttpStatusCode.BadGateway
                || statusCode == System.Net.HttpStatusCode.ServiceUnavailable
                || statusCode == System.Net.HttpStatusCode.GatewayTimeout
                || statusCode == System.Net.HttpStatusCode.RequestTimeout;
        }

        private static TimeSpan GetRetryDelay(int attempt)
        {
            var seconds = Math.Min(8, Math.Pow(2, attempt + 1));
            return TimeSpan.FromSeconds(seconds);
        }

        private static int EstimateTokens(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return (int)Math.Ceiling(text.Length / 4.0);
        }

        private static decimal EstimateCostUsd(int inputTokens, int outputTokens)
        {
            // Lightweight estimation for governance dashboards (not billing-accurate).
            const decimal inputPerMillion = 0.35m;
            const decimal outputPerMillion = 1.05m;

            var inputCost = (inputTokens / 1_000_000m) * inputPerMillion;
            var outputCost = (outputTokens / 1_000_000m) * outputPerMillion;
            return decimal.Round(inputCost + outputCost, 8, MidpointRounding.AwayFromZero);
        }
    }

    public class EnhanceDescriptionRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string RawDescription { get; set; } = string.Empty;
    }

    public class EnhanceDescriptionResponse
    {
        public string EnhancedDescription { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
    }

    public class TranslateContentRequest
    {
        public string TargetLanguageCode { get; set; } = string.Empty;
        public TranslateContentPayload Source { get; set; } = new();
    }

    public class TranslateContentPayload
    {
        public string? Title { get; set; }
        public string? Subtitle { get; set; }
        public string? Description { get; set; }
        public string? PriceMin { get; set; }
        public string? PriceMax { get; set; }
        public double? Rating { get; set; }
        public string? OpenTime { get; set; }
        public string? CloseTime { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
    }
}
