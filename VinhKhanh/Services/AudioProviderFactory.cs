using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace VinhKhanh.Services
{
    /// <summary>
    /// Unified 4-Tier Audio Provider Factory
    /// Coordinates between multiple TTS providers with fallback chain:
    /// Tier 1: Pre-generated Audio (high quality, admin-uploaded)
    /// Tier 1.5: On-demand Edge TTS (translated + real-time TTS)
    /// Tier 2: Cloud TTS Stream (cloud-based, high fidelity)
    /// Tier 3: Local Native TTS (offline fallback)
    /// </summary>
    public class AudioProviderFactory : IAudioProviderFactory
    {
        private readonly IPreGeneratedAudioProvider _tier1;
        private readonly IEdgeTtsProvider _tier15;
        private readonly ICloudTtsProvider _tier2;
        private readonly NarrationService _tier3;
        private readonly ILogger<AudioProviderFactory> _logger;
        private readonly ConcurrentDictionary<string, (string Path, DateTime CachedAtUtc)> _resolvedAudioCache = new();

        public AudioProviderFactory(
            IPreGeneratedAudioProvider tier1,
            IEdgeTtsProvider tier15,
            ICloudTtsProvider tier2,
            NarrationService tier3,
            ILogger<AudioProviderFactory> logger)
        {
            _tier1 = tier1;
            _tier15 = tier15;
            _tier2 = tier2;
            _tier3 = tier3;
            _logger = logger;
        }

        public async Task<string> GetAudioPathWithFallbackAsync(int poiId, string text, string languageCode)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (string.IsNullOrEmpty(languageCode)) languageCode = "vi";

            var cacheKey = $"{poiId}:{languageCode}:{text.GetHashCode()}";
            if (_resolvedAudioCache.TryGetValue(cacheKey, out var cached)
                && (DateTime.UtcNow - cached.CachedAtUtc) <= TimeSpan.FromMinutes(20)
                && !string.IsNullOrWhiteSpace(cached.Path))
            {
                if (cached.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    || cached.Path.StartsWith("NATIVE_TTS:", StringComparison.OrdinalIgnoreCase)
                    || File.Exists(cached.Path))
                {
                    return cached.Path;
                }
            }

            _logger?.LogInformation($"[AudioProviderFactory] Getting audio for POI {poiId}, lang {languageCode}, text: {text.Substring(0, Math.Min(30, text.Length))}...");

            try
            {
                // ===== TIER 1: Pre-generated Audio (Highest Priority) =====
                _logger?.LogInformation($"[AudioProviderFactory] Tier 1: Checking pre-generated audio...");
                var tier1Audio = await _tier1.GetAudioPathAsync(poiId, languageCode);
                if (!string.IsNullOrEmpty(tier1Audio))
                {
                    _logger?.LogInformation($"[AudioProviderFactory] ✓ Tier 1 SUCCESS: {tier1Audio}");
                    _resolvedAudioCache[cacheKey] = (tier1Audio, DateTime.UtcNow);
                    return tier1Audio;
                }
                _logger?.LogWarning($"[AudioProviderFactory] ✗ Tier 1 failed - no pre-generated audio");

                // ===== TIER 1.5: On-demand Edge TTS (Real-time Translation + TTS) =====
                _logger?.LogInformation($"[AudioProviderFactory] Tier 1.5: Checking Edge TTS...");
                var tier15Text = text;
                if (!string.Equals(languageCode, "vi", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        tier15Text = await _tier15.TranslateTextAsync(text, "vi", languageCode);
                    }
                    catch { }
                }

                var tier15Audio = await _tier15.GetOrGenerateAudioAsync(tier15Text, languageCode);
                if (!string.IsNullOrEmpty(tier15Audio) && File.Exists(tier15Audio))
                {
                    _logger?.LogInformation($"[AudioProviderFactory] ✓ Tier 1.5 SUCCESS: {tier15Audio}");
                    _resolvedAudioCache[cacheKey] = (tier15Audio, DateTime.UtcNow);
                    return tier15Audio;
                }
                _logger?.LogWarning($"[AudioProviderFactory] ✗ Tier 1.5 failed - Edge-TTS not available");

                // ===== TIER 2: Cloud TTS Stream =====
                // This requires special handling since it returns a stream
                if (await _tier2.IsAvailableAsync())
                {
                    _logger?.LogInformation($"[AudioProviderFactory] Tier 2: Checking cloud TTS...");
                    try
                    {
                        var stream = await _tier2.StreamAudioAsync(text, languageCode);
                        if (stream != null)
                        {
                            // Cache the stream to local file
                            var hash = System.Security.Cryptography.SHA256.HashData(
                                System.Text.Encoding.UTF8.GetBytes($"{poiId}_{text}_{languageCode}"));
                            var hashStr = Convert.ToHexString(hash).Substring(0, 16);
                            var cacheFile = Path.Combine(FileSystem.AppDataDirectory, $"cloud_tts_{hashStr}.mp3");

                            using (var fs = File.Create(cacheFile))
                            {
                                await stream.CopyToAsync(fs);
                            }
                            await stream.DisposeAsync();

                            _logger?.LogInformation($"[AudioProviderFactory] ✓ Tier 2 SUCCESS: {cacheFile}");
                            _resolvedAudioCache[cacheKey] = (cacheFile, DateTime.UtcNow);
                            return cacheFile;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, $"[AudioProviderFactory] Tier 2 error");
                    }
                }
                _logger?.LogWarning($"[AudioProviderFactory] ✗ Tier 2 failed - cloud TTS not available");

                // ===== TIER 3: Local Native TTS (Fallback - Online Only) =====
                _logger?.LogInformation($"[AudioProviderFactory] Tier 3: Using native TTS fallback...");
                // Generate TTS audio file on-device using native TextToSpeech
                var tier3File = Path.Combine(FileSystem.AppDataDirectory, $"native_tts_{poiId}_{languageCode}.wav");

                // Use IAudioGenerator if available (Android)
                // Otherwise, rely on NarrationService for immediate playback
                _logger?.LogInformation($"[AudioProviderFactory] ✓ Tier 3 FALLBACK: Using native TTS (will be generated on playback)");

                // Return marker indicating Tier 3 should be used
                var nativeMarker = $"NATIVE_TTS:{poiId}:{languageCode}:{System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(text))}";
                _resolvedAudioCache[cacheKey] = (nativeMarker, DateTime.UtcNow);
                return nativeMarker;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"[AudioProviderFactory] Critical error in audio provider chain");
                return null;
            }
        }

        public List<string> GetAvailableTiers()
        {
            var tiers = new List<string>();
            tiers.Add("Tier 1: Pre-generated Audio (API-managed)");
            tiers.Add("Tier 1.5: Edge translate + TTS (cached)");
            tiers.Add("Tier 2: Cloud TTS stream (cached)");
            tiers.Add("Tier 3: Native TTS (Fallback)");
            return tiers;
        }
    }
}
