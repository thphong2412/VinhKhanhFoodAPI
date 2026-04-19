using Microsoft.Maui.Media;
using Microsoft.Maui.Devices;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace VinhKhanh.Services
{
    public class NarrationService
    {
        private bool _isSpeaking = false;
        private System.Threading.CancellationTokenSource _cts;

        public static string NormalizeLanguageCode(string? language)
        {
            var normalized = (language ?? "en").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized)) return "en";

            if (normalized.Contains('-')) normalized = normalized.Split('-')[0];
            if (normalized.Contains('_')) normalized = normalized.Split('_')[0];

            return normalized switch
            {
                "vn" => "vi",
                "eng" => "en",
                "jp" => "ja",
                "kr" => "ko",
                "cn" => "zh",
                _ => normalized
            };
        }

        private static string? ResolveBestLocaleTag(System.Collections.Generic.IEnumerable<Locale>? locales, string requestedLanguage)
        {
            var all = locales?.ToList() ?? new System.Collections.Generic.List<Locale>();
            if (!all.Any()) return null;

            var lang = NormalizeLanguageCode(requestedLanguage);

            // 1) Exact language-region from user input
            var exactTag = all.FirstOrDefault(l => string.Equals((l.Language ?? string.Empty), requestedLanguage, StringComparison.OrdinalIgnoreCase));
            if (exactTag != null) return exactTag.Language;

            // 2) Exact by normalized language
            var exact = all.FirstOrDefault(l => string.Equals(NormalizeLanguageCode(l.Language), lang, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact.Language;

            // 3) StartsWith fallback (handles zh-CN, zh-TW, etc.)
            var prefix = all.FirstOrDefault(l => (l.Language ?? string.Empty).StartsWith(lang, StringComparison.OrdinalIgnoreCase));
            if (prefix != null) return prefix.Language;

            // 4) Common fallback by script/region-friendly picks
            if (lang == "zh")
            {
                var zhHans = all.FirstOrDefault(l => (l.Language ?? string.Empty).Contains("zh-CN", StringComparison.OrdinalIgnoreCase)
                                                     || (l.Language ?? string.Empty).Contains("zh-Hans", StringComparison.OrdinalIgnoreCase));
                if (zhHans != null) return zhHans.Language;

                var zhAny = all.FirstOrDefault(l => (l.Language ?? string.Empty).StartsWith("zh", StringComparison.OrdinalIgnoreCase));
                if (zhAny != null) return zhAny.Language;
            }

            // 5) Last resort English
            var en = all.FirstOrDefault(l => (l.Language ?? string.Empty).StartsWith("en", StringComparison.OrdinalIgnoreCase));
            return en?.Language;
        }

        public async Task SpeakAsync(string text, string language = "vi")
        {
            if (string.IsNullOrEmpty(text)) return;

            try { _cts?.Cancel(); } catch { }

            _cts = new System.Threading.CancellationTokenSource();
            var token = _cts.Token;

            if (_isSpeaking) await Task.Delay(50);

            try
            {
                _isSpeaking = true;
                var locales = await TextToSpeech.Default.GetLocalesAsync();
                var normalizedLanguage = NormalizeLanguageCode(language);
                var resolvedTag = ResolveBestLocaleTag(locales, normalizedLanguage);
                var locale = locales.FirstOrDefault(l => string.Equals(l.Language, resolvedTag, StringComparison.OrdinalIgnoreCase));

                await TextToSpeech.Default.SpeakAsync(text, new SpeechOptions
                {
                    Locale = locale,
                    Pitch = 1.0f,
                    Volume = 1.0f
                }, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
            finally
            {
                _isSpeaking = false;
                try { _cts?.Dispose(); } catch { }
                _cts = null;
            }
        }

        public void Stop()
        {
            try { if (_cts != null && !_cts.IsCancellationRequested) _cts.Cancel(); } catch { }
            finally { _isSpeaking = false; try { _cts?.Dispose(); } catch { } _cts = null; }
        }
    }
}
