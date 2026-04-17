using System.Threading.Tasks;

namespace VinhKhanh.Services
{
    /// <summary>
    /// Tier 1: Pre-generated Audio Provider
    /// Provides high-quality professional audio files that were pre-generated and uploaded by admin.
    /// </summary>
    public interface IPreGeneratedAudioProvider
    {
        /// <summary>
        /// Get pre-generated audio file path for a POI in a specific language.
        /// Returns null if no pre-generated audio exists.
        /// </summary>
        Task<string> GetAudioPathAsync(int poiId, string languageCode);

        /// <summary>
        /// Check if pre-generated audio exists for this POI+language combination.
        /// </summary>
        Task<bool> HasAudioAsync(int poiId, string languageCode);
    }

    /// <summary>
    /// Tier 1.5: On-demand Edge TTS Provider
    /// Provides real-time translation and text-to-speech generation using Edge-TTS or similar service.
    /// Caches generated audio for future use.
    /// </summary>
    public interface IEdgeTtsProvider
    {
        /// <summary>
        /// Get or generate audio for given text in a specific language.
        /// Attempts to retrieve from cache first, then generates if not cached.
        /// Returns null if generation fails.
        /// </summary>
        Task<string> GetOrGenerateAudioAsync(string text, string languageCode);

        /// <summary>
        /// Translate text to target language (optional, can return source if translation not available).
        /// </summary>
        Task<string> TranslateTextAsync(string sourceText, string sourceLanguage, string targetLanguage);

        /// <summary>
        /// Clear aged cache entries to manage disk space.
        /// </summary>
        Task ClearCacheAsync();
    }

    /// <summary>
    /// Tier 2: Cloud TTS Stream Provider
    /// Provides cloud-based TTS streaming (e.g., Azure Speech Services, Google Cloud TTS).
    /// Suitable for languages and voices not available locally.
    /// </summary>
    public interface ICloudTtsProvider
    {
        /// <summary>
        /// Stream audio from cloud TTS service for given text and language.
        /// Returns stream that can be played directly or cached locally.
        /// </summary>
        Task<Stream> StreamAudioAsync(string text, string languageCode);

        /// <summary>
        /// Check if cloud service is available (connectivity check).
        /// </summary>
        Task<bool> IsAvailableAsync();
    }

    /// <summary>
    /// Unified 4-Tier Audio Provider Factory
    /// Coordinates between multiple TTS providers with fallback chain:
    /// 1. Pre-generated Audio (Tier 1)
    /// 1.5. On-demand Edge TTS (Tier 1.5)
    /// 2. Cloud TTS Stream (Tier 2)
    /// 3. Local Native TTS (Tier 3)
    /// </summary>
    public interface IAudioProviderFactory
    {
        /// <summary>
        /// Get audio file path for given POI, language, and text using 4-tier fallback.
        /// Returns the file path if successful, null if all tiers failed.
        /// Tries tiers in order: Tier 1 → 1.5 → 2 → 3
        /// </summary>
        Task<string> GetAudioPathWithFallbackAsync(int poiId, string text, string languageCode);

        /// <summary>
        /// Get available audio tiers (for diagnostics).
        /// </summary>
        List<string> GetAvailableTiers();
    }
}
