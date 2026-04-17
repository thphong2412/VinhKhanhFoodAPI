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
                // fallback mềm: trả lại dữ liệu gốc nếu AI chưa cấu hình
                return Ok(new
                {
                    languageCode = targetLang,
                    title = source.Title ?? string.Empty,
                    subtitle = source.Subtitle ?? string.Empty,
                    description = source.Description ?? string.Empty,
                    priceMin = source.PriceMin ?? string.Empty,
                    priceMax = source.PriceMax ?? string.Empty,
                    rating = source.Rating,
                    openTime = source.OpenTime ?? string.Empty,
                    closeTime = source.CloseTime ?? string.Empty,
                    phoneNumber = source.PhoneNumber ?? string.Empty,
                    address = source.Address ?? string.Empty,
                    fallback = true
                });
            }

            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("Bạn là trợ lý dịch nội dung POI.");
            promptBuilder.AppendLine("Hãy dịch từ tiếng Việt sang ngôn ngữ đích: " + targetLang + ".");
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
            var prompt = promptBuilder.ToString();

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

                using var outerDoc = JsonDocument.Parse(body);
                var text = outerDoc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString() ?? "{}";

                var jsonStart = text.IndexOf('{');
                var jsonEnd = text.LastIndexOf('}');
                var translatedJson = (jsonStart >= 0 && jsonEnd > jsonStart)
                    ? text.Substring(jsonStart, jsonEnd - jsonStart + 1)
                    : "{}";

                string title = source.Title ?? string.Empty;
                string subtitle = source.Subtitle ?? string.Empty;
                string description = source.Description ?? string.Empty;
                string address = source.Address ?? string.Empty;

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
                    fallback = false
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "translate_exception", detail = ex.Message });
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
