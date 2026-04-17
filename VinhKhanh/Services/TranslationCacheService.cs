using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VinhKhanh.Shared;

namespace VinhKhanh.Services
{
    /// <summary>
    /// Translation Cache Service
    /// Manages a local cache of translated content with intelligent preloading (hotset)
    /// and optional warmup for offline scenarios.
    /// 
    /// Hotset Strategy:
    /// - Automatically preloads translations for 10 nearest POIs within 1.5km
    /// - Triggered on location update or manual request
    /// 
    /// Warmup Strategy:
    /// - User-initiated download of full content package for entire route
    /// - Useful for offline tours or low-connectivity areas
    /// </summary>
    public interface ITranslationCacheService
    {
        /// <summary>
        /// Get cached content for POI in requested language, with 3-tier fallback:
        /// 1. Exact language match
        /// 2. Fallback to English
        /// 3. Fallback to Vietnamese
        /// </summary>
        Task<ContentModel> GetContentWithFallbackAsync(int poiId, string preferredLanguage);

        /// <summary>
        /// Cache a content model locally.
        /// </summary>
        void CacheContent(ContentModel content);

        /// <summary>
        /// Hotset: Pre-load translations for 10 nearest POIs within 1.5km of current location.
        /// Should be called periodically during user tracking.
        /// </summary>
        Task PreloadHotsetAsync(double latitude, double longitude, List<PoiModel> allPois, string language);

        /// <summary>
        /// Warmup: Download complete offline package for all POIs on user's route.
        /// User-initiated, may take several seconds.
        /// </summary>
        Task<bool> DownloadOfflinePackageAsync(List<int> poiIds, List<string> languages);

        /// <summary>
        /// Clear aged cache entries to manage storage.
        /// </summary>
        Task ClearCacheAsync();

        /// <summary>
        /// Get cache statistics for diagnostics.
        /// </summary>
        Dictionary<string, object> GetCacheStats();
    }

    public class TranslationCacheService : ITranslationCacheService
    {
        private readonly DatabaseService _dbService;
        private readonly ApiService _apiService;
        private readonly ILogger<TranslationCacheService> _logger;

        // In-memory cache: Key = "poiId_languageCode", Value = ContentModel
        private readonly ConcurrentDictionary<string, ContentModel> _cache = new();

        // Track hotset loading to avoid redundant requests
        private readonly ConcurrentDictionary<string, DateTime> _hotsetLoads = new();
        private const int HotsetCooldownSeconds = 30; // Minimum time between hotset loads

        public TranslationCacheService(
            DatabaseService dbService,
            ApiService apiService,
            ILogger<TranslationCacheService> logger)
        {
            _dbService = dbService;
            _apiService = apiService;
            _logger = logger;
        }

        public async Task<ContentModel> GetContentWithFallbackAsync(int poiId, string preferredLanguage)
        {
            if (string.IsNullOrEmpty(preferredLanguage)) preferredLanguage = "vi";

            try
            {
                // Priority 1: Exact language match from cache
                var cacheKey = $"{poiId}_{preferredLanguage}";
                if (_cache.TryGetValue(cacheKey, out var cached))
                {
                    _logger?.LogInformation($"[TranslationCache] Cache hit for POI {poiId}/{preferredLanguage}");
                    return cached;
                }

                // Try to load from database
                var dbContent = await _dbService.GetContentByPoiIdAsync(poiId, preferredLanguage);
                if (dbContent != null)
                {
                    CacheContent(dbContent);
                    return dbContent;
                }

                // Priority 2: Fallback to English
                if (preferredLanguage != "en")
                {
                    var enCacheKey = $"{poiId}_en";
                    if (_cache.TryGetValue(enCacheKey, out var enCached))
                    {
                        _logger?.LogInformation($"[TranslationCache] Fallback to EN cache for POI {poiId}");
                        return enCached;
                    }

                    var enContent = await _dbService.GetContentByPoiIdAsync(poiId, "en");
                    if (enContent != null)
                    {
                        CacheContent(enContent);
                        return enContent;
                    }
                }

                // Priority 3: Fallback to Vietnamese (original)
                if (preferredLanguage != "vi")
                {
                    var viCacheKey = $"{poiId}_vi";
                    if (_cache.TryGetValue(viCacheKey, out var viCached))
                    {
                        _logger?.LogInformation($"[TranslationCache] Fallback to VI cache for POI {poiId}");
                        return viCached;
                    }

                    var viContent = await _dbService.GetContentByPoiIdAsync(poiId, "vi");
                    if (viContent != null)
                    {
                        CacheContent(viContent);
                        return viContent;
                    }
                }

                _logger?.LogWarning($"[TranslationCache] No content found for POI {poiId} in any language");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"[TranslationCache] Error getting content for POI {poiId}/{preferredLanguage}");
                return null;
            }
        }

        public void CacheContent(ContentModel content)
        {
            if (content == null) return;
            var key = $"{content.PoiId}_{content.LanguageCode}";
            _cache.AddOrUpdate(key, content, (k, old) => content);
        }

        public async Task PreloadHotsetAsync(double latitude, double longitude, List<PoiModel> allPois, string language)
        {
            if (string.IsNullOrEmpty(language)) language = "vi";
            if (allPois == null || !allPois.Any()) return;

            try
            {
                // Cooldown check to avoid excessive API calls
                var cooldownKey = "hotset_load";
                if (_hotsetLoads.TryGetValue(cooldownKey, out var lastLoad))
                {
                    if ((DateTime.UtcNow - lastLoad).TotalSeconds < HotsetCooldownSeconds)
                    {
                        _logger?.LogInformation($"[TranslationCache] Hotset on cooldown");
                        return;
                    }
                }
                _hotsetLoads[cooldownKey] = DateTime.UtcNow;

                // Find 10 nearest POIs within 1.5km
                const double hotsetRadiusKm = 1.5;
                const double earthRadiusKm = 6371.0;

                var nearby = allPois
                    .Select(p => new
                    {
                        Poi = p,
                        Distance = HaversineDistanceKm(latitude, longitude, p.Latitude, p.Longitude)
                    })
                    .Where(x => x.Distance <= hotsetRadiusKm)
                    .OrderBy(x => x.Distance)
                    .Take(10)
                    .Select(x => x.Poi)
                    .ToList();

                if (!nearby.Any())
                {
                    _logger?.LogInformation($"[TranslationCache] No POIs within {hotsetRadiusKm}km for hotset");
                    return;
                }

                _logger?.LogInformation($"[TranslationCache] Hotset: Loading {nearby.Count} POIs");

                // Preload content for each nearby POI
                foreach (var poi in nearby)
                {
                    try
                    {
                        var content = await GetContentWithFallbackAsync(poi.Id, language);
                        // Content is cached automatically if found
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, $"[TranslationCache] Error preloading hotset for POI {poi.Id}");
                    }
                }

                _logger?.LogInformation($"[TranslationCache] Hotset preload completed");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"[TranslationCache] Error in hotset preload");
            }
        }

        public async Task<bool> DownloadOfflinePackageAsync(List<int> poiIds, List<string> languages)
        {
            if (poiIds == null || !poiIds.Any()) return false;
            if (languages == null || !languages.Any()) languages = new List<string> { "vi", "en" };

            try
            {
                _logger?.LogInformation($"[TranslationCache] Warmup: Downloading offline package for {poiIds.Count} POIs, {languages.Count} languages");

                int totalDownloaded = 0;
                int totalFailed = 0;

                foreach (var poiId in poiIds)
                {
                    foreach (var lang in languages)
                    {
                        try
                        {
                            var content = await GetContentWithFallbackAsync(poiId, lang);
                            if (content != null)
                            {
                                totalDownloaded++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, $"[TranslationCache] Error downloading content for POI {poiId}/{lang}");
                            totalFailed++;
                        }
                    }
                }

                _logger?.LogInformation($"[TranslationCache] Warmup completed: {totalDownloaded} downloaded, {totalFailed} failed");
                return totalFailed == 0;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"[TranslationCache] Error in offline package download");
                return false;
            }
        }

        public async Task ClearCacheAsync()
        {
            try
            {
                // Simple strategy: clear in-memory cache (disk cache managed by DatabaseService)
                _cache.Clear();
                _hotsetLoads.Clear();
                _logger?.LogInformation($"[TranslationCache] Cache cleared");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"[TranslationCache] Error clearing cache");
            }
        }

        public Dictionary<string, object> GetCacheStats()
        {
            return new Dictionary<string, object>
            {
                { "CacheSize", _cache.Count },
                { "CachedLanguages", _cache.Values.Select(c => c.LanguageCode).Distinct().Count() },
                { "CachedPois", _cache.Values.Select(c => c.PoiId).Distinct().Count() },
                { "LastHotsetTime", _hotsetLoads.TryGetValue("hotset_load", out var lastTime) ? lastTime : null }
            };
        }

        /// <summary>
        /// Calculate distance between two coordinates using Haversine formula (in kilometers).
        /// </summary>
        private static double HaversineDistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthRadiusKm = 6371.0;
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return earthRadiusKm * c;
        }

        private static double ToRadians(double degrees) => degrees * (Math.PI / 180.0);
    }
}
