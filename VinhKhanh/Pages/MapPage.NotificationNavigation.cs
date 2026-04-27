using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace VinhKhanh.Pages
{
    public partial class MapPage
    {
        private string GetPreferredLanguageForAutoTts()
        {
            try
            {
                var osLang = System.Globalization.CultureInfo.CurrentUICulture?.TwoLetterISOLanguageName;
                var normalizedOsLang = NormalizeLanguageCode(osLang);
                var supported = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "vi", "en", "ru", "fr", "th", "zh", "es", "ja", "ko"
                };

                if (!string.IsNullOrWhiteSpace(normalizedOsLang) && supported.Contains(normalizedOsLang))
                {
                    return normalizedOsLang;
                }
            }
            catch { }

            return NormalizeLanguageCode(_currentLanguage);
        }

        private async Task TryHandlePendingPoiNotificationOpenAsync()
        {
            try
            {
                var poiId = Preferences.Default.Get("pending_poi_id", 0);
                if (poiId <= 0) return;

                var receivedUtcMs = Preferences.Default.Get("pending_poi_received_utc", 0L);
                if (receivedUtcMs > 0)
                {
                    var receivedUtc = DateTimeOffset.FromUnixTimeMilliseconds(receivedUtcMs).UtcDateTime;
                    if ((DateTime.UtcNow - receivedUtc).TotalMinutes > 15)
                    {
                        Preferences.Default.Remove("pending_poi_id");
                        Preferences.Default.Remove("pending_poi_autoplay");
                        Preferences.Default.Remove("pending_poi_name");
                        Preferences.Default.Remove("pending_poi_received_utc");
                        return;
                    }
                }

                if ((DateTime.UtcNow - _lastNotificationOpenUtc).TotalSeconds < 2)
                {
                    return;
                }

                var autoPlay = Preferences.Default.Get("pending_poi_autoplay", true);
                Preferences.Default.Remove("pending_poi_id");
                Preferences.Default.Remove("pending_poi_autoplay");
                Preferences.Default.Remove("pending_poi_name");
                Preferences.Default.Remove("pending_poi_received_utc");
                Preferences.Default.Remove("pending_poi_from_notification");

                if (_pois == null || !_pois.Any())
                {
                    _pois = await _dbService.GetPoisAsync();
                }

                var poi = _pois?.FirstOrDefault(p => p != null && p.Id == poiId);
                if (poi == null)
                {
                    try
                    {
                        var latest = await _dbService.GetPoisAsync();
                        _pois = latest ?? _pois;
                        poi = _pois?.FirstOrDefault(p => p != null && p.Id == poiId);
                    }
                    catch { }
                }

                if (poi == null)
                {
                    try
                    {
                        var fromApi = await _apiService.GetPoisLoadAllAsync("vi") ?? await _apiService.GetPoisAsync().ContinueWith(t => new VinhKhanh.Services.PoiLoadAllResult
                        {
                            Lang = "vi",
                            Items = (t.Result ?? new System.Collections.Generic.List<VinhKhanh.Shared.PoiModel>()).Select(x => new VinhKhanh.Services.PoiLoadAllItem { Poi = x }).ToList(),
                            Total = t.Result?.Count ?? 0
                        });

                        var remotePoi = fromApi?.Items?.Select(i => i?.Poi).FirstOrDefault(p => p != null && p.Id == poiId);
                        if (remotePoi != null)
                        {
                            try { await _dbService.SavePoiAsync(remotePoi); } catch { }
                            _pois = await _dbService.GetPoisAsync();
                            poi = _pois?.FirstOrDefault(p => p != null && p.Id == poiId);
                        }
                    }
                    catch { }
                }

                if (poi == null) return;

                _lastNotificationOpenUtc = DateTime.UtcNow;
                _selectedPoi = poi;
                _pendingNavigationPoiId = poi.Id;

                var preferredLang = GetPreferredLanguageForAutoTts();
                if (!string.IsNullOrWhiteSpace(preferredLang)
                    && !string.Equals(_currentLanguage, preferredLang, StringComparison.OrdinalIgnoreCase))
                {
                    _currentLanguage = preferredLang;
                    try { Preferences.Default.Set("selected_language", preferredLang); } catch { }
                    try { UpdateLanguageSelectionUI(); } catch { }
                    _ = UpdateUiStringsAsync();
                }

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await OpenPoiDetailFromSelectionAsync(poi, "notification_open", userInitiated: false);
                });

                if (autoPlay)
                {
                    var content = await GetContentForLanguageAsync(poi.Id, preferredLang)
                        ?? await GetContentForLanguageAsync(poi.Id, _currentLanguage);
                    if (content != null && !string.IsNullOrWhiteSpace(content.Description))
                    {
                        await PlayNarration(content.Description);
                    }
                }

                _ = TrackPoiEventAsync("poi_notification_open", poi.Id, $"\"trigger\":\"notification\",\"autoplay\":{(autoPlay ? "true" : "false")},\"lang\":\"{NormalizeLanguageCode(_currentLanguage)}\"");
            }
            catch { }
        }
    }
}