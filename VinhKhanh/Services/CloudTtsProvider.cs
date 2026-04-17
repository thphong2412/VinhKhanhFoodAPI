using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace VinhKhanh.Services
{
    /// <summary>
    /// Tier 2: Cloud TTS Stream Provider
    /// Provides cloud-based text-to-speech streaming (e.g., Azure Speech Services, Google Cloud TTS).
    /// Currently stubbed - ready for integration with cloud TTS API.
    /// </summary>
    public class CloudTtsProvider : ICloudTtsProvider
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CloudTtsProvider> _logger;
        private readonly string _elevenLabsApiKey;
        private readonly string _voiceId;
        private readonly bool _isAvailable;
        private readonly string _cacheDir;

        public CloudTtsProvider(HttpClient httpClient, ILogger<CloudTtsProvider> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _elevenLabsApiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY") ?? string.Empty;
            _voiceId = Environment.GetEnvironmentVariable("ELEVENLABS_VOICE_ID") ?? "21m00Tcm4TlvDq8ikWAM";
            _isAvailable = !string.IsNullOrWhiteSpace(_elevenLabsApiKey);
            _cacheDir = Path.Combine(FileSystem.AppDataDirectory, "cloud_tts_cache");
            Directory.CreateDirectory(_cacheDir);
        }

        public async Task<Stream> StreamAudioAsync(string text, string languageCode)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (string.IsNullOrEmpty(languageCode)) languageCode = "vi";

            try
            {
                if (!_isAvailable)
                {
                    _logger?.LogWarning("[CloudTtsProvider] ELEVENLABS_API_KEY not configured.");
                    return null;
                }

                var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes($"{text}_{languageCode}"));
                var hashStr = Convert.ToHexString(hash).Substring(0, 16);
                var cacheFile = Path.Combine(_cacheDir, $"cloud_tts_{hashStr}.mp3");

                if (File.Exists(cacheFile))
                {
                    return File.OpenRead(cacheFile);
                }

                var endpoint = $"https://api.elevenlabs.io/v1/text-to-speech/{_voiceId}";
                var payload = JsonSerializer.Serialize(new
                {
                    text,
                    model_id = "eleven_multilingual_v2"
                });

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("xi-api-key", _elevenLabsApiKey);
                request.Headers.Add("accept", "audio/mpeg");
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("[CloudTtsProvider] ElevenLabs request failed: {StatusCode}", response.StatusCode);
                    return null;
                }

                await using (var fs = File.Create(cacheFile))
                {
                    await response.Content.CopyToAsync(fs);
                }

                return File.Exists(cacheFile) ? File.OpenRead(cacheFile) : null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, $"[CloudTtsProvider] Error streaming audio");
                return null;
            }
        }

        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                if (!_isAvailable) return false;

                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/voices");
                request.Headers.Add("xi-api-key", _elevenLabsApiKey);
                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("[CloudTtsProvider] Health check failed: {StatusCode}", response.StatusCode);
                    return false;
                }

                return _isAvailable;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, $"[CloudTtsProvider] Error checking availability");
                return false;
            }
        }
    }
}
