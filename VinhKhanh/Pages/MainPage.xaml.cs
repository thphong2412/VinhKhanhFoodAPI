using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls; // FIX LỖI: ContentPage, SelectionChangedEventArgs, CollectionView, EventArgs
using Microsoft.Maui.Devices.Sensors; // Hỗ trợ Geolocation (Vị trí)
using Microsoft.Maui.Dispatching; // FIX LỖI: IDispatcherTimer
using Microsoft.Maui.Storage;
using VinhKhanh.Services;
using VinhKhanh.Shared;
using PoiModel = VinhKhanh.Shared.PoiModel;

namespace VinhKhanh
{
    public partial class MainPage : ContentPage
    {
        private readonly ApiService _apiService;
        private List<PoiModel> _allPois = new List<PoiModel>();
        private List<TourModel> _allTours = new List<TourModel>();
        private IDispatcherTimer _gpsTimer;
        private bool _isNavigatingToDetail;
        private bool _isCheckingLocation;
        private DateTime _lastLocationCheckUtc = DateTime.MinValue;
        private string _currentLanguage = "en";
        private readonly Dictionary<string, string> _uiTextCache = new(StringComparer.OrdinalIgnoreCase);

        public MainPage()
        {
            InitializeComponent();
            // Resolve ApiService from DI if available, otherwise create default
            try
            {
                _apiService = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService(typeof(VinhKhanh.Services.ApiService)) as VinhKhanh.Services.ApiService ?? new ApiService();
            }
            catch
            {
                _apiService = new ApiService();
            }

            try
            {
                _currentLanguage = NormalizeLanguageCode(Preferences.Default.Get("selected_language", "en"));
            }
            catch
            {
                _currentLanguage = "en";
            }

            _ = ApplyLocalizedUiAsync();
            StartGpsTracking();
        }

        private async void OnLoadPoisClicked(object sender, EventArgs e)
        {
            try
            {
                var pois = await _apiService.GetPoisAsync();
                if (pois != null)
                {
                    _allPois = pois;
                    PoiList.ItemsSource = _allPois;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert(await LocalizeAsync("Error", _currentLanguage), ex.Message, "OK");
            }
        }

        private async void OnLoadToursClicked(object sender, EventArgs e)
        {
            try
            {
                var tours = await _apiService.GetToursAsync();
                if (tours != null)
                {
                    _allTours = tours;
                    // TODO: Hiển thị danh sách tour lên UI (ví dụ: TourList.ItemsSource = _allTours)
                    await DisplayAlert("Tour", $"Đã tải {tours.Count} tour từ server!", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert(await LocalizeAsync("Error", _currentLanguage), ex.Message, "OK");
            }
        }

        private void StartGpsTracking()
        {
            if (_gpsTimer != null)
            {
                return;
            }

            // Sử dụng Dispatcher để tạo Timer chạy ngầm
            _gpsTimer = Dispatcher.CreateTimer();
            _gpsTimer.Interval = TimeSpan.FromSeconds(8);
            _gpsTimer.Tick += async (s, e) => await CheckLocationAsync();
            _gpsTimer.Start();
        }

        private async Task CheckLocationAsync()
        {
            if (_isCheckingLocation || _isNavigatingToDetail)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            if ((nowUtc - _lastLocationCheckUtc).TotalSeconds < 6)
            {
                return;
            }

            try
            {
                _isCheckingLocation = true;
                _lastLocationCheckUtc = nowUtc;
                var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium));

                if (location != null && _allPois != null && _allPois.Any())
                {
                    foreach (var poi in _allPois)
                    {
                        Location poiLoc = new Location(poi.Latitude, poi.Longitude);
                        // Tính khoảng cách giữa người dùng và quán ăn
                        double distance = location.CalculateDistance(poiLoc, DistanceUnits.Kilometers) * 1000;

                        // Nếu cách dưới 50m thì tự động mở trang thuyết minh (DetailsPage)
                        if (distance < 50)
                        {
                            if (_isNavigatingToDetail)
                            {
                                return;
                            }

                            _isNavigatingToDetail = true;
                            _gpsTimer.Stop();
                            await Navigation.PushAsync(new DetailsPage(poi, _currentLanguage));
                            break;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                _isCheckingLocation = false;
            }
        }

        private async void OnPoiSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is PoiModel selectedPoi)
            {
                if (_isNavigatingToDetail)
                {
                    return;
                }

                _isNavigatingToDetail = true;
                await Navigation.PushAsync(new DetailsPage(selectedPoi, _currentLanguage));

                // Reset lại lựa chọn để có thể bấm lại lần sau
                if (sender is CollectionView collectionView)
                {
                    collectionView.SelectedItem = null;
                }
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            try
            {
                var latestLang = NormalizeLanguageCode(Preferences.Default.Get("selected_language", "en"));
                if (!string.Equals(latestLang, _currentLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    _currentLanguage = latestLang;
                    _ = ApplyLocalizedUiAsync();
                }

                if (!_isNavigatingToDetail && _gpsTimer != null && !_gpsTimer.IsRunning)
                {
                    _gpsTimer.Start();
                }
            }
            catch { }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _isNavigatingToDetail = false;
            _isCheckingLocation = false;

            try
            {
                if (_gpsTimer != null && _gpsTimer.IsRunning)
                {
                    _gpsTimer.Stop();
                }
            }
            catch { }
        }

        private async Task ApplyLocalizedUiAsync()
        {
            try
            {
                Title = await LocalizeAsync("Vinh Khanh Food Street", _currentLanguage);
                if (BtnLoadPois != null)
                {
                    BtnLoadPois.Text = await LocalizeAsync("Load narration points", _currentLanguage);
                }
            }
            catch { }
        }

        private async Task<string> LocalizeAsync(string source, string language)
        {
            if (string.IsNullOrWhiteSpace(source)) return string.Empty;
            var lang = NormalizeLanguageCode(language);
            if (lang == "en") return source;

            var cacheKey = $"{lang}:{source}";
            if (_uiTextCache.TryGetValue(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
            {
                return cached;
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl={Uri.EscapeDataString(lang)}&dt=t&q={Uri.EscapeDataString(source)}";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return source;

                var body = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    return source;

                var segments = doc.RootElement[0];
                if (segments.ValueKind != System.Text.Json.JsonValueKind.Array)
                    return source;

                var sb = new System.Text.StringBuilder();
                foreach (var segment in segments.EnumerateArray())
                {
                    if (segment.ValueKind != System.Text.Json.JsonValueKind.Array || segment.GetArrayLength() == 0) continue;
                    var part = segment[0].GetString();
                    if (!string.IsNullOrWhiteSpace(part)) sb.Append(part);
                }

                var translated = sb.ToString().Trim();
                var value = string.IsNullOrWhiteSpace(translated) ? source : translated;
                _uiTextCache[cacheKey] = value;
                return value;
            }
            catch
            {
                return source;
            }
        }

        private static string NormalizeLanguageCode(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return "en";
            var normalized = language.Trim().ToLowerInvariant();
            if (normalized.Contains('-')) normalized = normalized.Split('-')[0];
            if (normalized.Contains('_')) normalized = normalized.Split('_')[0];
            if (normalized == "vn") return "vi";
            if (normalized == "eng") return "en";
            return string.IsNullOrWhiteSpace(normalized) ? "en" : normalized;
        }
    }
}