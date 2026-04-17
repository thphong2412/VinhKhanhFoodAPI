using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VinhKhanh.Shared;

namespace VinhKhanh.Services
{
    /// <summary>
    /// Tier 1: Pre-Generated Audio Provider
    /// Fetches pre-generated audio files from the API that were uploaded by administrators.
    /// These are high-quality professional recordings.
    /// </summary>
    public class PreGeneratedAudioProvider : IPreGeneratedAudioProvider
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PreGeneratedAudioProvider> _logger;
        private readonly Dictionary<string, string> _audioCache; // Key: "poiId_lang", Value: "audioUrl"

        public PreGeneratedAudioProvider(HttpClient httpClient, ILogger<PreGeneratedAudioProvider> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _audioCache = new Dictionary<string, string>();
        }

        public async Task<string> GetAudioPathAsync(int poiId, string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode)) languageCode = "vi";

            var cacheKey = $"{poiId}_{languageCode}";

            // Check in-memory cache first
            if (_audioCache.TryGetValue(cacheKey, out var cachedPath))
            {
                _logger?.LogInformation($"[PreGeneratedAudioProvider] Cache hit for POI {poiId}/{languageCode}");
                return cachedPath;
            }

            try
            {
                // Query API for audio files by POI
                var audioFiles = await _httpClient.GetFromJsonAsync<List<AudioModel>>($"api/audio/by-poi/{poiId}");

                if (audioFiles != null && audioFiles.Any())
                {
                    // Find matching language (prefer exact match, fallback to any)
                    var match = audioFiles.FirstOrDefault(a => 
                        a.LanguageCode?.Equals(languageCode, StringComparison.OrdinalIgnoreCase) == true && 
                        a.IsProcessed && 
                        !a.IsTts); // Prefer pre-generated, not TTS

                    if (match != null && !string.IsNullOrEmpty(match.Url))
                    {
                        _audioCache[cacheKey] = match.Url;
                        _logger?.LogInformation($"[PreGeneratedAudioProvider] Found audio for POI {poiId}/{languageCode}: {match.Url}");
                        return match.Url;
                    }
                }

                _logger?.LogWarning($"[PreGeneratedAudioProvider] No pre-generated audio for POI {poiId}/{languageCode}");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, $"[PreGeneratedAudioProvider] Error fetching audio for POI {poiId}/{languageCode}");
                return null;
            }
        }

        public async Task<bool> HasAudioAsync(int poiId, string languageCode)
        {
            var audioPath = await GetAudioPathAsync(poiId, languageCode);
            return !string.IsNullOrEmpty(audioPath);
        }
    }
}
