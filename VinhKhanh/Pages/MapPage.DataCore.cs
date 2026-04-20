using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        // Return content for requested language; if missing, fall back to auto-translated copy of Vietnamese content
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

                // Final fallback: translate from Vietnamese/English source so UI/content stays fully in selected language.
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
                targetLang = NormalizeToSupportedLanguage(targetLang);
                if (string.Equals(targetLang, "vi", StringComparison.OrdinalIgnoreCase))
                {
                    return source;
                }

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
            var list = source?
                .Where(c => c != null && NormalizeLanguageCode(c.LanguageCode) == normalized)
                .OrderByDescending(ComputeContentQualityScore)
                .ThenByDescending(c => c.Id)
                .ToList() ?? new List<ContentModel>();

            return list.FirstOrDefault();
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

        private async Task<List<ContentModel>> HydrateContentsFromApiAsync(int poiId)
        {
            try
            {
                await EnsureApiBaseReadyAsync();
                var contents = await _apiService.GetContentsByPoiIdAsync(poiId) ?? new List<ContentModel>();
                foreach (var c in contents.Where(c => c != null))
                {
                    try
                    {
                        c.PoiId = poiId;
                        await _dbService.SaveContentAsync(c);
                    }
                    catch { }
                }

                return contents;
            }
            catch
            {
                return new List<ContentModel>();
            }
        }

        private async Task HydratePoiDetailsFromApiAsync(PoiModel poi)
        {
            try
            {
                if (poi == null || poi.Id <= 0) return;

                var now = DateTime.UtcNow;
                if (_poiDetailHydrateUtc.TryGetValue(poi.Id, out var lastHydrateUtc)
                    && (now - lastHydrateUtc).TotalSeconds < 75)
                {
                    return;
                }

                await EnsureApiBaseReadyAsync();
                PoiModel? remotePoi = null;

                var preferredLanguage = NormalizeToSupportedLanguage(_currentLanguage);
                var loadAll = await GetLoadAllCachedAsync(preferredLanguage);
                if (loadAll?.Items == null || !loadAll.Items.Any())
                {
                    loadAll = await GetLoadAllCachedAsync("en");
                }
                PoiLoadAllItem? selectedItem = null;
                if (loadAll?.Items?.Any() == true)
                {
                    selectedItem = loadAll.Items.FirstOrDefault(x => x?.Poi != null && x.Poi.Id == poi.Id);
                    remotePoi = selectedItem?.Poi;
                }

                if (remotePoi == null)
                {
                    var remotePois = await _apiService.GetPoisAsync();
                    remotePoi = remotePois?.FirstOrDefault(p => p.Id == poi.Id);
                }

                if (remotePoi != null)
                {
                    var hasChanged = !string.Equals(poi.Name, remotePoi.Name, StringComparison.Ordinal)
                        || !string.Equals(poi.ImageUrl, remotePoi.ImageUrl, StringComparison.Ordinal)
                        || !string.Equals(poi.Category, remotePoi.Category, StringComparison.Ordinal)
                        || !string.Equals(poi.WebsiteUrl, remotePoi.WebsiteUrl, StringComparison.Ordinal)
                        || !string.Equals(poi.QrCode, remotePoi.QrCode, StringComparison.Ordinal)
                        || poi.Priority != remotePoi.Priority
                        || poi.Radius != remotePoi.Radius
                        || poi.Latitude != remotePoi.Latitude
                        || poi.Longitude != remotePoi.Longitude;

                    if (hasChanged)
                    {
                        poi.Name = remotePoi.Name;
                        poi.ImageUrl = remotePoi.ImageUrl;
                        poi.Category = remotePoi.Category;
                        poi.Priority = remotePoi.Priority;
                        poi.Radius = remotePoi.Radius;
                        poi.Latitude = remotePoi.Latitude;
                        poi.Longitude = remotePoi.Longitude;
                        poi.WebsiteUrl = remotePoi.WebsiteUrl;
                        poi.QrCode = remotePoi.QrCode;
                        try { await _dbService.SavePoiAsync(poi); } catch { }
                    }
                }

                try
                {
                    if (selectedItem?.Localization.HasValue == true
                        && selectedItem.Localization.Value.ValueKind == JsonValueKind.Object)
                    {
                        var loc = selectedItem.Localization.Value;
                        var fallbackContent = new ContentModel
                        {
                            PoiId = poi.Id,
                            LanguageCode = loc.TryGetProperty("languageCode", out var lng)
                                ? (NormalizeToSupportedLanguage(lng.GetString()) ?? "en")
                                : NormalizeToSupportedLanguage(_currentLanguage),
                            Title = loc.TryGetProperty("title", out var title) ? title.GetString() : null,
                            Subtitle = loc.TryGetProperty("subtitle", out var subtitle) ? subtitle.GetString() : null,
                            Description = loc.TryGetProperty("description", out var description) ? description.GetString() : null,
                            AudioUrl = loc.TryGetProperty("audio_url", out var audioUrl) ? audioUrl.GetString() : null,
                            IsTTS = loc.TryGetProperty("isTTS", out var isTts) && isTts.ValueKind == JsonValueKind.True,
                            PriceRange = loc.TryGetProperty("priceRange", out var priceRange) ? priceRange.GetString() : null,
                            Rating = loc.TryGetProperty("rating", out var rating) && rating.TryGetDouble(out var r) ? r : 0,
                            OpeningHours = loc.TryGetProperty("openingHours", out var openingHours) ? openingHours.GetString() : null,
                            PhoneNumber = loc.TryGetProperty("phoneNumber", out var phoneNumber) ? phoneNumber.GetString() : null,
                            Address = loc.TryGetProperty("address", out var address) ? address.GetString() : null,
                            ShareUrl = loc.TryGetProperty("shareUrl", out var shareUrl) ? shareUrl.GetString() : null
                        };

                        if (HasMeaningfulContent(fallbackContent))
                        {
                            await _dbService.SaveContentAsync(fallbackContent);
                        }
                    }
                }
                catch { }

                var hydrated = await HydrateContentsFromApiAsync(poi.Id);
                _poiDetailHydrateUtc[poi.Id] = DateTime.UtcNow;

                // Do not copy fallback language content into selected language slot.
                // Language completeness is handled by full fallback to English at selection time.
            }
            catch { }
        }

        private async Task<PoiLoadAllResult?> GetLoadAllCachedAsync(string language)
        {
            var normalized = NormalizeToSupportedLanguage(language);
            var now = DateTime.UtcNow;

            if (_loadAllCacheByLang.TryGetValue(normalized, out var cache)
                && cache?.Items?.Any() == true
                && (now - cache.CreatedUtc).TotalSeconds < 60)
            {
                return new PoiLoadAllResult
                {
                    Lang = cache.Language,
                    Total = cache.Items.Count,
                    Items = cache.Items
                };
            }

            await _loadAllFetchLock.WaitAsync();
            try
            {
                if (_loadAllCacheByLang.TryGetValue(normalized, out cache)
                    && cache?.Items?.Any() == true
                    && (DateTime.UtcNow - cache.CreatedUtc).TotalSeconds < 60)
                {
                    return new PoiLoadAllResult
                    {
                        Lang = cache.Language,
                        Total = cache.Items.Count,
                        Items = cache.Items
                    };
                }

                var fresh = await _apiService.GetPoisLoadAllAsync(normalized);
                if (fresh?.Items?.Any() == true)
                {
                    _loadAllCacheByLang[normalized] = new LoadAllCacheEntry
                    {
                        CreatedUtc = DateTime.UtcNow,
                        Language = normalized,
                        Items = fresh.Items
                    };
                }

                return fresh;
            }
            finally
            {
                _loadAllFetchLock.Release();
            }
        }

        private async Task EnsurePoiDataReadyAsync()
        {
            try
            {
                if (_pois != null && _pois.Any())
                {
                    return;
                }

                await EnsureApiBaseReadyAsync();

                var preferredLanguage = NormalizeToSupportedLanguage(_currentLanguage);
                var loadAll = await _apiService.GetPoisLoadAllAsync(preferredLanguage);
                if (loadAll?.Items == null || !loadAll.Items.Any())
                {
                    loadAll = await _apiService.GetPoisLoadAllAsync("en");
                }
                var fromLoadAll = loadAll?.Items?
                    .Select(i => i?.Poi)
                    .Where(p => p != null)
                    .GroupBy(p => p.Id)
                    .Select(g => g.First())
                    .ToList() ?? new List<PoiModel>();

                if (!fromLoadAll.Any())
                {
                    fromLoadAll = await _apiService.GetPoisAsync() ?? new List<PoiModel>();
                }

                if (!fromLoadAll.Any())
                {
                    return;
                }

                _pois = fromLoadAll;

                var preloadContents = loadAll?.Items?
                    .Where(i => i?.Poi != null)
                    .SelectMany(i => (i?.Poi?.Contents ?? new List<ContentModel>())
                        .Where(c => c != null)
                        .Select(c =>
                        {
                            c.PoiId = i!.Poi.Id;
                            c.LanguageCode = NormalizeLanguageCode(c.LanguageCode);
                            return c;
                        }))
                    .GroupBy(c => new { c.PoiId, Lang = NormalizeLanguageCode(c.LanguageCode) })
                    .Select(g => g.OrderByDescending(ComputeContentQualityScore).ThenByDescending(x => x.Id).First())
                    .ToList() ?? new List<ContentModel>();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        foreach (var poi in fromLoadAll)
                        {
                            await _dbService.SavePoiAsync(poi);
                        }

                        foreach (var content in preloadContents)
                        {
                            await _dbService.SaveContentAsync(content);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Lỗi lưu DB: {ex.Message}");
                    }
                });
            }
            catch { }
        }

        private async Task EnsureApiBaseReadyAsync()
        {
            if (_apiBaseReady) return;

            await _apiBaseReadyLock.WaitAsync();
            try
            {
                if (_apiBaseReady) return;
                var bootstrap = await _apiService.GetPoisAsync();
                if (bootstrap != null)
                {
                    _apiBaseReady = true;
                }
            }
            catch { }
            finally
            {
                _apiBaseReadyLock.Release();
            }
        }

        private string NormalizeToSupportedLanguage(string? language)
        {
            var normalized = NormalizeLanguageCode(language ?? string.Empty);
            return normalized switch
            {
                "vi" or "en" or "ja" or "ko" or "ru" or "fr" or "th" or "zh" or "es" => normalized,
                _ => "en"
            };
        }

        private static bool IsLikelyImageUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var source = value.ToLowerInvariant();
            return source.EndsWith(".jpg") || source.EndsWith(".jpeg") || source.EndsWith(".png") || source.EndsWith(".webp") || source.EndsWith(".gif");
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