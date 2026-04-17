using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace VinhKhanh.Services
{
    /// <summary>
    /// Tier 1.5: On-demand Edge TTS Provider
    /// Provides real-time translation and text-to-speech using Edge-TTS or similar.
    /// Currently stubbed - ready for integration with Edge-TTS API or other TTS service.
    /// </summary>
    public class EdgeTtsProvider : IEdgeTtsProvider
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<EdgeTtsProvider> _logger;
        private readonly string _cacheDir;
        private readonly string _translationCachePath;
        private readonly string _googleTtsBaseUrl = "https://translate.google.com/translate_tts";
        private readonly string _googleTranslateApi = "https://translate.googleapis.com/translate_a/single";

        public EdgeTtsProvider(HttpClient httpClient, ILogger<EdgeTtsProvider> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cacheDir = Path.Combine(FileSystem.AppDataDirectory, "edge_tts_cache");
            _translationCachePath = Path.Combine(_cacheDir, "translation_cache.json");
            Directory.CreateDirectory(_cacheDir);
        }

        public async Task<string> GetOrGenerateAudioAsync(string text, string languageCode)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (string.IsNullOrEmpty(languageCode)) languageCode = "vi";

            try
            {
                // Generate cache key from text hash
                var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{text}_{languageCode}"));
                var hashStr = Convert.ToHexString(hash).Substring(0, 16);
                var cachedFile = Path.Combine(_cacheDir, $"edge_tts_{hashStr}.mp3");

                // Check if cached
                if (File.Exists(cachedFile))
                {
                    _logger?.LogInformation($"[EdgeTtsProvider] Cache hit for text: {text.Substring(0, Math.Min(30, text.Length))}...");
                    return cachedFile;
                }

                var requestUrl =
                    $"{_googleTtsBaseUrl}?ie=UTF-8&client=tw-ob&tl={Uri.EscapeDataString(languageCode)}&q={Uri.EscapeDataString(text)}";

                using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Android 14; Mobile)");

                using var response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("[EdgeTtsProvider] TTS request failed: {StatusCode}", response.StatusCode);
                    return null;
                }

                await using (var fs = File.Create(cachedFile))
                {
                    await response.Content.CopyToAsync(fs);
                }

                return File.Exists(cachedFile) ? cachedFile : null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, $"[EdgeTtsProvider] Error generating audio");
                return null;
            }
        }

        public async Task<string> TranslateTextAsync(string sourceText, string sourceLanguage, string targetLanguage)
        {
            if (string.IsNullOrEmpty(sourceText)) return sourceText;
            if (sourceLanguage == targetLanguage) return sourceText;

            try
            {
                var cache = await LoadTranslationCacheAsync();
                var cacheKey = $"{sourceLanguage}->{targetLanguage}:{sourceText}";
                if (cache.TryGetValue(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
                {
                    return cached;
                }

                var url =
                    $"{_googleTranslateApi}?client=gtx&sl={Uri.EscapeDataString(sourceLanguage)}&tl={Uri.EscapeDataString(targetLanguage)}&dt=t&q={Uri.EscapeDataString(sourceText)}";

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Android 14; Mobile)");

                using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if (!res.IsSuccessStatusCode)
                {
                    return sourceText;
                }

                var payload = await res.Content.ReadAsStringAsync();
                var translated = TryParseGoogleTranslate(payload) ?? sourceText;

                if (!string.IsNullOrWhiteSpace(translated) && !string.Equals(translated, sourceText, StringComparison.Ordinal))
                {
                    cache[cacheKey] = translated;
                    await SaveTranslationCacheAsync(cache);
                }

                return translated;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, $"[EdgeTtsProvider] Translation error");
                return sourceText;
            }
        }

        public async Task ClearCacheAsync()
        {
            try
            {
                if (Directory.Exists(_cacheDir))
                {
                    var files = Directory.GetFiles(_cacheDir, "*", SearchOption.TopDirectoryOnly);
                    var threshold = DateTime.UtcNow.AddDays(-7);

                    foreach (var file in files)
                    {
                        try
                        {
                            var info = new FileInfo(file);
                            if (info.LastWriteTimeUtc < threshold)
                            {
                                info.Delete();
                                _logger?.LogInformation($"[EdgeTtsProvider] Deleted cached file: {file}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, $"[EdgeTtsProvider] Error deleting cached file");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, $"[EdgeTtsProvider] Error clearing cache");
            }
        }

        private static string? TryParseGoogleTranslate(string raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;

                var sentenceArray = root[0];
                if (sentenceArray.ValueKind != JsonValueKind.Array) return null;

                var chunks = new System.Text.StringBuilder();
                foreach (var part in sentenceArray.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.Array && part.GetArrayLength() > 0)
                    {
                        var text = part[0].GetString();
                        if (!string.IsNullOrWhiteSpace(text)) chunks.Append(text);
                    }
                }

                return chunks.Length == 0 ? null : chunks.ToString();
            }
            catch
            {
                return null;
            }
        }

        private async Task<Dictionary<string, string>> LoadTranslationCacheAsync()
        {
            try
            {
                if (!File.Exists(_translationCachePath)) return new Dictionary<string, string>();
                var json = await File.ReadAllTextAsync(_translationCachePath);
                if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        private async Task SaveTranslationCacheAsync(Dictionary<string, string> cache)
        {
            try
            {
                var trimmed = cache
                    .OrderByDescending(kvp => kvp.Key.Length)
                    .Take(500)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                var json = JsonSerializer.Serialize(trimmed);
                await File.WriteAllTextAsync(_translationCachePath, json);
            }
            catch
            {
                // ignore cache write errors
            }
        }
    }
}
