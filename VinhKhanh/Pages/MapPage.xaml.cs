using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Media;
using Microsoft.Maui.Devices;
using VinhKhanh.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using VinhKhanh.Shared;
using Microsoft.Maui.Networking;

namespace VinhKhanh.Pages
{
    public partial class MapPage : ContentPage
    {
        private readonly DatabaseService _dbService;
        private readonly IGeofenceEngine _geofenceEngine;
        private readonly NarrationService _narrationService;
        private readonly LocationPollingService _locationPollingService;
        private readonly AudioQueueService _audioQueue;
        private readonly PermissionService _permissionService;
        private readonly VinhKhanh.Services.ApiService _apiService;
        private readonly IAudioGenerator _audioGenerator;
        private List<PoiModel> _pois = new();
        private bool _isSpeaking = false;
        private bool _isTrackingActive = false;
        private PoiModel _selectedPoi;
        // Drag state for POI detail panel
        private double _poiStartTranslationY = 0;
        private readonly double _poiCollapseDistance = 420; // max distance to drag down
        private readonly double _poiExpandDistance = 420; // max distance to drag up
        private bool _isDescriptionExpanded = false;
        // current language code for UI and narration: "vi" by default
        private string _currentLanguage = "vi";
        private System.Collections.ObjectModel.ObservableCollection<string> _logItems;
        private Location _lastLocation; // Track last known location

        public MapPage(DatabaseService dbService, IGeofenceEngine geofenceEngine, NarrationService narrationService,
            LocationPollingService locationPollingService, AudioQueueService audioQueue, PermissionService permissionService, VinhKhanh.Services.ApiService apiService, IAudioGenerator audioGenerator)
        {
            InitializeComponent();
            _dbService = dbService;
            _geofenceEngine = geofenceEngine;
            _narrationService = narrationService;
            _locationPollingService = locationPollingService;
            _audioQueue = audioQueue;
            _permissionService = permissionService;
            _apiService = apiService;
            _audioGenerator = audioGenerator;
            // Đăng ký sự kiện PoiTriggered
            _geofenceEngine.PoiTriggered += OnPoiTriggered;
            // close POI when tapping on empty map area
            try { vinhKhanhMap.MapClicked += OnMapClicked; } catch { }

            // placeholder: action button images are now inside pill Frames (no direct named ImageButtons)

            // ensure language UI state and strings reflect current selection at startup
            UpdateLanguageSelectionUI();
            UpdateUiStrings();

            // Show language panel at first launch (if not previously selected)
            try
            {
                var seen = Preferences.Default.Get("lang_seen", false);
                if (!seen)
                {
                    LanguagePanel.IsVisible = true;
                }
            }
            catch { }

            // init logs collection
            _logItems = new System.Collections.ObjectModel.ObservableCollection<string>();
            try { CvLog.ItemsSource = _logItems; } catch { }

            // Highlights collection placeholder
            try { CvHighlights.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<PoiModel>(); } catch { }

        }

        // Map page QR tap handler (opens QR modal centered)
        private async void OnShowQrClicked_Map(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPoi == null)
                {
                    await DisplayAlert("Lỗi", "Chưa chọn điểm để xem QR.", "Đóng");
                    return;
                }

                var payload = _selectedPoi.QrCode;
                if (string.IsNullOrEmpty(payload))
                {
                    payload = $"POI:{_selectedPoi.Id}";
                    _selectedPoi.QrCode = payload;
                    try { await _dbService.SavePoiAsync(_selectedPoi); } catch { }
                }

                // create modal page with QR image and X close in corner
                var qrSrc = await new MapPageHelpers().GenerateQrImageSourceAsync(payload);
                var overlay = new Grid { BackgroundColor = Microsoft.Maui.Graphics.Colors.Black.WithAlpha(0.6f) };

                var box = new Frame { BackgroundColor = Microsoft.Maui.Graphics.Colors.White, CornerRadius = 16, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Padding = 16 };
                var img = new Image { Source = qrSrc, WidthRequest = 300, HeightRequest = 300, Aspect = Aspect.AspectFit };
                var closeX = new Button { Text = "✕", BackgroundColor = Microsoft.Maui.Graphics.Colors.Transparent, TextColor = Microsoft.Maui.Graphics.Colors.Black, FontSize = 20, WidthRequest = 44, HeightRequest = 44, CornerRadius = 22, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start };
                closeX.Clicked += async (s, ev) => await Navigation.PopModalAsync();

                var closeBtn = new Button { Text = "Đóng", BackgroundColor = Microsoft.Maui.Graphics.Colors.Black, TextColor = Microsoft.Maui.Graphics.Colors.White, CornerRadius = 10, HeightRequest = 44 };
                closeBtn.Clicked += async (s, ev) => await Navigation.PopModalAsync();

                box.Content = new StackLayout { Spacing = 12, Children = { img, closeBtn } };
                overlay.Children.Add(box);
                overlay.Children.Add(closeX);

                var page = new ContentPage { Content = overlay, BackgroundColor = Microsoft.Maui.Graphics.Colors.Transparent };
                await Navigation.PushModalAsync(page);
            }
            catch { }
        }

        // Allow dragging highlights panel up to view more
        public void OnHighlightsPanUpdated(object sender, PanUpdatedEventArgs e)
        {
            try
            {
                if (HighlightsPanel == null) return;
                switch (e.StatusType)
                {
                    case GestureStatus.Running:
                        var newY = HighlightsPanel.TranslationY + e.TotalY;
                        if (newY < -200) newY = -200; // limit upward drag
                        if (newY > 0) newY = 0;
                        HighlightsPanel.TranslationY = newY;
                        break;
                    case GestureStatus.Completed:
                    case GestureStatus.Canceled:
                        if (HighlightsPanel.TranslationY <= -100)
                        {
                            // expand panel by pushing full POI list (we'll open HighlightsListPage)
                            try { _ = Navigation.PushAsync(new HighlightsListPage(_pois.OrderByDescending(p => p.Priority).ToList())); } catch { }
                            // reset translation
                            HighlightsPanel.TranslationY = 0;
                        }
                        else
                        {
                            // snap back
                            _ = HighlightsPanel.TranslateTo(0, 0, 180, Easing.CubicOut);
                        }
                        break;
                }
            }
            catch { }
        }

        // Carousel navigation buttons
        private void OnImgPrevClicked(object sender, EventArgs e)
        {
            try
            {
                if (ImgCarousel == null) return;
                var pos = ImgCarousel.Position;
                if (pos > 0) ImgCarousel.Position = pos - 1;
            }
            catch { }
        }

        private void OnImgNextClicked(object sender, EventArgs e)
        {
            try
            {
                if (ImgCarousel == null) return;
                var pos = ImgCarousel.Position;
                var count = (ImgCarousel.ItemsSource as System.Collections.ICollection)?.Count ?? 0;
                if (pos < count - 1) ImgCarousel.Position = pos + 1;
            }
            catch { }
        }

        // Narration action: show choices Audio / TTS
        // NOTE: OnStartNarrationClicked already implemented further down; do not duplicate here.

        private async void OnSelectAudioClicked(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPoi == null) return;
                var result = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Chọn file audio" });
                if (result == null) return;

                // copy to app data directory for stable access
                var fileName = result.FileName;
                var dest = System.IO.Path.Combine(FileSystem.AppDataDirectory, fileName);
                using (var src = await result.OpenReadAsync())
                using (var dst = System.IO.File.Create(dest))
                {
                    await src.CopyToAsync(dst);
                }

                // save metadata in local DB
                var audio = new VinhKhanh.Shared.AudioModel
                {
                    PoiId = _selectedPoi.Id,
                    Url = dest,
                    LanguageCode = _currentLanguage,
                    IsTts = false,
                    IsProcessed = true
                };

                try { await _dbService.SaveAudioAsync(audio); } catch { }

                // also attach to content record for playback convenience
                var content = await _dbService.GetContentByPoiIdAsync(_selectedPoi.Id, _currentLanguage);
                if (content != null)
                {
                    content.AudioUrl = dest;
                    await _dbService.SaveContentAsync(content);
                }

                await DisplayAlert("OK", "File audio đã được lưu và gắn vào điểm này.", "Đóng");
            }
            catch (Exception ex)
            {
                AddLog($"Select audio failed: {ex.Message}");
                try { await DisplayAlert("Lỗi", "Không thể lưu file audio.", "Đóng"); } catch { }
            }
        }

        private async void OnGenerateTtsClicked(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPoi == null) return;
                // Generate/queue TTS for these languages
                var langs = new[] { "vi", "en", "ja", "ko" };
                foreach (var lang in langs)
                {
                    var content = await GetContentForLanguageAsync(_selectedPoi.Id, lang) ?? await _dbService.GetContentByPoiIdAsync(_selectedPoi.Id, "vi");
                    var text = content?.Description ?? _selectedPoi.Name ?? "";
                    if (string.IsNullOrEmpty(text)) continue;

                    // Try to generate a TTS file on-device (Android implementation)
                    var filename = $"poi_{_selectedPoi.Id}_{lang}.wav";
                    var outPath = System.IO.Path.Combine(FileSystem.AppDataDirectory, filename);
                    var generated = false;
                    try
                    {
                        if (_audioGenerator != null)
                        {
                            generated = await _audioGenerator.GenerateTtsToFileAsync(text, lang, outPath);
                        }
                    }
                    catch { generated = false; }

                    if (generated)
                    {
                        // save metadata and enqueue playback of generated file
                        var audio = new VinhKhanh.Shared.AudioModel
                        {
                            PoiId = _selectedPoi.Id,
                            Url = outPath,
                            LanguageCode = lang,
                            IsTts = true,
                            IsProcessed = true
                        };
                        try { await _dbService.SaveAudioAsync(audio); } catch { }

                        var item = new VinhKhanh.Services.AudioItem
                        {
                            IsTts = false,
                            FilePath = outPath,
                            Language = lang,
                            PoiId = _selectedPoi.Id,
                            Priority = 5
                        };
                        _audioQueue.Enqueue(item);
                    }
                    else
                    {
                        // Fallback: enqueue TTS speak if file generation not supported
                        var item = new VinhKhanh.Services.AudioItem
                        {
                            IsTts = true,
                            Language = lang,
                            Text = text,
                            PoiId = _selectedPoi.Id,
                            Priority = 5
                        };
                        _audioQueue.Enqueue(item);
                    }
                }

                await DisplayAlert("TTS", "Đã tạo TTS tạm thời và đưa vào hàng đợi phát. (Phát bằng TTS cục bộ)", "OK");
            }
            catch (Exception ex)
            {
                AddLog($"Generate TTS failed: {ex.Message}");
            }
        }

        private async void OnShowQrClicked(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPoi == null) return;
                // Ensure POI has QrCode payload
                if (string.IsNullOrEmpty(_selectedPoi.QrCode))
                {
                    _selectedPoi.QrCode = $"POI:{_selectedPoi.Id}";
                    try { await _dbService.SavePoiAsync(_selectedPoi); } catch { }
                }

                var payload = _selectedPoi.QrCode;
                var action = await DisplayActionSheet("QR cho điểm này", "Đóng", null, "Sao chép payload", "Chia sẻ payload", "Mở trang quét (mô phỏng)");
                if (action == "Sao chép payload")
                {
                    try { await Clipboard.Default.SetTextAsync(payload); await DisplayAlert("OK", "Đã sao chép payload vào clipboard", "Đóng"); } catch { }
                }
                else if (action == "Chia sẻ payload")
                {
                    try { await Share.RequestAsync(new ShareTextRequest { Text = payload, Title = "QR payload" }); } catch { }
                }
                else if (action == "Mở trang quét (mô phỏng)")
                {
                    // open ScanPage with auto POI id to simulate scanning QR
                    try { await Navigation.PushAsync(new ScanPage(_currentLanguage, _selectedPoi.Id, _dbService, _audioQueue, _narrationService, _audioGenerator)); } catch { }
                }
            }
            catch (Exception ex)
            {
                AddLog($"Show QR failed: {ex.Message}");
            }
        }

        private void AddLog(string text)
        {
            try
            {
                var entry = $"[{DateTime.Now:HH:mm:ss}] {text}";
                _logItems.Insert(0, entry);
                if (_logItems.Count > 200) _logItems.RemoveAt(_logItems.Count - 1);
            }
            catch { }
        }

        private async void OnStartTrackingClicked(object sender, EventArgs e)
        {
            try
            {
                BtnStartTracking.IsEnabled = false;
                AddLog("Requesting permissions...");
                var ok = await _permissionService.EnsureLocationPermissionsAsync();
                if (!ok)
                {
                    AddLog("Permissions denied");
                    LblTrackingStatus.Text = "Status: permission denied";
                    await DisplayAlert("Quyền bị từ chối", "Ứng dụng cần quyền vị trí để theo dõi. Vui lòng cấp quyền và thử lại.", "OK");
                    BtnStartTracking.IsEnabled = true;
                    return;
                }

                AddLog("Starting tracking service");
                // If background permission not granted, prompt the user to open Settings
                var bgOk = await _permissionService.IsBackgroundLocationGrantedAsync();
                if (!bgOk)
                {
                    var go = await DisplayAlert("Quyền nền", "Ứng dụng chưa được cấp quyền vị trí nền. Để theo dõi khi app ở nền, vui lòng cấp Permission 'Allow all the time' trong Cài đặt.", "Mở Cài đặt", "Tiếp tục (không cho phép)");
                    if (go)
                    {
                        try
                        {
                            AppInfo.ShowSettingsUI();
                        }
                        catch { }
                        BtnStartTracking.IsEnabled = true;
                        return;
                    }
                }

                await _locationPollingService.StartAsync();
                AddLog("Tracking service start requested");
                _isTrackingActive = true;
                LblTrackingStatus.Text = "Status: tracking";
                BtnStartTracking.IsEnabled = false;
                BtnStopTracking.IsEnabled = true;
            }
            catch (Exception ex)
            {
                AddLog($"Start failed: {ex.Message}");
                BtnStartTracking.IsEnabled = true;
            }
        }

        private async void OnStopTrackingClicked(object sender, EventArgs e)
        {
            try
            {
                AddLog("Stopping tracking service");
                await _locationPollingService.StopAsync();
                _isTrackingActive = false;
                LblTrackingStatus.Text = "Status: stopped";
            }
            catch (Exception ex)
            {
                AddLog($"Stop failed: {ex.Message}");
            }
        }

        // Return content for requested language; if missing, fall back to auto-translated copy of Vietnamese content
        private async Task<ContentModel> GetContentForLanguageAsync(int poiId, string language)
        {
            try
            {
                var content = await _dbService.GetContentByPoiIdAsync(poiId, language);
                if (content != null) return content;

                // if not present, try to get Vietnamese source and create a provisional translated copy
                if (language != "vi")
                {
                    var vi = await _dbService.GetContentByPoiIdAsync(poiId, "vi");
                    if (vi != null)
                    {
                        // create provisional copy
                        var copy = new ContentModel
                        {
                            PoiId = vi.PoiId,
                            LanguageCode = language,
                            Title = await TranslateTextAsync(vi.Title, language),
                            Subtitle = await TranslateTextAsync(vi.Subtitle, language),
                            Description = await TranslateTextAsync(vi.Description, language),
                            PriceRange = vi.PriceRange,
                            Rating = vi.Rating,
                            OpeningHours = vi.OpeningHours,
                            PhoneNumber = vi.PhoneNumber,
                            ShareUrl = vi.ShareUrl,
                            Address = await TranslateTextAsync(vi.Address, language)
                        };

                        return copy;
                    }
                }
            }
            catch { }
            return null;
        }

        // Very small placeholder translator: currently returns source text unchanged.
        // Replace with real translation service integration if available later.
        private Task<string> TranslateTextAsync(string source, string targetLanguage)
        {
            if (string.IsNullOrEmpty(source)) return Task.FromResult(string.Empty);
            // TODO: integrate real translation API. For now return source text.
            return Task.FromResult(source);
        }

        // Japanese and Korean selection handlers
        private async void OnSelectJapaneseClicked(object sender, EventArgs e)
        {
            _currentLanguage = "ja";
            UpdateLanguageSelectionUI();
            UpdateUiStrings();
            await DisplayAllPois();
        }

        private async void OnSelectKoreanClicked(object sender, EventArgs e)
        {
            _currentLanguage = "ko";
            UpdateLanguageSelectionUI();
            UpdateUiStrings();
            await DisplayAllPois();
        }

        // Close POI panel when clicking on map background
        private void OnMapClicked(object sender, Microsoft.Maui.Controls.Maps.MapClickedEventArgs e)
        {
            try
            {
                if (PoiDetailPanel != null && PoiDetailPanel.IsVisible)
                {
                    try { _narrationService?.Stop(); } catch { }
                    PoiDetailPanel.IsVisible = false;
                }
            }
            catch { }
        }

        // When user taps highlight item
        private async void OnHighlightSelected(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var selVm = e.CurrentSelection?.FirstOrDefault() as VinhKhanh.Shared.HighlightViewModel;
                var sel = selVm?.Poi;
                if (sel == null) return;
                _selectedPoi = sel;
                // hide highlights and show detail panel
                HighlightsPanel.IsVisible = false;
                await ShowPoiDetail(sel);
            }
            catch { }
        }

        private async void OnHighlightsTitleTapped(object sender, EventArgs e)
        {
            try
            {
                // open full highlights list
                try
                {
                    var list = new VinhKhanh.Pages.HighlightsListPage(_pois.OrderByDescending(p => p.Priority).ToList());
                    await Navigation.PushAsync(list);
                }
                catch
                {
                    await ShowHighlightsListFallback(_pois.OrderByDescending(p => p.Priority).ToList());
                }
            }
            catch { }
        }

        private async void OnViewAllHighlightsClicked(object sender, EventArgs e)
        {
            try
            {
                // Open highlights list page
                try
                {
                    var list = new VinhKhanh.Pages.HighlightsListPage(_pois.OrderByDescending(p => p.Priority).ToList());
                    await Navigation.PushAsync(list);
                }
                catch
                {
                    await ShowHighlightsListFallback(_pois.OrderByDescending(p => p.Priority).ToList());
                }
            }
            catch { }
        }

        // tapped on the highlight card (frame)
        private async void OnHighlightTapped(object sender, EventArgs e)
        {
            try
            {
                // frame's BindingContext is HighlightViewModel
                if (sender is VisualElement ve && ve.BindingContext is VinhKhanh.Shared.HighlightViewModel vm && vm.Poi != null)
                {
                    var poi = vm.Poi;
                    _selectedPoi = poi;
                    HighlightsPanel.IsVisible = false;
                    await ShowPoiDetail(poi);
                }
            }
            catch { }
        }

        private async void OnViewSavedClicked(object sender, EventArgs e)
        {
            try
            {
                // show saved POIs inside highlights area
                _pois = await _dbService.GetPoisAsync();
                var saved = _pois.Where(p => p.IsSaved).ToList();
                if (!saved.Any())
                {
                    await DisplayAlert("Thông báo", "Bạn chưa lưu địa điểm nào.", "OK");
                    return;
                }

                try
                {
                    var list = new VinhKhanh.Pages.HighlightsListPage(saved);
                    await Navigation.PushAsync(list);
                }
                catch
                {
                    await ShowHighlightsListFallback(saved);
                }
            }
            catch { }
        }

        protected override async void OnNavigatedTo(NavigatedToEventArgs args)
        {
            base.OnNavigatedTo(args);

            CenterMapOnVinhKhanh();

            try
            {
                // Nạp dữ liệu mẫu nếu máy chưa có
                await SeedFullData();
                _pois = await _dbService.GetPoisAsync();
                // Show 'show saved' floating button when at least one POI is saved
                try { BtnShowSaved.IsVisible = _pois.Any(p => p.IsSaved); } catch { }
                await DisplayAllPois();
                // update engine with current POIs
                _geofenceEngine?.UpdatePois(_pois);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi load dữ liệu: {ex.Message}");
            }

            // Check whether the native map control actually rendered tiles. If map fails to render
            // (common reasons: invalid API key, billing not enabled, emulator missing Google Play services)
            // show a fallback WebView (MapboxOfflineWebView) and notify the user.
            _ = CheckMapDisplayAsync();

            // NOTE: Do NOT start proximity/background tracking automatically on page navigation.
            // Starting tracking requires user permission and explicit user action (press Start).
            // If you want to auto-start for testing, call OnStartTrackingClicked or set
            // _isTrackingActive = true and start StartProximityTracking() after ensuring permissions.
        }

        // If native map fails to render within short timeout, fall back to WebView and surface hints to user.
        private async Task CheckMapDisplayAsync()
        {
            try
            {
                // wait a short time for map to initialize
                await Task.Delay(2500);
                // If VisibleRegion is null or center NaN, consider map not rendered
                if (vinhKhanhMap == null || vinhKhanhMap.VisibleRegion == null || double.IsNaN(vinhKhanhMap.VisibleRegion.Center?.Latitude ?? double.NaN))
                {
                    AddLog("Map control did not render - showing fallback WebView.");
                    try
                    {
                        // show fallback Mapbox webview (mapbox-offline.html should be present in app resources)
                        if (MapboxOfflineWebView != null) MapboxOfflineWebView.IsVisible = true;
                        if (vinhKhanhMap != null) vinhKhanhMap.IsVisible = false;
                    }
                    catch { }

                    // Inform developer/user with actionable hints
                    try
                    {
                        await DisplayAlert("Bản đồ chưa tải", "Bản đồ gốc không hiển thị. Nguyên nhân thường gặp: API key Google Maps chưa hợp lệ hoặc chưa bật Maps SDK for Android; billing chưa kích hoạt; hoặc emulator không có Google Play Services. Đã chuyển sang chế độ WebView tạm thời.", "OK");
                    }
                    catch { }
                }
                else
                {
                    AddLog("Map rendered successfully.");
                }
            }
            catch (Exception ex)
            {
                AddLog($"CheckMapDisplay error: {ex.Message}");
            }
        }

        // ================== SEED DATA (DỮ LIỆU MẪU) ==================
        private async Task SeedFullData()
        {
            var existingPois = await _dbService.GetPoisAsync();
            // If there are any POIs we don't want to abort completely because the DB
            // might already contain some sample entries (e.g. only the bus stop).
            // Instead ensure each sample POI exists and insert only missing ones.

            // helper to check existence by name
            bool Exists(string name) => existingPois != null && existingPois.Any(p => p.Name == name);

            // Ốc Oanh
            PoiModel ocOanh;
            if (!Exists("Ốc Oanh 534"))
            {
                ocOanh = new PoiModel { Name = "Ốc Oanh 534", Category = "Food", Latitude = 10.7584, Longitude = 106.7058, ImageUrl = "ocoanh.jpg" };
                await _dbService.SavePoiAsync(ocOanh);
            }
            else
            {
                ocOanh = existingPois.First(p => p.Name == "Ốc Oanh 534");
            }
            // ensure contents for ocOanh
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = ocOanh.Id,
                LanguageCode = "vi",
                Title = "Ốc Oanh 534",
                Subtitle = "Quán ốc truyền thống",
                Description = "Quán ốc nổi tiếng nhất phố Vĩnh Khánh với món đặc sản ốc hương trứng muối.",
                PriceRange = "100k-200k",
                Rating = 4.8,
                OpeningHours = "10:00 - 22:00",
                PhoneNumber = "0123456789",
                Address = "Số 534, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                ShareUrl = "https://example.com/ocoanh"
            });
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = ocOanh.Id,
                LanguageCode = "en",
                Title = "Oc Oanh 534",
                Subtitle = "Traditional seafood eatery",
                Description = "A famous snail restaurant on Vinh Khanh street, known for its salted egg snails.",
                PriceRange = "100k-200k",
                Rating = 4.8,
                OpeningHours = "10:00 - 22:00",
                PhoneNumber = "0123456789",
                Address = "Số 534, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                ShareUrl = "https://example.com/ocoanh"
            });
            // Japanese
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = ocOanh.Id,
                LanguageCode = "ja",
                Title = "Ốc Oanh 534",
                Subtitle = "伝統的なシーフードレストラン",
                Description = "地元で人気のあるỐc Oanh。特製の塩漬け卵のスネールが有名です。",
                PriceRange = "100k-200k",
                Rating = 4.8,
                OpeningHours = "10:00 - 22:00",
                PhoneNumber = "0123456789",
                Address = "Số 534, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                ShareUrl = "https://example.com/ocoanh"
            });
            // Korean
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = ocOanh.Id,
                LanguageCode = "ko",
                Title = "Ốc Oanh 534",
                Subtitle = "전통 해산물 식당",
                Description = "이 지역에서 유명한 스네일 전문점。",
                PriceRange = "100k-200k",
                Rating = 4.8,
                OpeningHours = "10:00 - 22:00",
                PhoneNumber = "0123456789",
                Address = "Số 534, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                ShareUrl = "https://example.com/ocoanh"
            });

            // Ốc Vũ
            PoiModel ocVu;
            if (!Exists("Ốc Vũ"))
            {
                ocVu = new PoiModel { Name = "Ốc Vũ", Category = "Food", Latitude = 10.7578, Longitude = 106.7050, ImageUrl = "ocvu.jpg" };
                await _dbService.SavePoiAsync(ocVu);
            }
            else
            {
                ocVu = existingPois.First(p => p.Name == "Ốc Vũ");
            }
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = ocVu.Id,
                LanguageCode = "vi",
                Title = "Ốc Vũ",
                Subtitle = "Quán nước sốt đặc trưng",
                Description = "Ốc Vũ nổi tiếng với nước sốt đậm đà và món ốc móng tay xào rau muống thơm lừng.",
                PriceRange = "80k-150k",
                Rating = 4.6,
                OpeningHours = "11:00 - 21:30",
                PhoneNumber = "0123456789",
                Address = "Số 12, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                ShareUrl = "https://example.com/ocvu"
            });
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = ocVu.Id,
                LanguageCode = "en",
                Title = "Oc Vu",
                Subtitle = "Famous for its sauce",
                Description = "Oc Vu is known for its rich sauce and razor clams stir-fried with morning glory.",
                PriceRange = "80k-150k",
                Rating = 4.6,
                OpeningHours = "11:00 - 21:30",
                PhoneNumber = "0123456789",
                Address = "Số 12, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                ShareUrl = "https://example.com/ocvu"
            });
            // Japanese
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = ocVu.Id,
                LanguageCode = "ja",
                Title = "Ốc Vũ",
                Subtitle = "名物ソースの店",
                Description = "Ốc Vũは独特のソースで有名です。",
                PriceRange = "80k-150k",
                Rating = 4.6,
                OpeningHours = "11:00 - 21:30",
                PhoneNumber = "0123456789",
                Address = "Số 12, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                ShareUrl = "https://example.com/ocvu"
            });
            // Korean
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = ocVu.Id,
                LanguageCode = "ko",
                Title = "Ốc Vũ",
                Subtitle = "특제 소스로 유명",
                Description = "Ốc Vũ는 진한 소스로 알려져 있습니다.",
                PriceRange = "80k-150k",
                Rating = 4.6,
                OpeningHours = "11:00 - 21:30",
                PhoneNumber = "0123456789",
                Address = "Số 12, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                ShareUrl = "https://example.com/ocvu"
            });

            // Trạm xe buýt
            PoiModel bus;
            if (!Exists("Trạm Xe Buýt"))
            {
                bus = new PoiModel { Name = "Trạm Xe Buýt", Category = "BusStop", Latitude = 10.7570, Longitude = 106.7045, ImageUrl = "bus.jpg" };
                await _dbService.SavePoiAsync(bus);
            }
            else
            {
                bus = existingPois.First(p => p.Name == "Trạm Xe Buýt");
            }
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = bus.Id,
                LanguageCode = "vi",
                Title = "Trạm Xe Buýt",
                Subtitle = "Giao thông công cộng",
                Description = "Trạm dừng xe buýt thuận tiện để du khách di chuyển về hướng trung tâm hoặc Quận 7.",
                PriceRange = "",
                Rating = 0,
                OpeningHours = "",
                PhoneNumber = "0123456789",
                Address = "Số 5, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                ShareUrl = ""
            });
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = bus.Id,
                LanguageCode = "en",
                Title = "Bus Stop",
                Subtitle = "Public transport",
                Description = "A convenient bus stop for visitors to travel towards downtown or District 7.",
                PriceRange = "",
                Rating = 0,
                OpeningHours = "",
                PhoneNumber = "0123456789",
                Address = "Số 5, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                ShareUrl = ""
            });

            // ===== KHU DU LỊCH NỔI TIẾNG TRONG PHỐ ẨM THỰC VINH KHÁNH =====
            PoiModel dulich1;
            if (!Exists("Công Viên Vĩnh Khánh"))
            {
                dulich1 = new PoiModel { Name = "Công Viên Vĩnh Khánh", Category = "Attraction", Latitude = 10.7592, Longitude = 106.7065, ImageUrl = "dulich1.jpg" };
                await _dbService.SavePoiAsync(dulich1);
            }
            else
            {
                dulich1 = existingPois.First(p => p.Name == "Công Viên Vĩnh Khánh");
            }
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = dulich1.Id,
                LanguageCode = "vi",
                Title = "Công Viên Vĩnh Khánh",
                Subtitle = "Không gian xanh giữa phố ẩm thực",
                Description = "Công viên Vĩnh Khánh là điểm đến thư giãn ngay giữa khu ẩm thực, có lối đi bộ, ghế đá và nhiều cây xanh. Buổi sáng ở đây có không khí trong lành, người dân đi bộ tập thể dục và các hoạt động nhỏ dành cho gia đình. Buổi tối công viên lên đèn, nhiều gian hàng ăn vặt nhỏ và không gian âm nhạc nhẹ nhàng tạo nên trải nghiệm vui vẻ cho du khách.",
                PriceRange = "",
                Rating = 4.5,
                OpeningHours = "06:00 - 22:00",
                PhoneNumber = "0123456789",
                Address = "Số 2, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                ShareUrl = ""
            });
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = dulich1.Id,
                LanguageCode = "en",
                Title = "Vinh Khanh Park",
                Subtitle = "Green oasis in the food street",
                Description = "Vinh Khanh Park is a relaxing green space in the middle of the culinary quarter. It offers walking paths, benches and abundant trees. Mornings are fresh with locals exercising and families enjoying activities. Evenings light up with small street-food stalls and gentle music, creating a pleasant atmosphere for visitors.",
                PriceRange = "",
                Rating = 4.5,
                OpeningHours = "06:00 - 22:00",
                PhoneNumber = "0123456789",
                Address = "Số 2, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                ShareUrl = ""
            });

            PoiModel dulich2;
            if (!Exists("Nhà Truyền Thống Vĩnh Khánh"))
            {
                dulich2 = new PoiModel { Name = "Nhà Truyền Thống Vĩnh Khánh", Category = "Attraction", Latitude = 10.7572, Longitude = 106.7068, ImageUrl = "dulich2.jpg" };
                await _dbService.SavePoiAsync(dulich2);
            }
            else
            {
                dulich2 = existingPois.First(p => p.Name == "Nhà Truyền Thống Vĩnh Khánh");
            }
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = dulich2.Id,
                LanguageCode = "vi",
                Title = "Nhà Truyền Thống Vĩnh Khánh",
                Subtitle = "Bảo tàng văn hoá ẩm thực địa phương",
                Description = "Nhà Truyền Thống giới thiệu lịch sử và văn hoá ẩm thực của khu vực Vĩnh Khánh: từ những gánh hàng rong, công thức gia truyền đến những lễ hội đồ ăn đặc sắc. Trưng bày nhiều hiện vật, hình ảnh và bản đồ ẩm thực giúp khách hiểu sâu hơn về nguồn gốc các món ăn địa phương.",
                PriceRange = "",
                Rating = 4.7,
                OpeningHours = "09:00 - 18:00",
                PhoneNumber = "0123456789",
                Address = "Số 23, Hẻm 10, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                ShareUrl = ""
            });
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = dulich2.Id,
                LanguageCode = "en",
                Title = "Vinh Khanh Heritage House",
                Subtitle = "Local culinary culture museum",
                Description = "The Heritage House showcases the history and culinary culture of the Vinh Khanh area: from street vendors and family recipes to festive food traditions. Exhibits include artifacts, photos and a culinary map to help visitors understand the origins of local dishes.",
                PriceRange = "",
                Rating = 4.7,
                OpeningHours = "09:00 - 18:00",
                PhoneNumber = "0123456789",
                Address = "Số 23, Hẻm 10, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                ShareUrl = ""
            });
            // Japanese
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = dulich2.Id,
                LanguageCode = "ja",
                Title = "Vinh Khanh Heritage House",
                Subtitle = "地元の食文化博物館",
                Description = "地域の食文化と歴史を紹介する展示があります。",
                OpeningHours = "09:00 - 18:00",
                PhoneNumber = "+84 90 555 1234",
                Address = "23 Hẻm 10, Vĩnh Khánh, Quận 4",
                ShareUrl = ""
            });
            // Korean
            await _dbService.SaveContentAsync(new ContentModel {
                PoiId = dulich2.Id,
                LanguageCode = "ko",
                Title = "Vinh Khanh Heritage House",
                Subtitle = "지역 요리 문화 박물관",
                Description = "지역의 음식 문화と歴史を紹介합니다。",
                OpeningHours = "09:00 - 18:00",
                PhoneNumber = "+84 90 555 1234",
                Address = "23 Alley 10, Vinh Khanh, Dist.4",
                ShareUrl = ""
            });

            // ===== THÊM 6 QUÁN ĂN / NHÀ HÀNG KHÁC (KHÔNG LÀ QUÁN ỐC) =====
            var restaurants = new List<(string name, double lat, double lng, string img, string viTitle, string enTitle)>
            {
                ("Nhà Hàng Làng Xưa", 10.7580, 106.7055, "quan1.jpg", "Nhà Hàng Làng Xưa", "Lang Xua Restaurant"),
                ("Nhà Hàng Bếp Quê", 10.7582, 106.7060, "quan2.jpg", "Nhà Hàng Bếp Quê", "Home Kitchen"),
                ("Quán Ăn Sài Gòn Ngon", 10.7575, 106.7052, "quan3.jpg", "Quán Ăn Sài Gòn Ngon", "Saigon Delights"),
                ("Nhà Hàng Hương Xưa", 10.7576, 106.7062, "quan4.jpg", "Nhà Hàng Hương Xưa", "Huong Xua Restaurant"),
                ("Quán Cơm Bình Dân Kim", 10.7579, 106.7048, "quan5.jpg", "Quán Cơm Bình Dân Kim", "Kim's Local Rice"),
                ("Nhà Hàng Hải Sản Phố", 10.7581, 106.7049, "quan6.jpg", "Nhà Hàng Hải Sản Phố", "Seafood Street"),
            };

            foreach (var r in restaurants)
            {
                PoiModel rest;
                if (!Exists(r.name))
                {
                    rest = new PoiModel { Name = r.name, Category = "Restaurant", Latitude = r.lat, Longitude = r.lng, ImageUrl = r.img };
                    await _dbService.SavePoiAsync(rest);
                }
                else
                {
                    rest = existingPois.First(p => p.Name == r.name);
                }

                // Vietnamese content (long description)
                await _dbService.SaveContentAsync(new ContentModel {
                    PoiId = rest.Id,
                    LanguageCode = "vi",
                    Title = r.viTitle,
                    Subtitle = "Ẩm thực truyền thống và đặc sản địa phương",
                    Description = "Đây là một quán ăn tiêu biểu trong khu ẩm thực, phục vụ nhiều món ăn truyền thống với hương vị đậm đà. Quán nổi tiếng nhờ sử dụng nguyên liệu tươi, nước dùng ninh kỹ và gia vị gia truyền. Không gian quán ấm cúng, phù hợp cho gia đình và nhóm bạn. Khách thường nhận xét về sự chu đáo của phục vụ và tỉ mỉ trong trình bày món ăn. Món ăn kèm theo rau thơm và nước chấm đặc trưng, tạo nên trải nghiệm ẩm thực trọn vẹn.",
                    PriceRange = "50k-200k",
                    Rating = 4.4,
                    OpeningHours = "10:00 - 22:00",
                    PhoneNumber = "0123456789",
                    Address = "Số 10, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                    ShareUrl = ""
                });

                // English content (long description)
                await _dbService.SaveContentAsync(new ContentModel {
                    PoiId = rest.Id,
                    LanguageCode = "en",
                    Title = r.enTitle,
                    Subtitle = "Traditional flavors and local specialties",
                    Description = "This restaurant is a typical eatery in the culinary quarter, serving a wide range of traditional dishes with rich flavors. It is known for fresh ingredients, carefully prepared broths and family spice recipes. The cozy atmosphere is suitable for families and groups. Guests often praise the attentive service and the thoughtful presentation of dishes. Meals are served with fresh herbs and a signature dipping sauce, creating a satisfying dining experience.",
                    PriceRange = "50k-200k",
                    Rating = 4.3,
                    OpeningHours = "10:00 - 22:00",
                    PhoneNumber = "0123456789",
                    Address = "Số 10, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                    ShareUrl = ""
                });
                // Japanese
                await _dbService.SaveContentAsync(new ContentModel {
                    PoiId = rest.Id,
                    LanguageCode = "ja",
                    Title = r.viTitle,
                    Subtitle = "地元の伝統料理",
                    Description = "伝統的な味を提供するレストランです。",
                    PriceRange = "50k-200k",
                    Rating = 4.3,
                    OpeningHours = "10:00 - 22:00",
                    PhoneNumber = "0123456789",
                    Address = "Số 10, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                    ShareUrl = ""
                });
                // Korean
                await _dbService.SaveContentAsync(new ContentModel {
                    PoiId = rest.Id,
                    LanguageCode = "ko",
                    Title = r.viTitle,
                    Subtitle = "현지 전통 요리",
                    Description = "전통적인 맛을 제공하는 식당입니다.",
                    PriceRange = "50k-200k",
                    Rating = 4.3,
                    OpeningHours = "10:00 - 22:00",
                    PhoneNumber = "0123456789",
                    Address = "Số 10, Đường Vĩnh Khánh, Phường 12, Quận 4, TP.HCM",
                    ShareUrl = ""
                });
            }
        }

        // ================== THEO DÕI GPS (GEOFENCING) ==================
        private async Task StartProximityTracking()
        {
            while (_isTrackingActive && Shell.Current.CurrentPage is MapPage)
            {
                try
                {
                    var userLocation = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));
                    if (userLocation != null)
                    {
                        _lastLocation = userLocation; // Update last known location
                        // feed location to engine (POC)
                        _geofenceEngine?.ProcessLocation(userLocation.Latitude, userLocation.Longitude);
                        // highlight nearest POI to user
                        await HighlightNearestPoi(userLocation.Latitude, userLocation.Longitude);
                    }
                }
                catch { }
                await Task.Delay(5000); // Kiểm tra lại sau mỗi 5 giây
            }
        }

        // Handle geofence engine triggers
        private async void OnPoiTriggered(object sender, PoiTriggeredEventArgs e)
        {
            try
            {
                if (e?.Poi == null) return;

                // If popup already open for the same POI, avoid reopening but ensure narration plays
                if (PoiDetailPanel != null && PoiDetailPanel.IsVisible && _selectedPoi != null && _selectedPoi.Id == e.Poi.Id)
                {
                    var c2 = await GetContentForLanguageAsync(e.Poi.Id, _currentLanguage);
                    if (c2 != null) await PlayNarration(c2.Description);
                    return;
                }

                // set selected poi, show popup and autoplay narration
                _selectedPoi = e.Poi;
                await ShowPoiDetail(e.Poi);
                var content = await GetContentForLanguageAsync(e.Poi.Id, _currentLanguage);
                if (content != null)
                {
                    await PlayNarration(content.Description);
                }
            }
            catch { }
        }

        // Haversine formula helpers (duplicate of GeofenceEngine for convenience)
        private static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            double R = 6371000; // meters
            double dLat = ToRadians(lat2 - lat1);
            double dLon = ToRadians(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double ToRadians(double deg) => deg * (Math.PI / 180.0);

        // ================== HIỂN THỊ MAP & POPUP ==================
        private async Task DisplayAllPois()
        {
            vinhKhanhMap.Pins.Clear();

            var poisToShow = _pois;
            // If DB returned no POIs, fall back to sample POIs so user can see pins (helps debugging/emulator)
            if (poisToShow == null || !poisToShow.Any())
            {
                Console.WriteLine("No POIs in DB - using sample POIs for display");
                poisToShow = new List<PoiModel>
                {
                    new PoiModel { Id = -1, Name = "Ốc Oanh 534", Category = "Food", Latitude = 10.7584, Longitude = 106.7058, ImageUrl = "ocoanh.jpg" },
                    new PoiModel { Id = -2, Name = "Ốc Vũ", Category = "Food", Latitude = 10.7578, Longitude = 106.7050, ImageUrl = "ocvu.jpg" },
                    new PoiModel { Id = -3, Name = "Trạm Xe Buýt", Category = "BusStop", Latitude = 10.7570, Longitude = 106.7045, ImageUrl = "bus.jpg" }
                };
            }

            foreach (var poi in poisToShow)
            {
                var currentPoi = poi; // avoid closure issues when awaiting inside loop

                // try get localized title for pin label (if poi from DB has valid positive Id)
                string label = currentPoi.Name;
                try
                {
                    if (currentPoi.Id > 0)
                    {
                        var content = await GetContentForLanguageAsync(currentPoi.Id, _currentLanguage);
                        if (content != null && !string.IsNullOrEmpty(content.Title)) label = content.Title;
                    }
                }
                catch { }

                var pin = new Pin
                {
                    Label = label,
                    Location = new Location(currentPoi.Latitude, currentPoi.Longitude),
                    Type = currentPoi.Category == "BusStop" ? PinType.SearchResult : PinType.Place
                };

                pin.MarkerClicked += async (s, e) =>
                {
                    _selectedPoi = currentPoi;
                    await ShowPoiDetail(currentPoi);
                };

                vinhKhanhMap.Pins.Add(pin);
            }

            // update highlight for nearest POI relative to current center/user location
            await HighlightNearestPoi();
        }

        private async Task ShowPoiDetail(PoiModel poi)
        {
            // Title pref: content.Title if available, otherwise poi.Name
            var content = await GetContentForLanguageAsync(poi.Id, _currentLanguage);
            LblPoiName.Text = content?.Title ?? poi.Name;
            // Address & phone: if current language content missing these fields, fall back to Vietnamese content
            try
            {
                var phone = content?.PhoneNumber;
                var addr = content?.Address;
                if (string.IsNullOrEmpty(phone) || string.IsNullOrEmpty(addr))
                {
                    // try Vietnamese source
                    var vi = await _dbService.GetContentByPoiIdAsync(poi.Id, "vi");
                    if (vi != null)
                    {
                        if (string.IsNullOrEmpty(phone)) phone = vi.PhoneNumber;
                        if (string.IsNullOrEmpty(addr)) addr = vi.Address;
                    }
                }

                if (LblAddress != null) LblAddress.Text = addr ?? string.Empty;
                if (LblPhone != null) LblPhone.Text = phone ?? string.Empty;
            }
            catch { }
            try
            {
                // Populate carousel with up to 5 images (fallback to placeholder)
                var images = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(poi.ImageUrl)) images.Add(poi.ImageUrl);
                // (If you later add multiple image URLs on POI or content, append here)
                if (!images.Any()) images.Add("store_placeholder.png");
                try { ImgCarousel.ItemsSource = images; ImgCarousel.Position = 0; } catch { }
            }
            catch { }
            // Subtitle and description
            if (LblSubtitle != null) LblSubtitle.Text = content?.Subtitle ?? string.Empty;
            if (LblDescription != null) LblDescription.Text = content?.Description ?? (_currentLanguage == "en" ? "No description available." : _currentLanguage == "ja" ? "説明はありません。" : _currentLanguage == "ko" ? "설명이 없습니다." : "Chưa có mô tả cho địa điểm này.");

            // Optional metadata
            try { var _lblRating = this.FindByName<Label>("LblRating"); if (_lblRating != null) _lblRating.Text = content != null && content.Rating > 0 ? $"{content.Rating:0.0} ★" : string.Empty; } catch { }
            try { var _lblPrice = this.FindByName<Label>("LblPrice"); if (_lblPrice != null) _lblPrice.Text = !string.IsNullOrEmpty(content?.PriceRange) ? content.PriceRange : string.Empty; } catch { }
            try { if (LblOpeningHours != null) LblOpeningHours.Text = !string.IsNullOrEmpty(content?.OpeningHours) ? content.OpeningHours : string.Empty; } catch { }

            // Compute open/closed status from OpeningHours (expected format "HH:mm - HH:mm")
            try
            {
                if (LblOpeningHours != null && LblOpenStatus != null && !string.IsNullOrEmpty(LblOpeningHours.Text))
                {
                    var txt = LblOpeningHours.Text;
                    var parts = txt.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                    if (parts.Length == 2)
                    {
                        if (TimeSpan.TryParse(parts[0], out var start) && TimeSpan.TryParse(parts[1], out var end))
                        {
                            var now = DateTime.Now.TimeOfDay;
                            bool open;
                            if (start <= end)
                                open = now >= start && now <= end;
                            else
                                open = now >= start || now <= end; // overnight

                            LblOpenStatus.Text = open ? (_currentLanguage == "en" ? "Open now" : "Đang mở cửa") : (_currentLanguage == "en" ? "Closed" : "Đóng cửa");
                            LblOpenStatus.TextColor = open ? Microsoft.Maui.Graphics.Color.FromArgb("#388E3C") : Microsoft.Maui.Graphics.Color.FromArgb("#D32F2F");
                        }
                    }
                }
            }
            catch { }

            // Reviews count (if available in content) - not present in model, leave blank or add when available
            try { var _lblRev = this.FindByName<Label>("LblReviewCount"); if (_lblRev != null) _lblRev.Text = string.Empty; } catch { }

            // Update action button visuals (frames and icons)
            try
            {
                var frameSave = this.FindByName<Frame>("FrameSave");
                var frameShare = this.FindByName<Frame>("FrameShare");
                var frameDir = this.FindByName<Frame>("FrameDirections");
                var frameNarr = this.FindByName<Frame>("FrameNarration");

                // Distinguish directions with blue accent; narration neutral
                if (frameDir != null) frameDir.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8");
                if (frameNarr != null) frameNarr.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF");

                // share & save default light background
                if (frameShare != null) frameShare.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#E0F2F1");
                if (frameSave != null)
                {
                    if (poi.IsSaved)
                        frameSave.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#BDBDBD");
                    else
                        frameSave.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#E0F2F1");
                }
            }
            catch { }

            PoiDetailPanel.IsVisible = true;
            // Reset translated position and description state when showing
            try
            {
                PoiDetailPanel.TranslationY = 0;
                _isDescriptionExpanded = false;
                if (LblDescription != null) LblDescription.MaxLines = 3;
                var _btnToggle = this.FindByName<Button>("BtnToggleDescription");
                if (_btnToggle != null)
                {
                    // show toggle only for long descriptions
                    var desc = content?.Description ?? string.Empty;
                    if (desc.Length > 180)
                    {
                        _btnToggle.IsVisible = true;
                        _btnToggle.Text = _currentLanguage == "en" ? "Read more" : "Xem thêm";
                    }
                    else
                    {
                        _btnToggle.IsVisible = false;
                    }
                }
            }
            catch { }
             vinhKhanhMap.MoveToRegion(MapSpan.FromCenterAndRadius(new Location(poi.Latitude, poi.Longitude), Distance.FromKilometers(0.1)));
        }

        // Helper to show list of highlights in a simple page (fallback if HighlightsListPage not present)
        private async Task ShowHighlightsListFallback(System.Collections.Generic.List<PoiModel> list)
        {
            try
            {
                // Build a simple action sheet with names if navigation to a page fails
                var names = list.Select(p => p.Name).Take(10).ToArray();
                var choice = await DisplayActionSheet("Địa điểm thịnh hành", "Đóng", null, names);
                if (!string.IsNullOrEmpty(choice) && choice != "Đóng")
                {
                    var poi = list.FirstOrDefault(p => p.Name == choice);
                    if (poi != null)
                    {
                        _selectedPoi = poi;
                        await ShowPoiDetail(poi);
                    }
                }
            }
            catch { }
        }

        // Handle pan gesture on POI detail panel to allow pulling down to collapse
        public void OnPoiDetailPanUpdated(object sender, PanUpdatedEventArgs e)
        {
            try
            {
                if (PoiDetailPanel == null) return;

                switch (e.StatusType)
                {
                    case GestureStatus.Started:
                        _poiStartTranslationY = PoiDetailPanel.TranslationY;
                        break;
                    case GestureStatus.Running:
                        var newY = _poiStartTranslationY + e.TotalY;
                        // allow dragging up (negative) and down (positive)
                        if (newY < -_poiExpandDistance) newY = -_poiExpandDistance;
                        if (newY > _poiCollapseDistance) newY = _poiCollapseDistance;
                        PoiDetailPanel.TranslationY = newY;
                        break;
                    case GestureStatus.Completed:
                    case GestureStatus.Canceled:
                        // decide final state based on how far it was dragged
                        var current = PoiDetailPanel.TranslationY;
                        if (current <= -(_poiExpandDistance / 2))
                        {
                            // expand up
                            _ = PoiDetailPanel.TranslateTo(0, -_poiExpandDistance, 200, Easing.CubicOut);
                        }
                        else if (current >= _poiCollapseDistance / 2)
                        {
                            // collapse down
                            _ = PoiDetailPanel.TranslateTo(0, _poiCollapseDistance, 200, Easing.CubicOut);
                        }
                        else
                        {
                            // snap back to default
                            _ = PoiDetailPanel.TranslateTo(0, 0, 200, Easing.CubicOut);
                        }
                        break;
                }
            }
            catch { }
        }

        // Toggle long description between collapsed and expanded
        private void OnToggleDescriptionClicked(object sender, EventArgs e)
        {
            try
            {
                _isDescriptionExpanded = !_isDescriptionExpanded;
                if (_isDescriptionExpanded)
                {
                    if (LblDescription != null) LblDescription.MaxLines = int.MaxValue;
                    var _btnToggle2 = this.FindByName<Button>("BtnToggleDescription");
                    if (_btnToggle2 != null) _btnToggle2.Text = _currentLanguage == "en" ? "Show less" : "Rút gọn";
                }
                else
                {
                    if (LblDescription != null) LblDescription.MaxLines = 3;
                    var _btnToggle3 = this.FindByName<Button>("BtnToggleDescription");
                    if (_btnToggle3 != null) _btnToggle3.Text = _currentLanguage == "en" ? "Read more" : "Xem thêm";
                }
            }
            catch { }
        }

        // Highlight nearest POI by updating pin labels (append " (Near)")
        private async Task HighlightNearestPoi(double? userLat = null, double? userLng = null)
        {
            try
            {
                if (_pois == null || !_pois.Any() || vinhKhanhMap == null) return;

                double lat = userLat ?? vinhKhanhMap.VisibleRegion?.Center?.Latitude ?? double.NaN;
                double lng = userLng ?? vinhKhanhMap.VisibleRegion?.Center?.Longitude ?? double.NaN;
                if (double.IsNaN(lat) || double.IsNaN(lng)) return;

                // find nearest poi
                PoiModel nearest = null;
                double best = double.MaxValue;
                foreach (var p in _pois)
                {
                    var d = HaversineDistanceMeters(lat, lng, p.Latitude, p.Longitude);
                    if (d < best)
                    {
                        best = d; nearest = p;
                    }
                }

                // update pin labels
                foreach (var pin in vinhKhanhMap.Pins.ToList())
                {
                    try
                    {
                        // restore original title from DB if possible
                        var match = _pois.FirstOrDefault(x => Math.Abs(x.Latitude - pin.Location.Latitude) < 0.00001 && Math.Abs(x.Longitude - pin.Location.Longitude) < 0.00001);
                        if (match != null)
                        {
                            var title = match.Name;
                            var content = await _dbService.GetContentByPoiIdAsync(match.Id, _currentLanguage);
                            if (content != null && !string.IsNullOrEmpty(content.Title)) title = content.Title;
                            if (nearest != null && match.Id == nearest.Id)
                                pin.Label = title + " (Near)";
                            else
                                pin.Label = title;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void CenterMapOnVinhKhanh()
        {
            vinhKhanhMap.MoveToRegion(MapSpan.FromCenterAndRadius(new Location(10.7584, 106.7058), Distance.FromKilometers(0.4)));
        }

        // ================== CÁC NÚT BẤM UI ==================
        private async void OnMyLocationClicked(object sender, EventArgs e)
        {
            try
            {
                var location = await Geolocation.Default.GetLocationAsync();
                if (location != null)
                     vinhKhanhMap.MoveToRegion(MapSpan.FromCenterAndRadius(location, Distance.FromKilometers(0.15)));
            }
            catch
            {
                await DisplayAlert("Lỗi", "Vui lòng bật định vị GPS", "OK");
            }
        }


        // Make handler public so XAML loader can find it reliably
        public void OnMenuClicked(object sender, EventArgs e)
        {
            // If a POI detail is open, close it when opening the language menu
            if (PoiDetailPanel != null && PoiDetailPanel.IsVisible)
                PoiDetailPanel.IsVisible = false;

            LanguagePanel.IsVisible = true;
            // Update the visual state of language buttons
            UpdateLanguageSelectionUI();
        }

        private void OnCloseMenuClicked(object sender, EventArgs e)
        {
            LanguagePanel.IsVisible = false;
        }

        // Tabs click handlers
        private void OnTabOverviewClicked(object sender, EventArgs e)
        {
            try
            {
                var overview = this.FindByName<VisualElement>("OverviewPanel");
                var intro = this.FindByName<VisualElement>("IntroPanel");
                var tabO = this.FindByName<Button>("TabOverview");
                var tabI = this.FindByName<Button>("TabIntro");
                if (overview != null) overview.IsVisible = true;
                if (intro != null) intro.IsVisible = false;
                if (tabO != null) tabO.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#00796B");
                if (tabI != null) tabI.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                if (tabO != null) tabO.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#E3F2FD");
                if (tabI != null) tabI.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent");
            }
            catch { }
        }

        private void OnTabIntroClicked(object sender, EventArgs e)
        {
            try
            {
                var overview = this.FindByName<VisualElement>("OverviewPanel");
                var intro = this.FindByName<VisualElement>("IntroPanel");
                var tabO = this.FindByName<Button>("TabOverview");
                var tabI = this.FindByName<Button>("TabIntro");
                if (overview != null) overview.IsVisible = false;
                if (intro != null) intro.IsVisible = true;
                if (tabO != null) tabO.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                if (tabI != null) tabI.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#00796B");
                if (tabI != null) tabI.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#E3F2FD");
                if (tabO != null) tabO.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent");
            }
            catch { }
        }

        private async void OnCloseDetailClicked(object sender, EventArgs e)
        {
            try
            {
                // stop any ongoing narration immediately when closing the POI detail
                try { if (_audioQueue != null) await _audioQueue.StopAsync(); else _narrationService?.Stop(); } catch { }
                // also clear local speaking flag so user can start again
                _isSpeaking = false;
            }
            catch { }

            PoiDetailPanel.IsVisible = false;
        }

        private async void OnStartNarrationClicked(object sender, EventArgs e)
        {
            if (_selectedPoi == null) return;
            var content = await GetContentForLanguageAsync(_selectedPoi.Id, _currentLanguage);
            if (content != null)
            {
                await PlayNarration(content.Description);
                // Post a trace with duration unknown (will be set on stop)
                try
                {
                    var trace = new VinhKhanh.Shared.TraceLog
                    {
                        PoiId = _selectedPoi.Id,
                        Latitude = _lastLocation?.Latitude ?? 0,
                        Longitude = _lastLocation?.Longitude ?? 0,
                        DeviceId = null,
                        ExtraJson = "{\"event\":\"play\"}",
                        DurationSeconds = null
                    };
                    _ = _apiService?.PostTraceAsync(trace);
                }
                catch { }
            }
        }

        private async void OnGetDirectionsClicked(object sender, EventArgs e)
        {
            if (_selectedPoi == null)
            {
                await DisplayAlert("Lỗi", "Chưa chọn điểm để dẫn đường", "OK");
                return;
            }

            var lat = _selectedPoi.Latitude;
            var lng = _selectedPoi.Longitude;
            var label = Uri.EscapeDataString(_selectedPoi.Name ?? "Destination");

            string uri = null;
            try
            {
                if (DeviceInfo.Platform == DevicePlatform.iOS)
                {
                    uri = $"http://maps.apple.com/?daddr={lat},{lng}";
                }
                else if (DeviceInfo.Platform == DevicePlatform.Android)
                {
                    uri = $"geo:{lat},{lng}?q={label}";
                }
                else
                {
                    // Fallback to Google Maps web
                    uri = $"https://www.google.com/maps/dir/?api=1&destination={lat},{lng}";
                }

                await Launcher.OpenAsync(new Uri(uri));
            }
            catch (Exception ex)
            {
                // fallback: open google maps web
                try
                {
                    var web = $"https://www.google.com/maps/dir/?api=1&destination={lat},{lng}";
                    await Launcher.OpenAsync(new Uri(web));
                }
                catch { }
            }
        }

        private async void OnShareClicked(object sender, EventArgs e)
        {
            if (_selectedPoi == null) return;
            var content = await _dbService.GetContentByPoiIdAsync(_selectedPoi.Id, _currentLanguage);
            var shareText = content?.ShareUrl ?? content?.Description ?? _selectedPoi.Name;
            try
            {
                await Share.RequestAsync(new ShareTextRequest
                {
                    Title = content?.Title ?? _selectedPoi.Name,
                    Text = shareText,
                    Uri = content?.ShareUrl
                });
            }
            catch { }
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            if (_selectedPoi == null) return;
            try
            {
                // toggle saved state
                _selectedPoi.IsSaved = !_selectedPoi.IsSaved;
                await _dbService.SavePoiAsync(_selectedPoi);

                // refresh list and UI
                _pois = await _dbService.GetPoisAsync();
                BtnShowSaved.IsVisible = _pois.Any(p => p.IsSaved);
                if (_selectedPoi != null)
                    await ShowPoiDetail(_selectedPoi);
            }
            catch { }
        }

        private async void OnShowSavedClicked(object sender, EventArgs e)
        {
            try
            {
                _pois = await _dbService.GetPoisAsync();
                var saved = _pois.Where(p => p.IsSaved).ToList();
                if (!saved.Any())
                {
                    BtnShowSaved.IsVisible = false;
                    return;
                }

                var names = saved.Select(p => p.Name).ToArray();
                var choice = await DisplayActionSheet("Địa điểm đã lưu", "Hủy", null, names);
                if (!string.IsNullOrEmpty(choice) && choice != "Hủy")
                {
                    var poi = saved.FirstOrDefault(p => p.Name == choice);
                    if (poi != null)
                    {
                        _selectedPoi = poi;
                        await ShowPoiDetail(poi);
                    }
                }
            }
            catch { }
        }



        private async void OnScanCameraClicked(object sender, EventArgs e) => await Navigation.PushAsync(new ScanPage());

        private async void OnQrClicked(object sender, EventArgs e)
        {
            if (_selectedPoi == null) return;
            try
            {
                // Open scanner page and auto-play this POI's narration
                await Navigation.PushAsync(new ScanPage(_currentLanguage, _selectedPoi.Id));
            }
            catch { }
        }

        // ================== THUYẾT MINH (TTS) ==================
        private Task PlayNarration(string text)
        {
            // Use AudioQueueService to manage TTS and audio items (prevents duplicates, handles priority)
            try
            {
                var key = _selectedPoi != null ? $"poi:{_selectedPoi.Id}:{_currentLanguage}" : (text?.GetHashCode().ToString() ?? Guid.NewGuid().ToString());
                var item = new AudioItem
                {
                    Key = key,
                    IsTts = true,
                    Language = _currentLanguage,
                    Text = text,
                    PoiId = _selectedPoi?.Id ?? 0,
                    Priority = _selectedPoi?.Priority ?? 0
                };

                _audioQueue?.Enqueue(item); // Enqueue narration item
                // Track analytics: send trace that user played this POI
                try // Attempt to log analytics trace
                {
                    var trace = new VinhKhanh.Shared.TraceLog // Create trace log
                    {
                        PoiId = item.PoiId,
                        Latitude = _lastLocation?.Latitude ?? 0,
                        Longitude = _lastLocation?.Longitude ?? 0,
                        ExtraJson = "{\"event\":\"play\"}",
                        DurationSeconds = null
                    };
                    _ = _apiService?.PostTraceAsync(trace); // Post trace asynchronously
                }
                catch { }
            }
            catch { }

            return Task.CompletedTask;
        }

        // Language selection handlers
        private async void OnSelectVietnameseClicked(object sender, EventArgs e)
        {
            _currentLanguage = "vi";
            // Do not auto-close the menu: user will close manually
            UpdateLanguageSelectionUI();
            UpdateUiStrings();
            // refresh pins
            await DisplayAllPois();
        }

        private async void OnSelectEnglishClicked(object sender, EventArgs e)
        {
            _currentLanguage = "en";
            // Do not auto-close the menu: user will close manually
            UpdateLanguageSelectionUI();
            UpdateUiStrings();
            // refresh pins to use english labels
            await DisplayAllPois();
        }


        // Update visual state of language buttons in the menu
        private void UpdateLanguageSelectionUI()
        {
            try
            {
                // Ensure language buttons exist
                if (BtnLangVI == null || BtnLangEN == null || BtnLangJA == null || BtnLangKO == null) return;

                // reset all
                BtnLangVI.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangVI.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                BtnLangEN.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangEN.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                BtnLangJA.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangJA.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                BtnLangKO.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangKO.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");

                // set selected
                switch (_currentLanguage)
                {
                    case "vi":
                        BtnLangVI.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangVI.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF");
                        break;
                    case "en":
                        BtnLangEN.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangEN.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF");
                        break;
                    case "ja":
                        BtnLangJA.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangJA.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF");
                        break;
                    case "ko":
                        BtnLangKO.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangKO.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF");
                        break;
                }
            }
            catch { }
        }

        private async void OnConfirmLanguageClicked(object sender, EventArgs e)
        {
            try
            {
                // save preference and close
                Preferences.Default.Set("lang_seen", true);
                LanguagePanel.IsVisible = false;
                await DisplayAllPois();
            }
            catch { }
        }

        // Update static UI text strings according to current language
        private void UpdateUiStrings()
        {
            try
            {
                if (TabOverview != null && TabIntro != null)
                {
                    switch (_currentLanguage)
                    {
                        case "en":
                            TabOverview.Text = "Overview"; TabIntro.Text = "Intro"; break;
                        case "ja":
                            TabOverview.Text = "概要"; TabIntro.Text = "紹介"; break;
                        case "ko":
                            TabOverview.Text = "개요"; TabIntro.Text = "소개"; break;
                        default:
                            TabOverview.Text = "Tổng quan"; TabIntro.Text = "Giới thiệu"; break;
                    }
                }

                var btnToggle = this.FindByName<Button>("BtnToggleDescription");
                if (btnToggle != null)
                {
                    if (_currentLanguage == "en") btnToggle.Text = "Read more";
                    else if (_currentLanguage == "ja") btnToggle.Text = "続きを読む";
                    else if (_currentLanguage == "ko") btnToggle.Text = "더보기";
                    else btnToggle.Text = "Xem thêm";
                }

                // Action labels
                var lbDir = this.FindByName<Label>("LblActDirections"); if (lbDir != null) lbDir.Text = _currentLanguage == "en" ? "Directions" : _currentLanguage == "ja" ? "道順" : _currentLanguage == "ko" ? "길찾기" : "Dẫn đường";
                var lbNarr = this.FindByName<Label>("LblActNarration"); if (lbNarr != null) lbNarr.Text = _currentLanguage == "en" ? "Narration" : _currentLanguage == "ja" ? "音声案内" : _currentLanguage == "ko" ? "해설" : "Thuyết minh";
                var lbShare = this.FindByName<Label>("LblActShare"); if (lbShare != null) lbShare.Text = _currentLanguage == "en" ? "Share" : _currentLanguage == "ja" ? "共有" : _currentLanguage == "ko" ? "공유" : "Chia sẻ";
                var lbSave = this.FindByName<Label>("LblActSave"); if (lbSave != null) lbSave.Text = _currentLanguage == "en" ? "Save" : _currentLanguage == "ja" ? "保存" : _currentLanguage == "ko" ? "저장" : "Lưu";

                // Search placeholder and language menu labels
                var ph = this.FindByName<Label>("LblSearchPlaceholder"); if (ph != null) ph.Text = _currentLanguage == "en" ? "Search..." : _currentLanguage == "ja" ? "検索..." : _currentLanguage == "ko" ? "검색..." : "Tìm kiếm...";
                var langTitle = this.FindByName<Label>("LblLangTitle"); if (langTitle != null) langTitle.Text = _currentLanguage == "en" ? "⚙️ Language settings" : _currentLanguage == "ja" ? "⚙️ 言語設定" : _currentLanguage == "ko" ? "⚙️ 언어 설정" : "⚙️ Cài đặt ngôn ngữ";
                var btnClose = this.FindByName<Button>("BtnCloseMenu"); if (btnClose != null) btnClose.Text = _currentLanguage == "en" ? "Close" : _currentLanguage == "ja" ? "閉じる" : _currentLanguage == "ko" ? "닫기" : "Đóng";
            }
            catch { }
        }

        protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
        {
            base.OnNavigatedFrom(args);
            _isTrackingActive = false;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            CenterMapOnVinhKhanh();
            try
            {
                // Nạp dữ liệu mẫu nếu máy chưa có
                await SeedFullData();
                _pois = await _dbService.GetPoisAsync();
                // Hiển thị POI lên bản đồ
                AddPoisToMap();
                // Show 'show saved' floating button when at least one POI is saved
                try { BtnShowSaved.IsVisible = _pois.Any(p => p.IsSaved); } catch { }
                await DisplayAllPois();
                // Populate highlights when no POI selected
                try
                {
                var highlights = _pois.OrderByDescending(p => p.Priority).Take(6).ToList();
                    var vmColl = new System.Collections.ObjectModel.ObservableCollection<VinhKhanh.Shared.HighlightViewModel>();
                    foreach (var h in highlights)
                    {
                        var content = await _dbService.GetContentByPoiIdAsync(h.Id, _currentLanguage) ?? await _dbService.GetContentByPoiIdAsync(h.Id, "vi");
                        var openStatus = "";
                        var openColorHex = "#9E9E9E"; // default gray
                        if (content != null && !string.IsNullOrEmpty(content.OpeningHours))
                        {
                            var parts = content.OpeningHours.Split('-', System.StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                            if (parts.Length == 2 && System.TimeSpan.TryParse(parts[0], out var s) && System.TimeSpan.TryParse(parts[1], out var e))
                            {
                                var now = System.DateTime.Now.TimeOfDay;
                                bool isOpen = s <= e ? now >= s && now <= e : now >= s || now <= e;
                                openStatus = isOpen ? (_currentLanguage == "en" ? "Open now" : "Đang mở cửa") : (_currentLanguage == "en" ? "Closed" : "Đóng cửa");
                                openColorHex = isOpen ? "#388E3C" : "#D32F2F";
                            }
                        }

                            var vm = new VinhKhanh.Shared.HighlightViewModel
                        {
                            Poi = h,
                            ImageUrl = h.ImageUrl,
                            Name = h.Name,
                            Category = h.Category,
                            RatingDisplay = content != null ? (content.Rating > 0 ? string.Format("{0:0.0} ★", content.Rating) : string.Empty) : string.Empty,
                            ReviewCount = 0,
                            OpeningHours = content?.OpeningHours ?? string.Empty,
                            OpenStatus = openStatus,
                            OpenStatusColorHex = openColorHex
                        };
                        vmColl.Add(vm);
                    }

                    CvHighlights.ItemsSource = vmColl;
                    HighlightsPanel.IsVisible = vmColl.Any();
                }
                catch { }
                // update engine with current POIs
                _geofenceEngine?.UpdatePois(_pois);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi load dữ liệu: {ex.Message}");
            }
        }

        private void AddPoisToMap()
        {
            try
            {
                vinhKhanhMap.Pins.Clear();
                foreach (var poi in _pois)
                {
                    var pin = new Microsoft.Maui.Controls.Maps.Pin
                    {
                        Label = poi.Name,
                        Address = poi.Category,
                        Location = new Microsoft.Maui.Devices.Sensors.Location(poi.Latitude, poi.Longitude),
                        Type = Microsoft.Maui.Controls.Maps.PinType.Place
                    };
                    pin.MarkerClicked += OnPinClicked;
                    vinhKhanhMap.Pins.Add(pin);
                }
            }
            catch { }
        }

        private async void OnPinClicked(object sender, Microsoft.Maui.Controls.Maps.PinClickedEventArgs e)
        {
            var pin = sender as Microsoft.Maui.Controls.Maps.Pin;
            var poi = _pois.FirstOrDefault(p => p.Name == pin.Label && Math.Abs(p.Latitude - pin.Location.Latitude) < 0.0001);
            if (poi != null)
            {
                await ShowPoiDetail(poi);
            }
        }

        // Existing async ShowPoiDetail is defined earlier; remove this duplicate sync overload.
    }
}