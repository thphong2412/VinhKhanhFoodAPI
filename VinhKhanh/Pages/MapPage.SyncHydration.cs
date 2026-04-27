using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VinhKhanh.Shared;

namespace VinhKhanh.Pages
{
    public partial class MapPage
    {
        private async Task<List<ContentModel>> HydrateContentsFromApiAsync(int poiId)
        {
            try
            {
                var hasLocal = await _dbService.GetContentsByPoiIdAsync(poiId) ?? new List<ContentModel>();
                var preferredLang = NormalizeLanguageCode(_currentLanguage);
                var hasPreferred = hasLocal.Any(c => NormalizeLanguageCode(c.LanguageCode) == preferredLang && HasMeaningfulContent(c));
                var hasVietnamese = hasLocal.Any(c => NormalizeLanguageCode(c.LanguageCode) == "vi" && HasMeaningfulContent(c));
                if (hasPreferred && hasVietnamese)
                {
                    return hasLocal;
                }

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
                    loadAll = await GetLoadAllCachedAsync("vi")
                              ?? await GetLoadAllCachedAsync("en");
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
                    loadAll = await _apiService.GetPoisLoadAllAsync("vi")
                              ?? await _apiService.GetPoisLoadAllAsync("en");
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

        private async Task EnsurePoiContentRelinkAsync()
        {
            try
            {
                var localContents = await _dbService.GetAllContentsAsync() ?? new List<ContentModel>();
                var poiIds = (_pois ?? new List<PoiModel>())
                    .Where(p => p != null && p.Id > 0)
                    .Select(p => p.Id)
                    .Distinct()
                    .ToList();

                if (poiIds.Any())
                {
                    var hasVietnameseForAllPois = poiIds.All(poiId =>
                        localContents.Any(c => c != null
                                               && c.PoiId == poiId
                                               && NormalizeLanguageCode(c.LanguageCode) == "vi"
                                               && HasMeaningfulContent(c)));

                    if (hasVietnameseForAllPois)
                    {
                        return;
                    }
                }

                await EnsureApiBaseReadyAsync();

                var preferredLanguage = NormalizeToSupportedLanguage(_currentLanguage);
                var loadAll = await GetLoadAllCachedAsync(preferredLanguage);
                if (loadAll?.Items == null || !loadAll.Items.Any())
                {
                    loadAll = await GetLoadAllCachedAsync("vi")
                              ?? await GetLoadAllCachedAsync("en");
                }

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

                if (!preloadContents.Any())
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        foreach (var content in preloadContents)
                        {
                            await _dbService.SaveContentAsync(content);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Lỗi relink content: {ex.Message}");
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
    }
}