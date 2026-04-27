using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VinhKhanh.Shared;

namespace VinhKhanh.Pages
{
    public partial class MapPage
    {
        // Return content for requested language; if missing, fall back to auto-translated copy of Vietnamese or English content
        private async Task<ContentModel> GetContentForLanguageAsync(int poiId, string language)
        {
            try
            {
                var normalizedLanguage = NormalizeToSupportedLanguage(language);
                var content = await GetBestContentFromDbAsync(poiId, normalizedLanguage);
                if (HasMeaningfulContent(content)) return content;

                // Try hydrate from Admin/API when local is missing or stale
                var apiContents = await HydrateContentsFromApiAsync(poiId);
                if (apiContents.Any())
                {
                    content = SelectBestContentByLanguage(apiContents, normalizedLanguage);
                    if (HasMeaningfulContent(content)) return content;
                }

                // Retry local after hydrate
                content = await GetBestContentFromDbAsync(poiId, normalizedLanguage);
                if (HasMeaningfulContent(content)) return content;

                // Final fallback: translate from Vietnamese or English source so UI/content stays fully in selected language.
                var source = await GetBestContentFromDbAsync(poiId, "vi")
                             ?? await GetBestContentFromDbAsync(poiId, "en");
                var translated = await BuildTranslatedContentAsync(source, poiId, normalizedLanguage);
                if (HasMeaningfulContent(translated))
                {
                    try { await _dbService.SaveContentAsync(translated); } catch { }
                    return translated;
                }
            }
            catch { }
            return null;
        }

        private async Task<ContentModel?> BuildTranslatedContentAsync(ContentModel? source, int poiId, string language)
        {
            try
            {
                if (source == null) return null;
                var targetLang = NormalizeLanguageCode(language);
                if (string.Equals(targetLang, "vi", StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizeLanguageCode(source.LanguageCode) == "vi" ? source : null;
                }

                try
                {
                    var onDemand = await _apiService.LocalizationOnDemandAsync(poiId, targetLang);
                    if (onDemand?.Localization != null)
                    {
                        var serverContent = onDemand.Localization;
                        serverContent.PoiId = poiId;
                        serverContent.LanguageCode = NormalizeLanguageCode(serverContent.LanguageCode);
                        return serverContent;
                    }
                }
                catch { }

                return new ContentModel
                {
                    PoiId = poiId,
                    LanguageCode = targetLang,
                    Title = await TranslateTextAsync(source.Title ?? string.Empty, targetLang),
                    Subtitle = await TranslateTextAsync(source.Subtitle ?? string.Empty, targetLang),
                    Description = await TranslateTextAsync(source.Description ?? string.Empty, targetLang),
                    AudioUrl = source.AudioUrl,
                    IsTTS = source.IsTTS,
                    PriceRange = source.PriceRange,
                    Rating = source.Rating,
                    OpeningHours = source.OpeningHours,
                    PhoneNumber = source.PhoneNumber,
                    Address = await TranslateTextAsync(source.Address ?? string.Empty, targetLang),
                    ShareUrl = source.ShareUrl
                };
            }
            catch
            {
                return source;
            }
        }

        private async Task<ContentModel?> GetBestContentFromDbAsync(int poiId, string language)
        {
            try
            {
                var normalized = NormalizeLanguageCode(language);
                var all = await _dbService.GetContentsByPoiIdAsync(poiId) ?? new List<ContentModel>();

                var sameLang = all
                    .Where(c => c != null && NormalizeLanguageCode(c.LanguageCode) == normalized)
                    .OrderByDescending(ComputeContentQualityScore)
                    .ThenByDescending(c => c.Id)
                    .ToList();

                if (sameLang.Any()) return sameLang.First();

                var startsWithLang = all
                    .Where(c => c != null && (c.LanguageCode ?? string.Empty).StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(ComputeContentQualityScore)
                    .ThenByDescending(c => c.Id)
                    .ToList();

                return startsWithLang.FirstOrDefault();
            }
            catch
            {
                return await _dbService.GetContentByPoiIdAsync(poiId, language);
            }
        }

        private ContentModel? SelectBestContentByLanguage(IEnumerable<ContentModel>? source, string language)
        {
            var normalized = NormalizeLanguageCode(language);
            var all = source?
                .Where(c => c != null)
                .ToList() ?? new List<ContentModel>();

            var exact = all
                .Where(c => NormalizeLanguageCode(c.LanguageCode) == normalized)
                .OrderByDescending(ComputeContentQualityScore)
                .ThenByDescending(c => c.Id)
                .FirstOrDefault();
            if (exact != null) return exact;

            var preferredFallback = normalized == "vi" ? "en" : "vi";
            var fallback = all
                .Where(c => NormalizeLanguageCode(c.LanguageCode) == preferredFallback)
                .OrderByDescending(ComputeContentQualityScore)
                .ThenByDescending(c => c.Id)
                .FirstOrDefault();
            if (fallback != null) return fallback;

            return all
                .OrderByDescending(ComputeContentQualityScore)
                .ThenByDescending(c => c.Id)
                .FirstOrDefault();
        }

        private static int ComputeContentQualityScore(ContentModel? content)
        {
            if (content == null) return 0;

            var score = 0;
            if (!string.IsNullOrWhiteSpace(content.Title)) score += 4;
            if (!string.IsNullOrWhiteSpace(content.Description)) score += 6;
            if (!string.IsNullOrWhiteSpace(content.Subtitle)) score += 2;
            if (!string.IsNullOrWhiteSpace(content.Address)) score += 3;
            if (!string.IsNullOrWhiteSpace(content.PhoneNumber)) score += 2;
            if (!string.IsNullOrWhiteSpace(content.OpeningHours)) score += 2;
            if (content.Rating > 0) score += 1;
            return score;
        }

        private static bool HasMeaningfulContent(ContentModel content)
        {
            if (content == null) return false;
            return !string.IsNullOrWhiteSpace(content.Title)
                || !string.IsNullOrWhiteSpace(content.Description)
                || !string.IsNullOrWhiteSpace(content.Subtitle)
                || !string.IsNullOrWhiteSpace(content.Address)
                || !string.IsNullOrWhiteSpace(content.OpeningHours)
                || content.Rating > 0;
        }

        private async Task<string> TranslateTextAsync(string source, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(source)) return string.Empty;

            var normalizedTarget = NormalizeLanguageCode(targetLanguage);

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={Uri.EscapeDataString(normalizedTarget)}&dt=t&q={Uri.EscapeDataString(source)}";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return source;
                }

                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                {
                    return source;
                }

                var segments = doc.RootElement[0];
                if (segments.ValueKind != JsonValueKind.Array)
                {
                    return source;
                }

                var sb = new StringBuilder();
                foreach (var segment in segments.EnumerateArray())
                {
                    if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0) continue;
                    var part = segment[0].GetString();
                    if (!string.IsNullOrWhiteSpace(part)) sb.Append(part);
                }

                var translated = sb.ToString().Trim();
                return string.IsNullOrWhiteSpace(translated) ? source : translated;
            }
            catch
            {
                return source;
            }
        }
    }
}