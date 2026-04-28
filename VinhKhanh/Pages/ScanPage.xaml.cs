using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;
using Microsoft.Maui.ApplicationModel;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;
using Microsoft.Maui.Storage;
using VinhKhanh.Services;
using System.Collections.Generic;

namespace VinhKhanh.Pages
{
    public partial class ScanPage : ContentPage
    {
        private bool _isSpeaking = false;
        private string _language = "vi";
        private int _autoPoiId = 0;
        private readonly NarrationService _narrationService;
        private readonly DatabaseService _dbService;
        private readonly AudioQueueService _audioQueue;
        private readonly IAudioGenerator _audioGenerator;
        private readonly ApiService _apiService;
        private readonly Dictionary<string, string> _uiTextCache = new(StringComparer.OrdinalIgnoreCase);

        private static string BuildDeviceAnalyticsId()
        {
            try
            {
                var platform = DeviceInfo.Platform.ToString();
                var model = DeviceInfo.Model?.Trim();
                var manufacturer = DeviceInfo.Manufacturer?.Trim();
                var version = DeviceInfo.VersionString?.Trim();
                var installId = Preferences.Get("VinhKhanh_DeviceId", string.Empty);
                if (string.IsNullOrWhiteSpace(installId))
                {
                    installId = Guid.NewGuid().ToString("N");
                    Preferences.Set("VinhKhanh_DeviceId", installId);
                }

                return $"{platform}|{manufacturer}|{model}|{version}|{installId}";
            }
            catch
            {
                return Environment.MachineName;
            }
        }

        // Constructor accepts dependencies so caller can provide DI-resolved services
        public ScanPage(string language = "vi", int autoPoiId = 0, DatabaseService dbService = null, AudioQueueService audioQueue = null, NarrationService narrationService = null, IAudioGenerator audioGenerator = null, ApiService apiService = null)
        {
            InitializeComponent();
            _language = language ?? "vi";
            _autoPoiId = autoPoiId;
            _dbService = dbService ?? new DatabaseService();
            _audioQueue = audioQueue ?? new AudioQueueService(new Services.NoOpAudioService(), new NarrationService(), null, new System.Net.Http.HttpClient());
            _narrationService = narrationService ?? new NarrationService();
            _audioGenerator = audioGenerator;
            _apiService = apiService ?? new ApiService();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (cameraView != null)
            {
                var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.Camera>();
                }

                if (status == PermissionStatus.Granted)
                {
                    await Task.Delay(500);
                    // SỬA LỆNH BẬT: ZXing dùng IsDetecting = true
                    cameraView.IsDetecting = true;
                }
                else
                {
                    await DisplayAlert(
                        await LocalizeAsync("Camera Permission"),
                        await LocalizeAsync("Camera access is required to scan restaurant QR codes."),
                        await LocalizeAsync("Open settings"));
                }
            }

            // If this page was opened with an auto POI id, play its narration immediately
            if (_autoPoiId > 0)
            {
                try
                {
                    var content = await _dbService.GetContentByPoiIdAsync(_autoPoiId, _language)
                                  ?? await _dbService.GetContentByPoiIdAsync(_autoPoiId, "en");
                    if (content != null && !string.IsNullOrEmpty(content.Description))
                    {
                        await _narrationService.SpeakAsync(content.Description, _language);
                    }
                }
                catch { }
            }
        }

        // SỬA CHỖ ĐỎ: BarcodeEventArgs đổi thành BarcodeDetectionEventArgs
        private void OnBarcodeDetected(object sender, BarcodeDetectionEventArgs e)
        {
            if (_isSpeaking || e.Results == null || !e.Results.Any())
                return;

            string detectedText = e.Results.First().Value;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                _isSpeaking = true;
                try
                {
                    int poiId = 0;

                    // ✅ Handle deeplink format: vinhkhanh://poi/{id}?name={name}&action=viewDetail
                    if (detectedText.StartsWith("vinhkhanh://poi/"))
                    {
                        var uriParts = detectedText.Replace("vinhkhanh://poi/", "").Split('?');
                        if (int.TryParse(uriParts[0], out var id))
                        {
                            poiId = id;
                        }
                    }
                    // New public QR format: https://host/qr/{id}?lang=vi
                    else if (Uri.TryCreate(detectedText, UriKind.Absolute, out var parsed)
                             && parsed.Segments.Length >= 3
                             && (string.Equals(parsed.Segments[1].Trim('/'), "qr", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(parsed.Segments[1].Trim('/'), "listen", StringComparison.OrdinalIgnoreCase)))
                    {
                        var idSeg = parsed.Segments[2].Trim('/');
                        int.TryParse(idSeg, out poiId);
                    }
                    // Old format: POI:id
                    else if (detectedText.StartsWith("POI:", StringComparison.OrdinalIgnoreCase))
                    {
                        var part = detectedText.Substring(4);
                        int.TryParse(part, out poiId);
                    }
                    // Numeric id only
                    else
                    {
                        int.TryParse(detectedText, out poiId);
                    }

                    if (poiId > 0)
                    {
                        // ✅ Instant narration on successful QR scan
                        cameraView.IsDetecting = false;
                        var normalizedLang = NormalizeLanguageCode(_language);

                        // Track QR scan event for analytics/admin counter
                        try
                        {
                            var trace = new VinhKhanh.Shared.TraceLog
                            {
                                PoiId = poiId,
                                DeviceId = BuildDeviceAnalyticsId(),
                                Latitude = 0,
                                Longitude = 0,
                                ExtraJson = $"{{\"event\":\"qr_scan\",\"source\":\"mobile_scan\",\"lang\":\"{normalizedLang}\"}}",
                                DurationSeconds = null
                            };
                            if (_apiService != null) await _apiService.PostTraceAsync(trace);
                        }
                        catch { }

                        var content = await GetBestContentForPoiAsync(poiId, normalizedLang);

                        if (content != null && !string.IsNullOrWhiteSpace(content.Description))
                        {
                            await _narrationService.SpeakAsync(content.Description, normalizedLang);

                            try
                            {
                                var trace = new VinhKhanh.Shared.TraceLog
                                {
                                    PoiId = poiId,
                                    DeviceId = BuildDeviceAnalyticsId(),
                                    Latitude = 0,
                                    Longitude = 0,
                                    ExtraJson = $"{{\"event\":\"tts_play\",\"source\":\"mobile_scan\",\"lang\":\"{normalizedLang}\"}}",
                                    DurationSeconds = null
                                };
                                if (_apiService != null) await _apiService.PostTraceAsync(trace);
                            }
                            catch { }
                        }
                        else
                        {
                            var audio = await GetBestAudioForPoiAsync(poiId, normalizedLang);
                            if (audio != null && !string.IsNullOrWhiteSpace(audio.Url))
                            {
                                _audioQueue?.Enqueue(new AudioItem
                                {
                                    Key = $"qr_audio_{poiId}_{normalizedLang}",
                                    IsTts = false,
                                    FilePath = ResolveAudioUrl(audio.Url),
                                    Language = normalizedLang,
                                    Priority = 100,
                                    PoiId = poiId
                                });

                                try
                                {
                                    var trace = new VinhKhanh.Shared.TraceLog
                                    {
                                        PoiId = poiId,
                                        DeviceId = BuildDeviceAnalyticsId(),
                                        Latitude = 0,
                                        Longitude = 0,
                                        ExtraJson = $"{{\"event\":\"audio_play\",\"source\":\"mobile_scan\",\"lang\":\"{normalizedLang}\"}}",
                                        DurationSeconds = null
                                    };
                                    if (_apiService != null) await _apiService.PostTraceAsync(trace);
                                }
                                catch { }

                                await Task.Delay(500);
                                cameraView.IsDetecting = true;
                                return;
                            }

                            var poi = (await _dbService.GetPoisAsync()).FirstOrDefault(p => p.Id == poiId);
                            if (poi == null)
                            {
                                await RefreshPoiFromApiAsync(poiId);
                                poi = (await _dbService.GetPoisAsync()).FirstOrDefault(p => p.Id == poiId);
                            }

                            if (!string.IsNullOrWhiteSpace(poi?.Name))
                            {
                                await _narrationService.SpeakAsync(poi.Name, normalizedLang);
                            }
                            else
                            {
                                await DisplayAlert(await LocalizeAsync("Missing data"), $"{await LocalizeAsync("No narration content found for POI")} #{poiId}", await LocalizeAsync("OK"));
                            }
                        }

                        await Task.Delay(500);
                        cameraView.IsDetecting = true;
                        return;
                    }
                    else
                    {
                        await DisplayAlert(await LocalizeAsync("Invalid QR"), $"{await LocalizeAsync("Cannot resolve POI from QR content")}: {detectedText}", await LocalizeAsync("OK"));
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert(await LocalizeAsync("Error"), $"{await LocalizeAsync("Error")}: {ex.Message}", await LocalizeAsync("OK"));
                }
                finally
                {
                    _isSpeaking = false;
                    cameraView.IsDetecting = true;
                }
            });
        }

        private async Task _narration_service_fallback(string text)
        {
            try
            {
                var locales = await TextToSpeech.Default.GetLocalesAsync();
                var locale = locales.FirstOrDefault(l => l.Language.StartsWith(_language, StringComparison.OrdinalIgnoreCase));
                await TextToSpeech.Default.SpeakAsync(text, new SpeechOptions { Locale = locale });
            }
            catch { }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            if (cameraView != null)
            {
                // SỬA LỆNH TẮT: Tắt Detecting để rảnh tài nguyên
                cameraView.IsDetecting = false;
            }
        }

        private async Task<Shared.ContentModel> GetBestContentForPoiAsync(int poiId, string lang)
        {
            var content = await _dbService.GetContentByPoiIdAsync(poiId, lang)
                          ?? await _dbService.GetContentByPoiIdAsync(poiId, "en")
                          ?? await _dbService.GetContentByPoiIdAsync(poiId, "vi");
            if (content != null)
            {
                return content;
            }

            try
            {
                var remote = await _apiService.GetContentsByPoiIdAsync(poiId);
                if (remote != null)
                {
                    foreach (var item in remote.Where(x => x != null))
                    {
                        await _dbService.SaveContentAsync(item);
                    }
                }
            }
            catch { }

            return await _dbService.GetContentByPoiIdAsync(poiId, lang)
                   ?? await _dbService.GetContentByPoiIdAsync(poiId, "en")
                   ?? await _dbService.GetContentByPoiIdAsync(poiId, "vi");
        }

        private async Task<Shared.AudioModel> GetBestAudioForPoiAsync(int poiId, string lang)
        {
            var localList = await _dbService.GetAudiosByPoiAsync(poiId);
            var local = SelectBestAudioByLanguage(localList, lang)
                        ?? SelectBestAudioByLanguage(localList, "en")
                        ?? SelectBestAudioByLanguage(localList, "vi");
            if (local != null && !string.IsNullOrWhiteSpace(local.Url))
            {
                return local;
            }

            try
            {
                var remote = await _apiService.GetAudiosByPoiIdAsync(poiId);
                if (remote != null)
                {
                    foreach (var item in remote.Where(x => x != null))
                    {
                        await _dbService.SaveAudioAsync(item);
                    }
                }
            }
            catch { }

            var reloadedLocalList = await _dbService.GetAudiosByPoiAsync(poiId);
            return SelectBestAudioByLanguage(reloadedLocalList, lang)
                   ?? SelectBestAudioByLanguage(reloadedLocalList, "en")
                   ?? SelectBestAudioByLanguage(reloadedLocalList, "vi");
        }

        private static Shared.AudioModel? SelectBestAudioByLanguage(System.Collections.Generic.IEnumerable<Shared.AudioModel>? source, string lang)
        {
            var normalized = NormalizeLanguageCode(lang);
            return source?
                .Where(a => a != null
                            && !a.IsTts
                            && !string.IsNullOrWhiteSpace(a.Url)
                            && NormalizeLanguageCode(a.LanguageCode) == normalized)
                .OrderByDescending(a => a.IsProcessed)
                .ThenByDescending(a => a.CreatedAtUtc)
                .ThenByDescending(a => a.Id)
                .FirstOrDefault();
        }

        private async Task RefreshPoiFromApiAsync(int poiId)
        {
            try
            {
                var loadAll = await _apiService.GetPoisLoadAllAsync("vi");
                var target = loadAll?.Items?.Select(i => i?.Poi).FirstOrDefault(p => p != null && p.Id == poiId);
                if (target != null)
                {
                    await _dbService.SavePoiAsync(target);
                }
            }
            catch { }
        }

        private string ResolveAudioUrl(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return raw;
            }

            try
            {
                var baseApi = _apiService?.CurrentBaseUrl;
                if (!string.IsNullOrWhiteSpace(baseApi) && Uri.TryCreate(baseApi, UriKind.Absolute, out var apiUri))
                {
                    var root = apiUri.GetLeftPart(UriPartial.Authority);
                    return raw.StartsWith("/", StringComparison.Ordinal)
                        ? root + raw
                        : root + "/" + raw;
                }
            }
            catch { }

            return raw;
        }

        private static string NormalizeLanguageCode(string? language)
        {
            var normalized = (language ?? "vi").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized)) return "vi";
            if (normalized.StartsWith("vi")) return "vi";
            if (normalized.StartsWith("en")) return "en";
            if (normalized.StartsWith("ja")) return "ja";
            if (normalized.StartsWith("ko")) return "ko";
            if (normalized.StartsWith("fr")) return "fr";
            if (normalized.StartsWith("ru")) return "ru";
            if (normalized.StartsWith("th")) return "th";
            if (normalized.StartsWith("zh")) return "zh";
            if (normalized.StartsWith("es")) return "es";
            return normalized;
        }

        private async Task<string> LocalizeAsync(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return string.Empty;

            var lang = NormalizeLanguageCode(_language);
            if (lang == "en") return source;

            var key = $"{lang}:{source}";
            if (_uiTextCache.TryGetValue(key, out var cached) && !string.IsNullOrWhiteSpace(cached))
            {
                return cached;
            }

            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl={Uri.EscapeDataString(lang)}&dt=t&q={Uri.EscapeDataString(source)}";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return source;
                }

                var body = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                {
                    return source;
                }

                var segments = doc.RootElement[0];
                if (segments.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    return source;
                }

                var sb = new System.Text.StringBuilder();
                foreach (var segment in segments.EnumerateArray())
                {
                    if (segment.ValueKind != System.Text.Json.JsonValueKind.Array || segment.GetArrayLength() == 0) continue;
                    var part = segment[0].GetString();
                    if (!string.IsNullOrWhiteSpace(part)) sb.Append(part);
                }

                var translated = sb.ToString().Trim();
                var value = string.IsNullOrWhiteSpace(translated) ? source : translated;
                _uiTextCache[key] = value;
                return value;
            }
            catch
            {
                return source;
            }
        }
    }
}