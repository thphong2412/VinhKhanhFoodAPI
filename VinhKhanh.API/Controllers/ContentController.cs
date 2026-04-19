using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text;
using System.Text.Json;
using VinhKhanh.API.Data;
using VinhKhanh.API.Hubs;
using VinhKhanh.Shared;

namespace VinhKhanh.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ContentController : ControllerBase
    {
        private static readonly string[] AutoTranslateLanguages = { "en", "ja", "ko", "zh", "ru", "th", "es", "fr" };
        private readonly AppDbContext _db;
        private readonly IHubContext<SyncHub> _hubContext;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpFactory;

        public ContentController(AppDbContext db, IHubContext<SyncHub> hubContext, IConfiguration config, IHttpClientFactory httpFactory)
        {
            _db = db;
            _hubContext = hubContext;
            _config = config;
            _httpFactory = httpFactory;
        }

        [HttpGet("by-poi/{poiId}")]
        public async Task<IActionResult> GetByPoi(int poiId)
        {
            var list = await _db.PointContents.Where(c => c.PoiId == poiId).ToListAsync();
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ContentModel model)
        {
            if (model == null) return BadRequest();
            model.NormalizeCompositeFields();
            _db.PointContents.Add(model);
            await _db.SaveChangesAsync();

            if (string.Equals(model.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase))
            {
                await RebuildTranslationsFromVietnameseAsync(model.PoiId, model);
            }

            // ✅ Broadcast content creation
            try
            {
                await _hubContext.Clients.All.SendAsync("ContentCreated", model);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SignalR broadcast failed: {ex.Message}");
            }

            return CreatedAtAction(nameof(GetByPoi), new { poiId = model.PoiId }, model);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] ContentModel model)
        {
            var existing = await _db.PointContents.FindAsync(id);
            if (existing == null) return NotFound();
            model.NormalizeCompositeFields();
            existing.LanguageCode = model.LanguageCode;
            existing.Title = model.Title;
            existing.Subtitle = model.Subtitle;
            existing.Description = model.Description;
            existing.AudioUrl = model.AudioUrl;
            existing.IsTTS = model.IsTTS;
            existing.PriceRange = model.PriceRange;
            existing.Rating = model.Rating;
            existing.OpeningHours = model.OpeningHours;
            existing.PhoneNumber = model.PhoneNumber;
            existing.Address = model.Address;
            existing.ShareUrl = model.ShareUrl;
            await _db.SaveChangesAsync();

            if (string.Equals(existing.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase))
            {
                await RebuildTranslationsFromVietnameseAsync(existing.PoiId, existing);
            }

            // ✅ Broadcast content update
            try
            {
                await _hubContext.Clients.All.SendAsync("ContentUpdated", existing);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SignalR broadcast failed: {ex.Message}");
            }

            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var existing = await _db.PointContents.FindAsync(id);
            if (existing == null) return NotFound();

            var poiId = existing.PoiId;
            _db.PointContents.Remove(existing);
            await _db.SaveChangesAsync();

            // ✅ Broadcast content deletion
            try
            {
                await _hubContext.Clients.All.SendAsync("ContentDeleted", id);
                await _hubContext.Clients.All.SendAsync("RequestFullPoiSync", new { poiId, source = "content-delete", timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SignalR broadcast failed: {ex.Message}");
            }

            return NoContent();
        }

        private async Task RebuildTranslationsFromVietnameseAsync(int poiId, ContentModel viContent)
        {
            var oldTranslations = await _db.PointContents
                .Where(c => c.PoiId == poiId && !string.Equals(c.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase))
                .ToListAsync();

            if (oldTranslations.Any())
            {
                _db.PointContents.RemoveRange(oldTranslations);
                await _db.SaveChangesAsync();
            }

            foreach (var lang in AutoTranslateLanguages)
            {
                var translated = await TranslateFromVietnameseAsync(viContent, lang);
                translated.PoiId = poiId;
                translated.LanguageCode = lang;
                translated.NormalizeCompositeFields();
                _db.PointContents.Add(translated);
            }

            await _db.SaveChangesAsync();

            try
            {
                await _hubContext.Clients.All.SendAsync("RequestFullPoiSync", new { poiId, source = "content-translation-rebuild", timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SignalR broadcast failed: {ex.Message}");
            }
        }

        private async Task<ContentModel> TranslateFromVietnameseAsync(ContentModel source, string targetLang)
        {
            var title = await TranslateTextWithFallbackAsync(source.Title, targetLang);
            var subtitle = await TranslateTextWithFallbackAsync(source.Subtitle, targetLang);
            var description = await TranslateTextWithFallbackAsync(source.Description, targetLang);
            var address = await TranslateTextWithFallbackAsync(source.Address, targetLang);

            return new ContentModel
            {
                Title = title ?? source.Title,
                Subtitle = subtitle ?? source.Subtitle,
                Description = description ?? source.Description,
                PriceMin = source.PriceMin,
                PriceMax = source.PriceMax,
                Rating = source.Rating,
                OpenTime = source.OpenTime,
                CloseTime = source.CloseTime,
                PhoneNumber = source.PhoneNumber,
                Address = address ?? source.Address,
                AudioUrl = source.AudioUrl,
                IsTTS = source.IsTTS,
                ShareUrl = source.ShareUrl
            };
        }

        private async Task<string?> TranslateTextWithFallbackAsync(string? text, string targetLang)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var geminiTranslated = await TryTranslateWithGeminiAsync(text, targetLang);
            if (!string.IsNullOrWhiteSpace(geminiTranslated) && !IsSameText(geminiTranslated, text))
                return geminiTranslated;

            var freeTranslated = await TryTranslateWithFreeApiAsync(text, targetLang);
            return string.IsNullOrWhiteSpace(freeTranslated) ? text : freeTranslated;
        }

        private async Task<string?> TryTranslateWithGeminiAsync(string text, string targetLang)
        {
            var apiKey = _config["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey)) return null;

            try
            {
                var model = _config["Gemini:Model"] ?? "gemini-1.5-flash";
                var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                var prompt = $"Dịch từ tiếng Việt sang {targetLang}. Chỉ trả về bản dịch thuần văn bản, không markdown: {text}";

                var payload = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = prompt } } }
                    }
                };

                var client = _httpFactory.CreateClient();
                var body = JsonSerializer.Serialize(payload);
                var res = await client.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json"));
                if (!res.IsSuccessStatusCode) return null;

                var resBody = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(resBody);
                return doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString()
                    ?.Trim();
            }
            catch
            {
                return null;
            }
        }

        private async Task<string?> TryTranslateWithFreeApiAsync(string text, string targetLang)
        {
            try
            {
                var client = _httpFactory.CreateClient();
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=vi&tl={Uri.EscapeDataString(targetLang)}&dt=t&q={Uri.EscapeDataString(text)}";
                var res = await client.GetAsync(url);
                if (!res.IsSuccessStatusCode) return null;

                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    return null;

                var segments = doc.RootElement[0];
                if (segments.ValueKind != JsonValueKind.Array) return null;

                var sb = new StringBuilder();
                foreach (var segment in segments.EnumerateArray())
                {
                    if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0) continue;
                    var part = segment[0].GetString();
                    if (!string.IsNullOrWhiteSpace(part)) sb.Append(part);
                }

                var result = sb.ToString().Trim();
                return string.IsNullOrWhiteSpace(result) ? null : result;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSameText(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right)) return true;
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
