using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Threading;
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
        private readonly RealtimeSyncManager _realtimeSyncManager;
        private readonly IMapOfflinePackService _mapOfflinePackService;
        private List<PoiModel> _pois = new();
        private bool _isSpeaking = false;
        private bool _isTrackingActive = false;
        private PoiModel _selectedPoi;
        // Drag state for POI detail panel
        private double _poiStartTranslationY = 0;
        private readonly double _poiCollapseDistance = 420; // max distance to drag down
        private readonly double _poiExpandDistance = 420; // max distance to drag up
        private bool _isDescriptionExpanded = false;
        private bool _isHighlightsExpanded = false;
        private bool _isHighlightsAnimating;
        private double _highlightsStartTranslationY = 0;
        private const double HighlightsExpandedHeight = 510;
        private const double HighlightsCollapsedHeight = 112;
        private int _highlightScrollIndex;
        private readonly SemaphoreSlim _highlightsAnimationLock = new(1, 1);
        // current language code for UI and narration: "vi" by default
        private string _currentLanguage = "vi";
        private System.Collections.ObjectModel.ObservableCollection<string> _logItems;
        private Location _lastLocation; // Track last known location
        private string _lastSearchKeyword = string.Empty;
        private bool _isRealtimeEventsSubscribed;
        private bool _isPageInitializing;
        private CancellationTokenSource? _appearingCts;
        private CancellationTokenSource? _searchDebounceCts;
        private CancellationTokenSource? _detailCts;
        private CancellationTokenSource? _realtimeMapRefreshCts;
        private CancellationTokenSource? _realtimeHighlightsRefreshCts;
        private CancellationTokenSource? _realtimeDetailRefreshCts;
        private CancellationTokenSource? _languageRefreshCts;
        private string? _offlineMapLocalEntry;
        private bool _offlineMapEnabled;
        private int _detailRequestVersion;
        private string? _runtimeMapboxToken;
        private int _mapRefreshVersion;
        private bool _isLanguageModalOpen;
        private bool _suppressNextRealtimeFullSyncEvent;
        private readonly SemaphoreSlim _uiRefreshLock = new(1, 1);
        private readonly SemaphoreSlim _apiBaseReadyLock = new(1, 1);
        private readonly Dictionary<string, string> _highlightImageCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _highlightImageDownloadGate = new(2, 2);
        private readonly SemaphoreSlim _fullSyncGate = new(1, 1);
        private readonly Dictionary<string, string> _dynamicUiTextCache = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<int, PoiLiveStatsDto> _liveStatsByPoiId = new();
        private DateTime _lastLiveStatsFetchUtc = DateTime.MinValue;
        private static readonly HttpClient _highlightImageHttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
        private bool _apiBaseReady;
        private Task? _backgroundFullSyncTask;
        private DateTime _lastHeartbeatUtc = DateTime.MinValue;
        private int _lastHeartbeatPoiId;
        private int _pendingNavigationPoiId;
    // Limit number of pins rendered to keep map responsive on low-end devices/emulators
    private const int MaxPinsToRender = 300;
        // Cache rendered Pin instances by POI id for incremental updates
        private Dictionary<int, Microsoft.Maui.Controls.Maps.Pin> _pinByPoiId;
        private CancellationTokenSource? _mapMoveDebounceCts;
        private static string BuildDeviceAnalyticsId()
        {
            try
            {
                var platform = DeviceInfo.Platform.ToString();
                var model = DeviceInfo.Model?.Trim();
                var manufacturer = DeviceInfo.Manufacturer?.Trim();
                var version = DeviceInfo.VersionString?.Trim();
                return $"{platform}|{manufacturer}|{model}|{version}";
            }
            catch
            {
                return Environment.MachineName;
            }
        }

        // Debounced property changed handler for map VisibleRegion updates
        private void OnMapPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try
            {
                if (e == null) return;
                if (!string.Equals(e.PropertyName, "VisibleRegion", StringComparison.OrdinalIgnoreCase)) return;

                _mapMoveDebounceCts?.Cancel();
                _mapMoveDebounceCts?.Dispose();
                _mapMoveDebounceCts = new CancellationTokenSource();
                var token = _mapMoveDebounceCts.Token;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(250, token);
                        if (token.IsCancellationRequested) return;
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            try { AddPoisToMap(); } catch { }
                        });
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                });
            }
            catch { }
        }

        private void SetMapLoadingState(bool isLoading)
        {
            // Loading placeholder disabled by UX request
            try
            {
                if (MapLoadingPlaceholder != null)
                {
                    MapLoadingPlaceholder.IsVisible = false;
                }
            }
            catch { }
        }

        public MapPage(DatabaseService dbService, IGeofenceEngine geofenceEngine, NarrationService narrationService,
            LocationPollingService locationPollingService, AudioQueueService audioQueue, PermissionService permissionService, VinhKhanh.Services.ApiService apiService, IAudioGenerator audioGenerator, RealtimeSyncManager realtimeSyncManager, IMapOfflinePackService mapOfflinePackService)
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
            _realtimeSyncManager = realtimeSyncManager;
            _mapOfflinePackService = mapOfflinePackService;
            // Đăng ký sự kiện PoiTriggered
            _geofenceEngine.PoiTriggered += OnPoiTriggered;
            try { vinhKhanhMap.PropertyChanged += OnMapPropertyChanged; } catch { }
            // close POI when tapping on empty map area
            try { vinhKhanhMap.MapClicked += OnMapClicked; } catch { }

            // placeholder: action button images are now inside pill Frames (no direct named ImageButtons)

            try
            {
                _currentLanguage = Preferences.Default.Get("selected_language", "vi");
                if (TxtCustomLanguageCode != null)
                {
                    TxtCustomLanguageCode.Text = _currentLanguage;
                }
            }
            catch
            {
                _currentLanguage = "vi";
            }

            // ensure language UI state and strings reflect current selection at startup
            UpdateLanguageSelectionUI();
            _ = UpdateUiStringsAsync();

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

            try
            {
                _offlineMapEnabled = Preferences.Default.Get("offline_map_enabled", false);
            }
            catch
            {
                _offlineMapEnabled = false;
            }

            try
            {
                _offlineMapLocalEntry = Preferences.Default.Get("offline_map_local_entry", string.Empty);
            }
            catch
            {
                _offlineMapLocalEntry = string.Empty;
            }

            var isVietnamese = string.Equals(NormalizeLanguageCode(_currentLanguage), "vi", StringComparison.OrdinalIgnoreCase);
            UpdateOfflineMapStatusUi(_offlineMapEnabled
                ? (isVietnamese ? "Trạng thái: Offline map đã sẵn sàng" : "Status: Offline map is ready")
                : (isVietnamese ? "Trạng thái: Chưa tải bản đồ offline" : "Status: Offline map not downloaded"));
            UpdateOfflineMapProgressUi(0, isVietnamese ? "Tiến độ: 0%" : "Progress: 0%");

            // Highlights collection placeholder
            try { CvHighlights.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<PoiModel>(); } catch { }

        }

        private void EnsureRealtimeSyncSubscriptions()
        {
            if (_isRealtimeEventsSubscribed || _realtimeSyncManager == null) return;

            _realtimeSyncManager.PoiDataChanged += HandleRealtimePoiChanged;
            _realtimeSyncManager.ContentDataChanged += HandleRealtimeContentChanged;
            _realtimeSyncManager.AudioDataChanged += HandleRealtimeAudioChanged;
            _realtimeSyncManager.FullSyncRequested += HandleRealtimeFullSyncRequested;
            _isRealtimeEventsSubscribed = true;
        }

        private async Task HandleRealtimePoiChanged(PoiModel poi)
        {
            try
            {
                _ = ScheduleRealtimeMapRefreshAsync(refreshSelectedPoi: true);
                _ = PushPoisToOfflineMapAsync();
            }
            catch { }

            await Task.CompletedTask;
        }

        private async Task HandleRealtimeContentChanged(ContentModel content)
        {
            try
            {
                if (_selectedPoi != null
                    && content != null
                    && content.PoiId == _selectedPoi.Id
                    && PoiDetailPanel != null
                    && PoiDetailPanel.IsVisible)
                {
                    _ = ScheduleRealtimeSelectedPoiDetailRefreshAsync();
                }

                _ = ScheduleRealtimeHighlightsRefreshAsync();
            }
            catch { }

            await Task.CompletedTask;
        }

        private async Task ScheduleRealtimeSelectedPoiDetailRefreshAsync()
        {
            try
            {
                _realtimeDetailRefreshCts?.Cancel();
                _realtimeDetailRefreshCts?.Dispose();
                _realtimeDetailRefreshCts = new CancellationTokenSource();
                var token = _realtimeDetailRefreshCts.Token;

                await Task.Delay(320, token);
                if (token.IsCancellationRequested) return;

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (token.IsCancellationRequested) return;
                    if (_selectedPoi == null) return;
                    await ShowPoiDetail(_selectedPoi);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch { }
        }

        private async Task HandleRealtimeAudioChanged(AudioModel audio)
        {
            try
            {
                if (audio == null) return;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (_selectedPoi != null && audio.PoiId == _selectedPoi.Id)
                    {
                        AddLog($"Audio cập nhật cho POI #{audio.PoiId}");
                    }

                    return Task.CompletedTask;
                });
            }
            catch { }

            await Task.CompletedTask;
        }

        private async Task HandleRealtimeFullSyncRequested()
        {
            try
            {
                if (_suppressNextRealtimeFullSyncEvent)
                {
                    _suppressNextRealtimeFullSyncEvent = false;
                    return;
                }

                _ = ScheduleRealtimeMapRefreshAsync(refreshSelectedPoi: true);
            }
            catch { }

            await Task.CompletedTask;
        }

        private async Task ScheduleRealtimeMapRefreshAsync(bool refreshSelectedPoi)
        {
            try
            {
                _realtimeMapRefreshCts?.Cancel();
                _realtimeMapRefreshCts?.Dispose();
                _realtimeMapRefreshCts = new CancellationTokenSource();
                var token = _realtimeMapRefreshCts.Token;

                await Task.Delay(220, token);
                if (token.IsCancellationRequested) return;

                var updatedPois = await _dbService.GetPoisAsync();
                if (token.IsCancellationRequested) return;

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    _pois = updatedPois ?? new List<PoiModel>();
                    AddPoisToMap();
                    try { BtnShowSaved.IsVisible = _pois.Any(p => p.IsSaved); } catch { }

                    _ = ScheduleRealtimeHighlightsRefreshAsync();

                    if (refreshSelectedPoi && _selectedPoi != null)
                    {
                        var refreshedSelected = _pois.FirstOrDefault(p => p.Id == _selectedPoi.Id);
                        if (refreshedSelected == null)
                        {
                            _selectedPoi = null;
                            if (PoiDetailPanel != null) PoiDetailPanel.IsVisible = false;
                        }
                        else if (PoiDetailPanel?.IsVisible == true)
                        {
                            _selectedPoi = refreshedSelected;
                            await ShowPoiDetail(refreshedSelected);
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch { }
        }

        private async Task ScheduleRealtimeHighlightsRefreshAsync()
        {
            try
            {
                _realtimeHighlightsRefreshCts?.Cancel();
                _realtimeHighlightsRefreshCts?.Dispose();
                _realtimeHighlightsRefreshCts = new CancellationTokenSource();
                var token = _realtimeHighlightsRefreshCts.Token;

                await Task.Delay(180, token);
                if (token.IsCancellationRequested) return;

                var top = (_pois ?? new List<PoiModel>()).OrderByDescending(p => p.Priority).Take(6).ToList();
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (token.IsCancellationRequested) return;
                    await RenderHighlightsAsync(top);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch { }
        }

        // Map page QR tap handler (opens QR modal centered)
        private async void OnShowQrClicked_Map(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPoi == null)
                {
                    await TryRestoreSelectedPoiFromUiAsync();
                }

                if (_selectedPoi == null)
                {
                    var dialogText = await GetDialogTextsAsync();
                    await DisplayAlert(dialogText["error"], dialogText["no_selected_poi_qr"], dialogText["close"]);
                    return;
                }

                // Always refresh selected POI from API so QR payload stays synced with Admin source-of-truth.
                await HydratePoiDetailsFromApiAsync(_selectedPoi);

                var payload = _selectedPoi.QrCode?.Trim();
                if (string.IsNullOrWhiteSpace(payload))
                {
                    // Fallback only when admin has no payload yet.
                    payload = ToAbsoluteApiUrl($"/qr/{_selectedPoi.Id}?lang=vi");
                    _selectedPoi.QrCode = payload;
                    try { await _dbService.SavePoiAsync(_selectedPoi); } catch { }
                }
                else if (payload.StartsWith("/", StringComparison.Ordinal))
                {
                    payload = ToAbsoluteApiUrl(payload);
                }

                // create modal page with QR image and X close in corner
                var qrSrc = await new MapPageHelpers().GenerateQrImageSourceAsync(payload);
                var overlay = new Grid { BackgroundColor = Microsoft.Maui.Graphics.Colors.Black.WithAlpha(0.6f) };

                var box = new Frame { BackgroundColor = Microsoft.Maui.Graphics.Colors.White, CornerRadius = 16, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Padding = 16 };
                var img = new Image { Source = qrSrc, WidthRequest = 300, HeightRequest = 300, Aspect = Aspect.AspectFit };
                var closeX = new Button { Text = "✕", BackgroundColor = Microsoft.Maui.Graphics.Colors.Transparent, TextColor = Microsoft.Maui.Graphics.Colors.Black, FontSize = 20, WidthRequest = 44, HeightRequest = 44, CornerRadius = 22, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start };
                closeX.Clicked += async (s, ev) => await Navigation.PopModalAsync();

                var closeText = await GetDialogTextsAsync();
                var closeBtn = new Button { Text = closeText["close"], BackgroundColor = Microsoft.Maui.Graphics.Colors.Black, TextColor = Microsoft.Maui.Graphics.Colors.White, CornerRadius = 10, HeightRequest = 44 };
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
                if (HighlightsPanel == null || _isHighlightsAnimating) return;
                switch (e.StatusType)
                {
                    case GestureStatus.Started:
                        _highlightsStartTranslationY = HighlightsPanel.TranslationY;
                        break;
                    case GestureStatus.Running:
                        var rawY = _highlightsStartTranslationY + e.TotalY;
                        var minY = -(HighlightsExpandedHeight - HighlightsCollapsedHeight);
                        var maxY = 0d;
                        HighlightsPanel.TranslationY = Math.Clamp(rawY, minY, maxY);
                        break;
                    case GestureStatus.Completed:
                    case GestureStatus.Canceled:
                        var threshold = 12d;
                        var currentY = HighlightsPanel.TranslationY;
                        var midpoint = -(HighlightsExpandedHeight - HighlightsCollapsedHeight) / 2d;
                        var shouldExpand = e.TotalY <= -threshold
                            ? true
                            : e.TotalY >= threshold
                                ? false
                                : currentY <= midpoint;

                        HighlightsPanel.TranslationY = 0;
                        SetHighlightsExpandedState(shouldExpand);
                        break;
                }
            }
            catch { }
        }

        private void OnHighlightsPanelTapped(object sender, EventArgs e)
        {
            try
            {
                if (!_isHighlightsExpanded)
                {
                    SetHighlightsExpandedState(true);
                }
            }
            catch { }
        }

        private void OnScrollHighlightsUpClicked(object sender, EventArgs e)
        {
            try
            {
                _ = ScrollHighlightsBy(-1);
            }
            catch { }
        }

        private void OnScrollHighlightsDownClicked(object sender, EventArgs e)
        {
            try
            {
                _ = ScrollHighlightsBy(1);
            }
            catch { }
        }

        private async Task ScrollHighlightsBy(int delta)
        {
            try
            {
                if (CvHighlights?.ItemsSource is not IEnumerable<VinhKhanh.Shared.HighlightViewModel> items) return;
                var list = items.ToList();
                if (!list.Any()) return;

                if (!_isHighlightsExpanded)
                {
                    await AnimateHighlightsExpandedStateAsync(true);
                }

                _highlightScrollIndex = Math.Clamp(_highlightScrollIndex + delta, 0, list.Count - 1);
                CvHighlights.ScrollTo(_highlightScrollIndex, position: ScrollToPosition.Center, animate: true);
            }
            catch { }
        }

        private void SetHighlightsExpandedState(bool expanded)
        {
            _ = AnimateHighlightsExpandedStateAsync(expanded);
        }

        private async Task AnimateHighlightsExpandedStateAsync(bool expanded)
        {
            await _highlightsAnimationLock.WaitAsync();
            try
            {
                if (HighlightsPanel == null) return;
                var targetPanelHeight = expanded ? HighlightsExpandedHeight : HighlightsCollapsedHeight;
                var targetListHeight = expanded ? 320 : 0;

                if (_isHighlightsExpanded == expanded
                    && Math.Abs((HighlightsPanel.HeightRequest <= 0 ? HighlightsCollapsedHeight : HighlightsPanel.HeightRequest) - targetPanelHeight) < 0.5)
                {
                    if (CvHighlights != null)
                    {
                        CvHighlights.IsVisible = expanded;
                        CvHighlights.HeightRequest = targetListHeight;
                    }

                    if (BtnViewAllHighlights != null)
                    {
                        BtnViewAllHighlights.IsVisible = expanded;
                    }

                    if (BtnToggleHighlights != null)
                    {
                        BtnToggleHighlights.Text = expanded ? "-" : "+";
                    }

                    return;
                }

                _isHighlightsAnimating = true;

                this.AbortAnimation("HighlightsPanelHeightAnim");
                this.AbortAnimation("HighlightsListHeightAnim");

                HighlightsPanel.TranslationY = 0;
                HighlightsPanel.HeightRequest = targetPanelHeight;

                if (CvHighlights != null)
                {
                    if (expanded)
                    {
                        CvHighlights.IsVisible = true;
                        CvHighlights.HeightRequest = targetListHeight;
                        await CvHighlights.FadeTo(1, 140, Easing.CubicOut);
                    }
                    else
                    {
                        await CvHighlights.FadeTo(0, 100, Easing.CubicIn);
                        CvHighlights.HeightRequest = 0;
                        CvHighlights.IsVisible = false;
                    }
                }

                if (BtnViewAllHighlights != null)
                {
                    if (expanded)
                    {
                        BtnViewAllHighlights.IsVisible = true;
                        await BtnViewAllHighlights.FadeTo(1, 140, Easing.CubicOut);
                    }
                    else
                    {
                        await BtnViewAllHighlights.FadeTo(0, 100, Easing.CubicIn);
                        BtnViewAllHighlights.IsVisible = false;
                    }
                }

                if (HighlightsLoadingIndicator != null && !expanded)
                {
                    HighlightsLoadingIndicator.IsRunning = false;
                    HighlightsLoadingIndicator.IsVisible = false;
                }

                _isHighlightsExpanded = expanded;

                if (BtnToggleHighlights != null)
                {
                    BtnToggleHighlights.Text = expanded ? "-" : "+";
                }
            }
            catch { }
            finally
            {
                _isHighlightsAnimating = false;
                _highlightsAnimationLock.Release();
            }
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
                if (!string.IsNullOrWhiteSpace(payload)
                    && (payload.StartsWith("http", StringComparison.OrdinalIgnoreCase) || payload.StartsWith("/")))
                {
                    payload = ToAbsoluteApiUrl(payload);
                }
                var t = await GetDialogTextsAsync();
                var action = await DisplayActionSheet(t["qr_for_this_poi"], t["close"], null, t["copy_payload"], t["share_payload"], t["open_scan_sim"]);
                if (string.Equals(action, t["copy_payload"], StringComparison.OrdinalIgnoreCase))
                {
                    try { await Clipboard.Default.SetTextAsync(payload); await DisplayAlert(t["ok"], t["payload_copied"], t["close"]); } catch { }
                }
                else if (string.Equals(action, t["share_payload"], StringComparison.OrdinalIgnoreCase))
                {
                    try { await Share.RequestAsync(new ShareTextRequest { Text = payload, Title = "QR payload" }); } catch { }
                }
                else if (string.Equals(action, t["open_scan_sim"], StringComparison.OrdinalIgnoreCase))
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
            if (!MainThread.IsMainThread)
            {
                MainThread.BeginInvokeOnMainThread(() => AddLog(text));
                return;
            }

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
                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["permission_denied_title"], t["permission_denied_msg"], t["ok"]);
                    BtnStartTracking.IsEnabled = true;
                    return;
                }

                AddLog("Starting tracking service");
                // If background permission not granted, prompt the user to open Settings
                var bgOk = await _permissionService.IsBackgroundLocationGrantedAsync();
                if (!bgOk)
                {
                    var t = await GetDialogTextsAsync();
                    var go = await DisplayAlert(t["background_permission_title"], t["background_permission_msg"], t["open_settings"], t["continue_without"]);
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
                LblTrackingStatus.Text = await GetTrackingStatusTextAsync("tracking");
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
                LblTrackingStatus.Text = await GetTrackingStatusTextAsync("stopped");
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
                var normalizedLanguage = NormalizeLanguageCode(language);
                var content = await GetBestContentFromDbAsync(poiId, normalizedLanguage);
                if (HasMeaningfulContent(content)) return content;

                // Try hydrate from Admin/API when local is missing or stale
                var apiContents = await HydrateContentsFromApiAsync(poiId);
                if (apiContents.Any())
                {
                    content = SelectBestContentByLanguage(apiContents, normalizedLanguage);
                    if (HasMeaningfulContent(content)) return content;
                }

                // Retry local after hydrate
                content = await GetBestContentFromDbAsync(poiId, normalizedLanguage);
                if (HasMeaningfulContent(content)) return content;

                // Final fallback: translate from Vietnamese/English source so UI/content stays fully in selected language.
                var source = await GetBestContentFromDbAsync(poiId, "vi")
                             ?? await GetBestContentFromDbAsync(poiId, "en");
                var translated = await BuildTranslatedContentAsync(source, poiId, normalizedLanguage);
                if (HasMeaningfulContent(translated))
                {
                    try { await _dbService.SaveContentAsync(translated); } catch { }
                    return translated;
                }
            }
            catch { }
            return null;
        }

        private async Task<ContentModel?> BuildTranslatedContentAsync(ContentModel? source, int poiId, string language)
        {
            try
            {
                if (source == null) return null;
                var targetLang = NormalizeLanguageCode(language);
                if (string.Equals(targetLang, "vi", StringComparison.OrdinalIgnoreCase))
                {
                    return source;
                }

                return new ContentModel
                {
                    PoiId = poiId,
                    LanguageCode = targetLang,
                    Title = await TranslateTextAsync(source.Title ?? string.Empty, targetLang),
                    Subtitle = await TranslateTextAsync(source.Subtitle ?? string.Empty, targetLang),
                    Description = await TranslateTextAsync(source.Description ?? string.Empty, targetLang),
                    AudioUrl = source.AudioUrl,
                    IsTTS = source.IsTTS,
                    PriceRange = source.PriceRange,
                    Rating = source.Rating,
                    OpeningHours = source.OpeningHours,
                    PhoneNumber = source.PhoneNumber,
                    Address = await TranslateTextAsync(source.Address ?? string.Empty, targetLang),
                    ShareUrl = source.ShareUrl
                };
            }
            catch
            {
                return source;
            }
        }

        private async Task<ContentModel?> GetBestContentFromDbAsync(int poiId, string language)
        {
            try
            {
                var normalized = NormalizeLanguageCode(language);
                var all = await _dbService.GetContentsByPoiIdAsync(poiId) ?? new List<ContentModel>();

                var sameLang = all
                    .Where(c => c != null && NormalizeLanguageCode(c.LanguageCode) == normalized)
                    .OrderByDescending(ComputeContentQualityScore)
                    .ThenByDescending(c => c.Id)
                    .ToList();

                if (sameLang.Any()) return sameLang.First();

                var startsWithLang = all
                    .Where(c => c != null && (c.LanguageCode ?? string.Empty).StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(ComputeContentQualityScore)
                    .ThenByDescending(c => c.Id)
                    .ToList();

                return startsWithLang.FirstOrDefault();
            }
            catch
            {
                return await _dbService.GetContentByPoiIdAsync(poiId, language);
            }
        }

        private ContentModel? SelectBestContentByLanguage(IEnumerable<ContentModel>? source, string language)
        {
            var normalized = NormalizeLanguageCode(language);
            var list = source?
                .Where(c => c != null && NormalizeLanguageCode(c.LanguageCode) == normalized)
                .OrderByDescending(ComputeContentQualityScore)
                .ThenByDescending(c => c.Id)
                .ToList() ?? new List<ContentModel>();

            return list.FirstOrDefault();
        }

        private static int ComputeContentQualityScore(ContentModel? content)
        {
            if (content == null) return 0;

            var score = 0;
            if (!string.IsNullOrWhiteSpace(content.Title)) score += 4;
            if (!string.IsNullOrWhiteSpace(content.Description)) score += 6;
            if (!string.IsNullOrWhiteSpace(content.Subtitle)) score += 2;
            if (!string.IsNullOrWhiteSpace(content.Address)) score += 3;
            if (!string.IsNullOrWhiteSpace(content.PhoneNumber)) score += 2;
            if (!string.IsNullOrWhiteSpace(content.OpeningHours)) score += 2;
            if (content.Rating > 0) score += 1;
            return score;
        }

        private async Task<Dictionary<string, string>> GetDialogTextsAsync()
        {
            var lang = NormalizeLanguageCode(_currentLanguage);
            var viMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ok"] = "OK",
                ["close"] = "Đóng",
                ["cancel"] = "Hủy",
                ["error"] = "Lỗi",
                ["notification"] = "Thông báo",
                ["sync"] = "Sync",
                ["audio"] = "Audio",
                ["tts"] = "TTS",
                ["language"] = "Ngôn ngữ",
                ["search"] = "Tìm kiếm",
                ["directions"] = "Dẫn đường",
                ["permission_denied_title"] = "Quyền bị từ chối",
                ["permission_denied_msg"] = "Ứng dụng cần quyền vị trí để theo dõi. Vui lòng cấp quyền và thử lại.",
                ["background_permission_title"] = "Quyền nền",
                ["background_permission_msg"] = "Ứng dụng chưa được cấp quyền vị trí nền. Để theo dõi khi app ở nền, vui lòng cấp Permission 'Allow all the time' trong Cài đặt.",
                ["open_settings"] = "Mở Cài đặt",
                ["continue_without"] = "Tiếp tục (không cho phép)",
                ["no_selected_poi_qr"] = "Chưa chọn điểm để xem QR.",
                ["qr_for_this_poi"] = "QR cho điểm này",
                ["copy_payload"] = "Sao chép payload",
                ["share_payload"] = "Chia sẻ payload",
                ["open_scan_sim"] = "Mở trang quét (mô phỏng)",
                ["payload_copied"] = "Đã sao chép payload vào clipboard",
                ["save_audio_success"] = "File audio đã được lưu và gắn vào điểm này.",
                ["save_audio_failed"] = "Không thể lưu file audio.",
                ["tts_generated_local"] = "Đã tạo TTS tạm thời và đưa vào hàng đợi phát. (Phát bằng TTS cục bộ)",
                ["no_saved_poi"] = "Bạn chưa lưu địa điểm nào.",
                ["highlights_places"] = "Địa điểm thịnh hành",
                ["saved_places"] = "Địa điểm đã lưu",
                ["sync_service_missing"] = "Không tìm thấy dịch vụ đồng bộ.",
                ["sync_success"] = "Đã ghi đè dữ liệu mới nhất từ Admin/API.",
                ["sync_failed"] = "Force sync thất bại. Vui lòng thử lại.",
                ["syncing"] = "Đang đồng bộ...",
                ["sync_done_log"] = "Đồng bộ hoàn tất: {0} POI",
                ["no_tts_for_lang"] = "Chưa có TTS đúng ngôn ngữ hiện tại.",
                ["cannot_play_tts"] = "Không thể phát TTS lúc này.",
                ["no_audio_for_lang"] = "Chưa có file MP3/TTS cho ngôn ngữ hiện tại.",
                ["choose_audio_file"] = "Chọn file Audio",
                ["invalid_audio_file"] = "File audio không hợp lệ.",
                ["cannot_load_audio_list"] = "Không thể tải danh sách MP3.",
                ["listening"] = "Nghe",
                ["stop"] = "Dừng",
                ["source_translated"] = "Nguồn: nội dung dịch theo ngôn ngữ đã chọn",
                ["source_prefix"] = "Nguồn",
                ["language"] = "Ngôn ngữ",
                ["select_language"] = "Chọn ngôn ngữ",
                ["change_language"] = "Đổi ngôn ngữ",
                ["field_address_en"] = "Address",
                ["field_opening_hours_en"] = "Opening hours",
                ["field_price_en"] = "Price",
                ["listen_narration"] = "Listen narration",
                ["stop_narration"] = "Stop",
                ["no_selected_poi_directions"] = "Chưa chọn điểm để dẫn đường",
                ["opening_directions_to"] = "Đang mở chỉ đường tới {0}",
                ["opened_web_directions"] = "Đã mở chỉ đường bằng Google Maps web.",
                ["cannot_open_directions"] = "Không thể mở chỉ đường lúc này.",
                ["search_not_found"] = "Không tìm thấy POI phù hợp theo tên hoặc tiêu đề.",
                ["choose_poi"] = "Chọn POI",
                ["invalid_language_code"] = "Vui lòng nhập mã ngôn ngữ hợp lệ.",
                ["fallback_to_english"] = "Ngôn ngữ đã chọn chưa có dữ liệu, hệ thống tự động chuyển sang English."
            };

            if (lang == "vi") return viMap;

            var enMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ok"] = "OK",
                ["close"] = "Close",
                ["cancel"] = "Cancel",
                ["error"] = "Error",
                ["notification"] = "Notification",
                ["sync"] = "Sync",
                ["audio"] = "Audio",
                ["tts"] = "TTS",
                ["language"] = "Language",
                ["search"] = "Search",
                ["directions"] = "Directions",
                ["permission_denied_title"] = "Permission denied",
                ["permission_denied_msg"] = "Location permission is required. Please grant permission and try again.",
                ["background_permission_title"] = "Background permission",
                ["background_permission_msg"] = "Background location is not granted. Please allow 'Allow all the time' in Settings.",
                ["open_settings"] = "Open Settings",
                ["continue_without"] = "Continue without",
                ["no_selected_poi_qr"] = "No POI selected for QR.",
                ["qr_for_this_poi"] = "QR for this POI",
                ["copy_payload"] = "Copy payload",
                ["share_payload"] = "Share payload",
                ["open_scan_sim"] = "Open scan page (simulation)",
                ["payload_copied"] = "Payload copied to clipboard",
                ["save_audio_success"] = "Audio file was saved for this POI.",
                ["save_audio_failed"] = "Cannot save audio file.",
                ["tts_generated_local"] = "Temporary TTS generated and queued.",
                ["no_saved_poi"] = "No saved POIs.",
                ["highlights_places"] = "Popular places",
                ["saved_places"] = "Saved places",
                ["sync_service_missing"] = "Sync service is missing.",
                ["sync_success"] = "Latest data synced from Admin/API.",
                ["sync_failed"] = "Force sync failed. Please try again.",
                ["syncing"] = "Syncing...",
                ["sync_done_log"] = "Sync completed: {0} POIs",
                ["no_tts_for_lang"] = "No TTS available for current language.",
                ["cannot_play_tts"] = "Cannot play TTS right now.",
                ["no_audio_for_lang"] = "No MP3/TTS available for current language.",
                ["choose_audio_file"] = "Choose audio file",
                ["invalid_audio_file"] = "Invalid audio file.",
                ["cannot_load_audio_list"] = "Cannot load audio list.",
                ["listening"] = "Listen",
                ["stop"] = "Stop",
                ["source_translated"] = "Source: translated content for selected language",
                ["source_prefix"] = "Source",
                ["language"] = "Language",
                ["select_language"] = "Select language",
                ["change_language"] = "Change language",
                ["field_address_en"] = "Address",
                ["field_opening_hours_en"] = "Opening hours",
                ["field_price_en"] = "Price",
                ["listen_narration"] = "Listen narration",
                ["stop_narration"] = "Stop",
                ["no_selected_poi_directions"] = "No POI selected for directions.",
                ["opening_directions_to"] = "Opening directions to {0}",
                ["opened_web_directions"] = "Opened web directions in Google Maps.",
                ["cannot_open_directions"] = "Cannot open directions right now.",
                ["search_not_found"] = "No matching POI found.",
                ["choose_poi"] = "Choose POI",
                ["invalid_language_code"] = "Please enter a valid language code.",
                ["fallback_to_english"] = "Selected language has no data. Fallback to English."
            };

            if (lang == "en") return enMap;

            var translated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in enMap)
            {
                var cacheKey = $"dlg:{lang}:{kv.Key}";
                if (_dynamicUiTextCache.TryGetValue(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
                {
                    translated[kv.Key] = cached;
                    continue;
                }

                var value = await TranslateTextAsync(kv.Value, lang);
                if (string.IsNullOrWhiteSpace(value)) value = kv.Value;
                _dynamicUiTextCache[cacheKey] = value;
                translated[kv.Key] = value;
            }

            return translated;
        }

        private async Task<List<ContentModel>> HydrateContentsFromApiAsync(int poiId)
        {
            try
            {
                await EnsureApiBaseReadyAsync();
                var contents = await _apiService.GetContentsByPoiIdAsync(poiId) ?? new List<ContentModel>();
                foreach (var c in contents.Where(c => c != null))
                {
                    try
                    {
                        c.PoiId = poiId;
                        await _dbService.SaveContentAsync(c);
                    }
                    catch { }
                }

                return contents;
            }
            catch
            {
                return new List<ContentModel>();
            }
        }

        private async Task HydratePoiDetailsFromApiAsync(PoiModel poi)
        {
            try
            {
                if (poi == null || poi.Id <= 0) return;

                await EnsureApiBaseReadyAsync();
                PoiModel? remotePoi = null;

                var loadAll = await _apiService.GetPoisLoadAllAsync(NormalizeLanguageCode(_currentLanguage));
                PoiLoadAllItem? selectedItem = null;
                if (loadAll?.Items?.Any() == true)
                {
                    selectedItem = loadAll.Items.FirstOrDefault(x => x?.Poi != null && x.Poi.Id == poi.Id);
                    remotePoi = selectedItem?.Poi;
                }

                if (remotePoi == null)
                {
                    var remotePois = await _apiService.GetPoisAsync();
                    remotePoi = remotePois?.FirstOrDefault(p => p.Id == poi.Id);
                }

                if (remotePoi != null)
                {
                var hasChanged = !string.Equals(poi.Name, remotePoi.Name, StringComparison.Ordinal)
                        || !string.Equals(poi.ImageUrl, remotePoi.ImageUrl, StringComparison.Ordinal)
                        || !string.Equals(poi.Category, remotePoi.Category, StringComparison.Ordinal)
                    || !string.Equals(poi.WebsiteUrl, remotePoi.WebsiteUrl, StringComparison.Ordinal)
                    || !string.Equals(poi.QrCode, remotePoi.QrCode, StringComparison.Ordinal)
                    || poi.Priority != remotePoi.Priority
                    || poi.Radius != remotePoi.Radius
                    || poi.Latitude != remotePoi.Latitude
                    || poi.Longitude != remotePoi.Longitude;

                    if (hasChanged)
                    {
                        poi.Name = remotePoi.Name;
                        poi.ImageUrl = remotePoi.ImageUrl;
                        poi.Category = remotePoi.Category;
                        poi.Priority = remotePoi.Priority;
                        poi.Radius = remotePoi.Radius;
                    poi.Latitude = remotePoi.Latitude;
                    poi.Longitude = remotePoi.Longitude;
                        poi.WebsiteUrl = remotePoi.WebsiteUrl;
                        poi.QrCode = remotePoi.QrCode;
                        try { await _dbService.SavePoiAsync(poi); } catch { }
                    }
                }

                try
                {
                    if (selectedItem?.Localization.HasValue == true
                        && selectedItem.Localization.Value.ValueKind == JsonValueKind.Object)
                    {
                        var loc = selectedItem.Localization.Value;
                        var fallbackContent = new ContentModel
                        {
                            PoiId = poi.Id,
                            LanguageCode = loc.TryGetProperty("languageCode", out var lng)
                                ? (NormalizeLanguageCode(lng.GetString()) ?? "vi")
                                : NormalizeLanguageCode(_currentLanguage),
                            Title = loc.TryGetProperty("title", out var title) ? title.GetString() : null,
                            Subtitle = loc.TryGetProperty("subtitle", out var subtitle) ? subtitle.GetString() : null,
                            Description = loc.TryGetProperty("description", out var description) ? description.GetString() : null,
                            AudioUrl = loc.TryGetProperty("audio_url", out var audioUrl) ? audioUrl.GetString() : null,
                            IsTTS = loc.TryGetProperty("isTTS", out var isTts) && isTts.ValueKind == JsonValueKind.True,
                            PriceRange = loc.TryGetProperty("priceRange", out var priceRange) ? priceRange.GetString() : null,
                            Rating = loc.TryGetProperty("rating", out var rating) && rating.TryGetDouble(out var r) ? r : 0,
                            OpeningHours = loc.TryGetProperty("openingHours", out var openingHours) ? openingHours.GetString() : null,
                            PhoneNumber = loc.TryGetProperty("phoneNumber", out var phoneNumber) ? phoneNumber.GetString() : null,
                            Address = loc.TryGetProperty("address", out var address) ? address.GetString() : null,
                            ShareUrl = loc.TryGetProperty("shareUrl", out var shareUrl) ? shareUrl.GetString() : null
                        };

                        if (HasMeaningfulContent(fallbackContent))
                        {
                            await _dbService.SaveContentAsync(fallbackContent);
                        }
                    }
                }
                catch { }

                var hydrated = await HydrateContentsFromApiAsync(poi.Id);

                // Do not copy fallback language content into selected language slot.
                // Language completeness is handled by full fallback to English at selection time.
            }
            catch { }
        }

        private async Task EnsurePoiDataReadyAsync()
        {
            try
            {
                if (_pois != null && _pois.Any())
                {
                    return;
                }

                await EnsureApiBaseReadyAsync();

                var loadAll = await _apiService.GetPoisLoadAllAsync(NormalizeLanguageCode(_currentLanguage));
                var fromLoadAll = loadAll?.Items?
                    .Select(i => i?.Poi)
                    .Where(p => p != null)
                    .GroupBy(p => p.Id)
                    .Select(g => g.First())
                    .ToList() ?? new List<PoiModel>();

                if (!fromLoadAll.Any())
                {
                    fromLoadAll = await _apiService.GetPoisAsync() ?? new List<PoiModel>();
                }

                if (!fromLoadAll.Any())
                {
                    return;
                }

                foreach (var poi in fromLoadAll)
                {
                    try { await _dbService.SavePoiAsync(poi); } catch { }
                }

                _pois = fromLoadAll;
            }
            catch { }
        }

        private async Task EnsureApiBaseReadyAsync()
        {
            if (_apiBaseReady) return;

            await _apiBaseReadyLock.WaitAsync();
            try
            {
                if (_apiBaseReady) return;
                var bootstrap = await _apiService.GetPoisAsync();
                if (bootstrap != null)
                {
                    _apiBaseReady = true;
                }
            }
            catch { }
            finally
            {
                _apiBaseReadyLock.Release();
            }
        }

        private static bool IsLikelyImageUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var source = value.ToLowerInvariant();
            return source.EndsWith(".jpg") || source.EndsWith(".jpeg") || source.EndsWith(".png") || source.EndsWith(".webp") || source.EndsWith(".gif");
        }

        private static bool HasMeaningfulContent(ContentModel content)
        {
            if (content == null) return false;
            return !string.IsNullOrWhiteSpace(content.Title)
                || !string.IsNullOrWhiteSpace(content.Description)
                || !string.IsNullOrWhiteSpace(content.Subtitle)
                || !string.IsNullOrWhiteSpace(content.Address)
                || !string.IsNullOrWhiteSpace(content.OpeningHours)
                || content.Rating > 0;
        }

        private async Task<string> TranslateTextAsync(string source, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(source)) return string.Empty;

            var normalizedTarget = NormalizeLanguageCode(targetLanguage);

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={Uri.EscapeDataString(normalizedTarget)}&dt=t&q={Uri.EscapeDataString(source)}";
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
                return string.IsNullOrWhiteSpace(translated) ? source : translated;
            }
            catch
            {
                return source;
            }
        }

        // Japanese and Korean selection handlers
        // Close POI panel when clicking on map background
        private void OnMapClicked(object sender, Microsoft.Maui.Controls.Maps.MapClickedEventArgs e)
        {
            try
            {
                if (PoiDetailPanel != null && PoiDetailPanel.IsVisible)
                {
                    try { _ = _audioQueue?.StopAsync(); } catch { }
                    try { _narrationService?.Stop(); } catch { }
                    PoiDetailPanel.IsVisible = false;
                }

                if (HighlightsPanel != null && _selectedPoi == null)
                {
                    HighlightsPanel.IsVisible = true;
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
                await OpenPoiDetailFromSelectionAsync(sel, "highlight_select", userInitiated: true);
            }
            catch { }
        }

        // Called when user taps image or name inside highlight card
        private async void OnHighlightItemTapped(object sender, EventArgs e)
        {
            try
            {
                // Determine binding context from sender or parent
                PoiModel poi = null;
                if (sender is VisualElement ve && ve.BindingContext is VinhKhanh.Shared.HighlightViewModel hvm && hvm.Poi != null)
                {
                    poi = hvm.Poi;
                }
                else
                {
                    // fall back: try to find nearest BindingContext up the visual tree
                    if (sender is Element elem)
                    {
                        var current = elem;
                        while (current != null && !(current is CollectionView))
                        {
                            if (current.BindingContext is VinhKhanh.Shared.HighlightViewModel bc && bc.Poi != null)
                            {
                                poi = bc.Poi;
                                break;
                            }
                            current = current.Parent;
                        }
                    }
                }

                if (poi != null)
                {
                    await OpenPoiDetailFromSelectionAsync(poi, "highlight_tap_image_or_name", userInitiated: true);
                }
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
                    var list = new VinhKhanh.Pages.HighlightsListPage(_pois.OrderByDescending(p => p.Priority).ToList(), _currentLanguage, _dbService, _apiService);
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
                    var fullList = (_pois ?? new List<PoiModel>())
                        .Where(p => p != null)
                        .DistinctBy(p => p.Id)
                        .OrderByDescending(p => p.Priority)
                        .ThenBy(p => p.Name)
                        .ToList();

                    var list = new VinhKhanh.Pages.HighlightsListPage(fullList, _currentLanguage, _dbService, _apiService);
                    await Navigation.PushAsync(list);
                }
                catch
                {
                    await ShowHighlightsListFallback((_pois ?? new List<PoiModel>())
                        .Where(p => p != null)
                        .DistinctBy(p => p.Id)
                        .OrderByDescending(p => p.Priority)
                        .ThenBy(p => p.Name)
                        .ToList());
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
                    await OpenPoiDetailFromSelectionAsync(vm.Poi, "highlight_tap", userInitiated: true);
                }
            }
            catch { }
        }

        private async Task OpenPoiDetailFromSelectionAsync(PoiModel poi, string trigger, bool userInitiated)
        {
            if (poi == null) return;

            try
            {
                _selectedPoi = poi;
                _ = TrackPoiEventAsync("poi_click", poi.Id, $"\"trigger\":\"{trigger}\",\"lang\":\"{NormalizeLanguageCode(_currentLanguage)}\"");

                if (HighlightsPanel != null)
                {
                    HighlightsPanel.IsVisible = true;
                    SetHighlightsExpandedState(false);
                }

                await ShowPoiDetail(poi, userInitiated);
            }
            catch
            {
                _selectedPoi = null;
                try
                {
                    if (PoiDetailPanel != null) PoiDetailPanel.IsVisible = false;
                    if (HighlightsPanel != null) HighlightsPanel.IsVisible = true;
                }
                catch { }
            }
        }

        private void OnToggleHighlightsClicked(object sender, EventArgs e)
        {
            try
            {
                if (HighlightsPanel == null) return;
                var expandNow = !_isHighlightsExpanded;
                SetHighlightsExpandedState(expandNow);

                if (expandNow)
                {
                    _ = MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        try
                        {
                            await Task.Delay(120);
                            CvHighlights?.ScrollTo(0, position: ScrollToPosition.Start, animate: true);
                        }
                        catch { }
                    });
                }
            }
            catch { }
        }

        private async void OnViewSavedClicked(object sender, EventArgs e)
        {
            try
            {
                // Open saved POIs directly on map detail panel (no separate page)
                _pois = await _dbService.GetPoisAsync();
                var saved = _pois.Where(p => p.IsSaved).ToList();
                if (!saved.Any())
                {
                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["notification"], t["no_saved_poi"], t["ok"]);
                    return;
                }

                var options = saved
                    .OrderByDescending(p => p.Priority)
                    .ThenBy(p => p.Name)
                    .Take(12)
                    .Select(p => $"#{p.Id} - {p.Name}")
                    .ToArray();

                var text = await GetDialogTextsAsync();
                var picked = await DisplayActionSheet(text["saved_places"], text["cancel"], null, options);
                if (string.IsNullOrWhiteSpace(picked) || string.Equals(picked, text["cancel"], StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var target = saved.FirstOrDefault(p => string.Equals($"#{p.Id} - {p.Name}", picked, StringComparison.Ordinal));
                if (target == null)
                {
                    return;
                }

                _selectedPoi = target;
                _ = TrackPoiEventAsync("poi_click", target.Id, $"\"trigger\":\"saved_shortcut\",\"lang\":\"{NormalizeLanguageCode(_currentLanguage)}\"");

                if (HighlightsPanel != null)
                {
                    HighlightsPanel.IsVisible = true;
                    SetHighlightsExpandedState(false);
                }

                await ShowPoiDetail(target, true);
            }
            catch { }
        }

        protected override void OnNavigatedTo(NavigatedToEventArgs args)
        {
            base.OnNavigatedTo(args);
        }
        private async Task CheckMapDisplayAsync()
        {
            try
            {
                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    try
                    {
                        if (MapboxOfflineWebView != null)
                        {
                            MapboxOfflineWebView.IsVisible = false;
                            MapboxOfflineWebView.InputTransparent = true;
                        }

                        if (vinhKhanhMap != null)
                        {
                            vinhKhanhMap.IsVisible = true;
                            vinhKhanhMap.InputTransparent = false;
                        }
                    }
                    catch { }
                }

                // wait a short time for map to initialize
                await Task.Delay(2500);
                // If VisibleRegion is null or center NaN, consider map not rendered
                if (vinhKhanhMap == null || vinhKhanhMap.VisibleRegion == null || double.IsNaN(vinhKhanhMap.VisibleRegion.Center?.Latitude ?? double.NaN))
                {
                    AddLog("Map control did not render. Keep GG map visible and retry positioning.");
                    try
                    {
                        if (MapboxOfflineWebView != null)
                        {
                            MapboxOfflineWebView.IsVisible = false;
                            MapboxOfflineWebView.InputTransparent = true;
                        }

                        if (vinhKhanhMap != null)
                        {
                            vinhKhanhMap.IsVisible = true;
                            vinhKhanhMap.InputTransparent = false;
                            CenterMapOnVinhKhanh();
                        }
                    }
                    catch { }
                }
                else
                {
                    AddLog("Map rendered successfully.");
                }

                SetMapLoadingState(false);
            }
            catch (Exception ex)
            {
                AddLog($"CheckMapDisplay error: {ex.Message}");
                SetMapLoadingState(false);
            }
        }

        // ================== SEED DATA (DỮ LIỆU MẪU) ==================
        private async Task SeedFullData()
        {
            await Task.CompletedTask;
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

                _ = TrackPoiEventAsync("poi_enter", e.Poi.Id, $"\"trigger\":\"geofence\",\"distance\":{Math.Round(e.DistanceMeters, 2).ToString(System.Globalization.CultureInfo.InvariantCulture)},\"lang\":\"{NormalizeLanguageCode(_currentLanguage)}\"");

                if (_pendingNavigationPoiId > 0 && _pendingNavigationPoiId == e.Poi.Id)
                {
                    _pendingNavigationPoiId = 0;
                    _ = TrackPoiEventAsync("navigation_arrived", e.Poi.Id, $"\"trigger\":\"geofence_arrive\",\"lang\":\"{NormalizeLanguageCode(_currentLanguage)}\"");
                }

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
        private async Task DisplayAllPois(CancellationToken cancellationToken = default)
        {
            var refreshVersion = Interlocked.Increment(ref _mapRefreshVersion);
            var poisToShow = _pois;
            if (poisToShow == null || !poisToShow.Any()) return;

            // Build quick lookup of content titles for preferred language to avoid sequential DB calls per POI
            var preferredLang = NormalizeLanguageCode(_currentLanguage);
            var contents = await _dbService.GetAllContentsAsync();
            var contentLookup = new Dictionary<int, ContentModel?>();
            try
            {
                // Prefer exact preferredLang, then en, then vi
                var grouped = (contents ?? new List<ContentModel>())
                    .GroupBy(c => c.PoiId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var kv in grouped)
                {
                    var poiId = kv.Key;
                    var list = kv.Value;
                    ContentModel? sel = list.FirstOrDefault(c => NormalizeLanguageCode(c.LanguageCode) == preferredLang && HasMeaningfulContent(c))
                                    ?? list.FirstOrDefault(c => NormalizeLanguageCode(c.LanguageCode) == "en" && HasMeaningfulContent(c))
                                    ?? list.FirstOrDefault(c => NormalizeLanguageCode(c.LanguageCode) == "vi" && HasMeaningfulContent(c));
                    contentLookup[poiId] = sel;
                }
            }
            catch { }

            var pinInfos = new List<(PoiModel Poi, string Label)>();
            foreach (var poi in poisToShow)
            {
                if (cancellationToken.IsCancellationRequested) return;
                if (refreshVersion != _mapRefreshVersion) return;
                var currentPoi = poi;
                string label = currentPoi.Name;
                try
                {
                    if (currentPoi.Id > 0 && contentLookup.TryGetValue(currentPoi.Id, out var content) && content != null && !string.IsNullOrEmpty(content.Title))
                    {
                        label = content.Title;
                    }
                }
                catch { }

                pinInfos.Add((currentPoi, label));
            }

            if (cancellationToken.IsCancellationRequested) return;
            if (refreshVersion != _mapRefreshVersion) return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    if (refreshVersion != _mapRefreshVersion) return;
                    if (vinhKhanhMap == null) return;
                    vinhKhanhMap.Pins.Clear();

                    // Limit number of pins to render to keep UI responsive on low-end devices/emulators
                    var toRender = pinInfos.Take(MaxPinsToRender).ToList();
                    foreach (var info in toRender)
                    {
                        var currentPoi = info.Poi;
                        var pin = new Pin
                        {
                            Label = info.Label,
                            Location = new Location(currentPoi.Latitude, currentPoi.Longitude),
                            Type = currentPoi.Category == "BusStop" ? PinType.SearchResult : PinType.Place
                        };

                        // Use event handler that hides default info window and opens detail reliably
                        pin.MarkerClicked += async (s, e) =>
                        {
                            try
                            {
                                // prevent default info window
                                try { e.HideInfoWindow = true; } catch { }
                                // Use existing selection flow so analytics and UI states remain consistent
                                await OpenPoiDetailFromSelectionAsync(currentPoi, "map_pin", userInitiated: true);
                            }
                            catch { }
                        };

                        vinhKhanhMap.Pins.Add(pin);
                    }

                    if (pinInfos.Count > MaxPinsToRender)
                    {
                        AddLog($"Rendered {MaxPinsToRender} of {pinInfos.Count} pins to keep map responsive.");
                    }

                    // update highlight for nearest POI relative to current center/user location
                    await HighlightNearestPoi();
                }
                catch { }
            });
        }

        private async Task ShowPoiDetail(PoiModel poi, bool userInitiated = false, bool hydrateFromApi = true)
        {
            if (poi == null) return;

            _selectedPoi = poi;

            var requestVersion = Interlocked.Increment(ref _detailRequestVersion);
            _detailCts?.Cancel();
            _detailCts?.Dispose();
            _detailCts = new CancellationTokenSource();

            await HydratePoiDetailsFromApiAsync(poi);
            var dynamicUi = await BuildDynamicUiTextAsync(_currentLanguage);

            // Title pref: content.Title if available, otherwise poi.Name
            var content = await GetContentForLanguageAsync(poi.Id, _currentLanguage);
            if (_detailCts.IsCancellationRequested || requestVersion != _detailRequestVersion)
            {
                return;
            }

            LblPoiName.Text = content?.Title ?? poi.Name;

            if (userInitiated)
            {
                _ = TrackPoiEventAsync("poi_detail_open", poi.Id, $"\"trigger\":\"tap\",\"lang\":\"{NormalizeLanguageCode(_currentLanguage)}\"");
            }
            // Address & phone: nếu thiếu dữ liệu ngôn ngữ hiện tại thì fallback English
            try
            {
                var phone = content?.PhoneNumber;
                var addr = content?.Address;
                if (string.IsNullOrEmpty(phone) || string.IsNullOrEmpty(addr))
                {
                    var fallback = await GetStrictContentForLanguageAsync(poi.Id, NormalizeLanguageCode(_currentLanguage));
                    if (fallback != null)
                    {
                        if (string.IsNullOrEmpty(phone)) phone = fallback.PhoneNumber;
                        if (string.IsNullOrEmpty(addr)) addr = fallback.Address;
                    }
                }

                if (LblAddress != null) LblAddress.Text = addr ?? string.Empty;
                if (LblPhone != null) LblPhone.Text = phone ?? string.Empty;

                try
                {
                    if (LblPoiCategory != null) LblPoiCategory.Text = await LocalizeFreeTextAsync(poi.Category, _currentLanguage);
                    if (LblRadiusChip != null) LblRadiusChip.Text = poi.Radius > 0 ? $"{Math.Round(poi.Radius, 0)}m" : string.Empty;
                    if (LblPriorityChip != null) LblPriorityChip.Text = poi.Priority > 0
                        ? dynamicUi["priority_chip"].Replace("{value}", poi.Priority.ToString())
                        : string.Empty;
                    if (LblCoordinates != null) LblCoordinates.Text = $"{poi.Latitude:0.#####}, {poi.Longitude:0.#####}";
                    if (LblWebsite != null)
                    {
                        var website = poi.WebsiteUrl;
                        if (!string.IsNullOrWhiteSpace(website)
                            && !website.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            && !website.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            website = "https://" + website.Trim();
                        }

                        LblWebsite.Text = website ?? string.Empty;
                    }

                    if (LblDistance != null)
                    {
                        var lat = _lastLocation?.Latitude;
                        var lng = _lastLocation?.Longitude;
                        if (lat.HasValue && lng.HasValue)
                        {
                            var meters = HaversineDistanceMeters(lat.Value, lng.Value, poi.Latitude, poi.Longitude);
                            LblDistance.Text = meters >= 1000
                                ? $"{meters / 1000d:0.0} km"
                                : $"{meters:0} m";
                        }
                        else
                        {
                            LblDistance.Text = string.Empty;
                        }
                    }
                }
                catch { }
            }
            catch { }
            try
            {
                // Populate carousel with up to 5 images (fallback to placeholder)
                var images = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(poi.ImageUrl))
                {
                    var parsed = poi.ImageUrl
                        .Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x?.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (parsed.Any())
                    {
                        foreach (var item in parsed)
                        {
                            images.Add(ToAbsoluteApiUrl(item));
                        }
                    }
                    else
                    {
                        images.Add(ToAbsoluteApiUrl(poi.ImageUrl));
                    }
                }
                if (!string.IsNullOrWhiteSpace(content?.AudioUrl) && IsLikelyImageUrl(content.AudioUrl)) images.Add(ToAbsoluteApiUrl(content.AudioUrl));
                // (If you later add multiple image URLs on POI or content, append here)
                if (!images.Any()) images.Add("dulich1.jpg");
                var resolvedImages = new List<string>();
                foreach (var raw in images)
                {
                    try
                    {
                        var localOrUrl = await ResolveHighlightImageSourceAsync(raw, poi.Id);
                        if (!string.IsNullOrWhiteSpace(localOrUrl))
                        {
                            resolvedImages.Add(localOrUrl);
                        }
                    }
                    catch { }
                }

                if (!resolvedImages.Any()) resolvedImages.Add("dulich1.jpg");
                try { ImgCarousel.ItemsSource = resolvedImages.Distinct(StringComparer.OrdinalIgnoreCase).ToList(); ImgCarousel.Position = 0; } catch { }
            }
            catch { }
            // Subtitle and description
            if (LblSubtitle != null) LblSubtitle.Text = content?.Subtitle ?? string.Empty;
            if (LblDescription != null) LblDescription.Text = content?.Description ?? dynamicUi["no_description"];

            // Optional metadata
            try { var _lblRating = this.FindByName<Label>("LblRating"); if (_lblRating != null) _lblRating.Text = content != null && content.Rating > 0 ? $"{content.Rating:0.0} ★" : string.Empty; } catch { }
            try { var _lblPrice = this.FindByName<Label>("LblPrice"); if (_lblPrice != null) _lblPrice.Text = content?.GetNormalizedPriceRangeDisplay() ?? string.Empty; } catch { }
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

                            LblOpenStatus.Text = open ? dynamicUi["open_now"] : dynamicUi["closed"];
                            LblOpenStatus.TextColor = open ? Microsoft.Maui.Graphics.Color.FromArgb("#388E3C") : Microsoft.Maui.Graphics.Color.FromArgb("#D32F2F");
                        }
                    }
                }
            }
            catch { }

            // Reviews count (if available in content) - not present in model, leave blank or add when available
            try
            {
                var _lblRev = this.FindByName<Label>("LblReviewCount");
                var _lblSummary = this.FindByName<Label>("LblReviewsSummary");
                var ratingText = content != null && content.Rating > 0 ? $"{content.Rating:0.0}★" : dynamicUi["no_rating"];
                if (_lblRev != null) _lblRev.Text = ratingText;
                if (_lblSummary != null)
                {
                    var open = string.IsNullOrWhiteSpace(LblOpenStatus?.Text) ? string.Empty : $" · {LblOpenStatus.Text}";
                    _lblSummary.Text = $"{dynamicUi["rating_label"]}: {ratingText}{open}";
                }
            }
            catch { }

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
                        _btnToggle.Text = dynamicUi["read_more"];
                    }
                    else
                    {
                        _btnToggle.IsVisible = false;
                    }
                }
            }
            catch { }
            try
            {
                var center = vinhKhanhMap?.VisibleRegion?.Center;
                var shouldMove = center == null
                    || HaversineDistanceMeters(center.Latitude, center.Longitude, poi.Latitude, poi.Longitude) > 25;

                if (shouldMove)
                {
                    vinhKhanhMap.MoveToRegion(MapSpan.FromCenterAndRadius(new Location(poi.Latitude, poi.Longitude), Distance.FromKilometers(0.1)));
                }
            }
            catch { }
        }

        // Helper to show list of highlights in a simple page (fallback if HighlightsListPage not present)
        private async Task ShowHighlightsListFallback(System.Collections.Generic.List<PoiModel> list)
        {
            try
            {
                // Build a simple action sheet with names if navigation to a page fails
                var names = list.Select(p => p.Name).Take(10).ToArray();
                var t = await GetDialogTextsAsync();
                var choice = await DisplayActionSheet(t["highlights_places"], t["close"], null, names);
                if (!string.IsNullOrEmpty(choice) && !string.Equals(choice, t["close"], StringComparison.OrdinalIgnoreCase))
                {
                    var poi = list.FirstOrDefault(p => p.Name == choice);
                if (poi != null)
                    {
                        _selectedPoi = poi;
                    _ = TrackPoiEventAsync("poi_click", poi.Id, $"\"trigger\":\"map_pin\",\"lang\":\"{NormalizeLanguageCode(_currentLanguage)}\"");
                        await ShowPoiDetail(poi, true);
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
                var readMoreText = _dynamicUiTextCache.TryGetValue($"ui:{NormalizeLanguageCode(_currentLanguage)}:read_more", out var rm)
                    ? rm
                    : "Read more";
                var readLessText = _dynamicUiTextCache.TryGetValue($"ui:{NormalizeLanguageCode(_currentLanguage)}:read_less", out var rl)
                    ? rl
                    : "Show less";
                if (_isDescriptionExpanded)
                {
                    if (LblDescription != null) LblDescription.MaxLines = int.MaxValue;
                    var _btnToggle2 = this.FindByName<Button>("BtnToggleDescription");
                    if (_btnToggle2 != null) _btnToggle2.Text = readLessText;
                }
                else
                {
                    if (LblDescription != null) LblDescription.MaxLines = 3;
                    var _btnToggle3 = this.FindByName<Button>("BtnToggleDescription");
                    if (_btnToggle3 != null) _btnToggle3.Text = readMoreText;
                }
            }
            catch { }
        }

        // Highlight nearest POI by updating pin labels (append " (Near)")
        private async Task HighlightNearestPoi(double? userLat = null, double? userLng = null)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
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
                                    var title = pin.Label?.Replace(" (Near)", string.Empty, StringComparison.Ordinal) ?? match.Name;
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
                });
            }
            catch { }
        }

        // Throttle map visible region changes to avoid heavy recompute while dragging
        private void OnMapVisibleRegionChanged(object sender, EventArgs e)
        {
            try
            {
                _mapMoveDebounceCts?.Cancel();
                _mapMoveDebounceCts?.Dispose();
                _mapMoveDebounceCts = new CancellationTokenSource();
                var token = _mapMoveDebounceCts.Token;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(250, token); // debounce interval
                        if (token.IsCancellationRequested) return;
                        // Update pins based on new visible region without blocking UI
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            try { AddPoisToMap(); } catch { }
                        });
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                });
            }
            catch { }
        }

        private void CenterMapOnVinhKhanh()
        {
            try
            {
                if (MainThread.IsMainThread)
                {
                    vinhKhanhMap?.MoveToRegion(MapSpan.FromCenterAndRadius(new Location(10.7584, 106.7058), Distance.FromKilometers(0.4)));
                    return;
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        vinhKhanhMap?.MoveToRegion(MapSpan.FromCenterAndRadius(new Location(10.7584, 106.7058), Distance.FromKilometers(0.4)));
                    }
                    catch { }
                });
            }
            catch { }
        }

        private async Task CenterMapOnUserFirstAsync()
        {
            try
            {
                var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)));
                if (location != null)
                {
                    _lastLocation = location;
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        try
                        {
                            vinhKhanhMap?.MoveToRegion(MapSpan.FromCenterAndRadius(location, Distance.FromKilometers(0.5)));
                        }
                        catch { }
                    });
                    return;
                }
            }
            catch { }

            CenterMapOnVinhKhanh();
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
                var t = await GetDialogTextsAsync();
                await DisplayAlert(t["error"], t["permission_denied_msg"], t["ok"]);
            }
        }

        private void OnZoomInClicked(object sender, EventArgs e)
        {
            ZoomMap(0.7);
        }

        private void OnZoomOutClicked(object sender, EventArgs e)
        {
            ZoomMap(1.4);
        }

        private void ZoomMap(double factor)
        {
            try
            {
                if (vinhKhanhMap == null || factor <= 0) return;

                var region = vinhKhanhMap.VisibleRegion;
                var center = region?.Center
                    ?? _lastLocation
                    ?? new Location(10.7584, 106.7058);

                // Radius-based zoom is more stable across devices/emulator than raw LatitudeDegrees fallback.
                var currentRadiusKm = region != null
                    ? Math.Clamp((region.LatitudeDegrees * 111d) / 2d, 0.03, 20d)
                    : 0.15d;

                var nextRadiusKm = Math.Clamp(currentRadiusKm * factor, 0.03, 20d);
                vinhKhanhMap.MoveToRegion(MapSpan.FromCenterAndRadius(center, Distance.FromKilometers(nextRadiusKm)));
            }
            catch { }
        }

        private async void OnForceSyncNowClicked(object sender, EventArgs e)
        {
            if (_realtimeSyncManager == null)
            {
                var t = await GetDialogTextsAsync();
                await DisplayAlert(t["sync"], t["sync_service_missing"], t["ok"]);
                return;
            }

            try
            {
                if (BtnForceSyncNow != null)
                {
                    var syncDialog = await GetDialogTextsAsync();
                    BtnForceSyncNow.IsEnabled = false;
                    BtnForceSyncNow.Text = syncDialog["syncing"];
                }

                var syncedCount = await RunFastPoiSyncAndApplyUiAsync();
                var syncText = await GetDialogTextsAsync();
                AddLog(string.Format(syncText["sync_done_log"], syncedCount));

                if (_selectedPoi != null && PoiDetailPanel?.IsVisible == true)
                {
                    await ShowPoiDetail(_selectedPoi);
                }

                if (_backgroundFullSyncTask == null || _backgroundFullSyncTask.IsCompleted)
                {
                    _backgroundFullSyncTask = Task.Run(async () =>
                    {
                        try
                        {
                            await RunSingleFullSyncAndApplyUiAsync();
                        }
                        catch { }
                    });
                }

                var t = await GetDialogTextsAsync();
                await DisplayAlert(t["sync"], t["sync_success"], t["ok"]);
            }
            catch (Exception ex)
            {
                AddLog($"Force sync failed: {ex.Message}");
                var t = await GetDialogTextsAsync();
                await DisplayAlert(t["sync"], t["sync_failed"], t["ok"]);
            }
            finally
            {
                if (BtnForceSyncNow != null)
                {
                    BtnForceSyncNow.IsEnabled = true;
                    var ui = await BuildDynamicUiTextAsync(_currentLanguage);
                    BtnForceSyncNow.Text = ui["force_sync_now"];
                }
            }
        }

        private async Task<int> RunFastPoiSyncAndApplyUiAsync()
        {
            await _fullSyncGate.WaitAsync();
            try
            {
                await EnsureApiBaseReadyAsync();

                var lang = NormalizeLanguageCode(_currentLanguage);
                var loadAll = await _apiService.GetPoisLoadAllAsync(lang)
                              ?? await _apiService.GetPoisLoadAllAsync("vi");

                var serverPois = loadAll?.Items?
                    .Select(i => i?.Poi)
                    .Where(p => p != null)
                    .GroupBy(p => p.Id)
                    .Select(g => g.First())
                    .ToList() ?? new List<PoiModel>();

                if (!serverPois.Any())
                {
                    serverPois = await _apiService.GetPoisAsync() ?? new List<PoiModel>();
                }

                // Save POIs in parallel with limited concurrency to speed up sync without overwhelming device
                var semaphore = new SemaphoreSlim(Environment.ProcessorCount >= 4 ? 8 : 4);
                var saveTasks = new List<Task>();
                foreach (var poi in serverPois)
                {
                    await semaphore.WaitAsync();
                    var p = poi;
                    saveTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await _dbService.SavePoiAsync(p);
                        }
                        catch { }
                        finally
                        {
                            try { semaphore.Release(); } catch { }
                        }
                    }));
                }

                try { await Task.WhenAll(saveTasks); } catch { }

                var syncedPois = await _dbService.GetPoisAsync();
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    _pois = syncedPois ?? new List<PoiModel>();
                    AddPoisToMap();
                    try { BtnShowSaved.IsVisible = _pois.Any(p => p.IsSaved); } catch { }
                    var highlights = _pois.OrderByDescending(p => p.Priority).Take(6).ToList();
                    await RenderHighlightsAsync(highlights);
                });

                return _pois?.Count ?? 0;
            }
            finally
            {
                _fullSyncGate.Release();
            }
        }


        // Make handler public so XAML loader can find it reliably
        public void OnMenuClicked(object sender, EventArgs e)
        {
            // If a POI detail is open, close it when opening the language menu
            if (PoiDetailPanel != null && PoiDetailPanel.IsVisible)
                PoiDetailPanel.IsVisible = false;

            if (HighlightsPanel != null)
                HighlightsPanel.IsVisible = false;

            if (GpsButtonFrame != null)
                GpsButtonFrame.IsVisible = false;

            _isLanguageModalOpen = true;
            LanguagePanel.IsVisible = true;
            // Update the visual state of language buttons
            UpdateLanguageSelectionUI();
        }

        private void OnCloseMenuClicked(object sender, EventArgs e)
        {
            _isLanguageModalOpen = false;
            LanguagePanel.IsVisible = false;

            try
            {
                if (GpsButtonFrame != null)
                    GpsButtonFrame.IsVisible = true;

                if (HighlightsPanel != null && _selectedPoi == null)
                    HighlightsPanel.IsVisible = true;
            }
            catch { }
        }

        // Tabs click handlers
        private void OnTabOverviewClicked(object sender, EventArgs e)
        {
            try
            {
                var overview = this.FindByName<VisualElement>("OverviewPanel");
                var intro = this.FindByName<VisualElement>("IntroPanel");
                var review = this.FindByName<VisualElement>("ReviewPanel");
                var tabO = this.FindByName<Button>("TabOverview");
                var tabI = this.FindByName<Button>("TabIntro");
                var tabR = this.FindByName<Button>("TabReview");
                if (overview != null) overview.IsVisible = true;
                if (intro != null) intro.IsVisible = false;
                if (review != null) review.IsVisible = false;
                if (tabO != null) tabO.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#00796B");
                if (tabI != null) tabI.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                if (tabR != null) tabR.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                if (tabO != null) tabO.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#E3F2FD");
                if (tabI != null) tabI.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent");
                if (tabR != null) tabR.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent");
            }
            catch { }
        }

        private void OnTabIntroClicked(object sender, EventArgs e)
        {
            try
            {
                var overview = this.FindByName<VisualElement>("OverviewPanel");
                var intro = this.FindByName<VisualElement>("IntroPanel");
                var review = this.FindByName<VisualElement>("ReviewPanel");
                var tabO = this.FindByName<Button>("TabOverview");
                var tabI = this.FindByName<Button>("TabIntro");
                var tabR = this.FindByName<Button>("TabReview");
                if (overview != null) overview.IsVisible = false;
                if (intro != null) intro.IsVisible = true;
                if (review != null) review.IsVisible = false;
                if (tabO != null) tabO.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                if (tabI != null) tabI.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#00796B");
                if (tabR != null) tabR.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                if (tabI != null) tabI.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#E3F2FD");
                if (tabO != null) tabO.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent");
                if (tabR != null) tabR.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent");
            }
            catch { }
        }

        private void OnTabReviewClicked(object sender, EventArgs e)
        {
            try
            {
                var overview = this.FindByName<VisualElement>("OverviewPanel");
                var intro = this.FindByName<VisualElement>("IntroPanel");
                var review = this.FindByName<VisualElement>("ReviewPanel");
                var tabO = this.FindByName<Button>("TabOverview");
                var tabI = this.FindByName<Button>("TabIntro");
                var tabR = this.FindByName<Button>("TabReview");
                if (overview != null) overview.IsVisible = false;
                if (intro != null) intro.IsVisible = false;
                if (review != null) review.IsVisible = true;
                if (tabO != null) tabO.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                if (tabI != null) tabI.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                if (tabR != null) tabR.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#00796B");
                if (tabR != null) tabR.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#E3F2FD");
                if (tabO != null) tabO.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent");
                if (tabI != null) tabI.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent");
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
            _selectedPoi = null;

            try
            {
                if (!_isLanguageModalOpen && HighlightsPanel != null)
                {
                    HighlightsPanel.IsVisible = true;
                    SetHighlightsExpandedState(_isHighlightsExpanded);
                }
            }
            catch { }

            try
            {
                if (CvHighlights != null)
                {
                    CvHighlights.SelectedItem = null;
                }
            }
            catch { }
        }

        private async void OnStartNarrationClicked(object sender, EventArgs e)
        {
            if (_selectedPoi == null) return;

            // "Audio" action: mở danh sách MP3 theo ngôn ngữ hiện tại
            await ShowAudioListForCurrentLanguageAsync();
        }

        private async void OnPlayPoiAudioClicked(object sender, EventArgs e)
        {
            if (_selectedPoi == null) return;

            try
            {
                // "Nghe ngay": ưu tiên TTS theo đúng ngôn ngữ user đang chọn
                var preferredLang = NormalizeLanguageCode(_currentLanguage);
                var audios = await _apiService.GetAudiosByPoiIdAsync(_selectedPoi.Id) ?? new List<AudioModel>();
                await TrackPoiEventAsync("listen_start", _selectedPoi.Id, $"\"trigger\":\"audio_tab\",\"lang\":\"{preferredLang}\"");
                var selectedTts = SelectBestAudioByLanguage(audios, preferredLang, isTts: true);
                if (selectedTts != null)
                {
                    var ttsUrl = ToAbsoluteApiUrl(selectedTts.Url);
                    if (!string.IsNullOrWhiteSpace(ttsUrl))
                    {
                        var ttsItem = new AudioItem
                        {
                            Key = $"tts-remote:{_selectedPoi.Id}:{preferredLang}:{selectedTts.Id}",
                            IsTts = false,
                            FilePath = ttsUrl,
                            Language = NormalizeLanguageCode(selectedTts.LanguageCode),
                            PoiId = _selectedPoi.Id,
                            Priority = _selectedPoi?.Priority ?? 0
                        };
                        _audioQueue.Enqueue(ttsItem);
                        await TrackPoiEventAsync("tts_play", _selectedPoi.Id, $"\"mode\":\"remote_tts\",\"lang\":\"{NormalizeLanguageCode(selectedTts.LanguageCode)}\"");
                        return;
                    }
                }

                // fallback: ưu tiên ngôn ngữ hiện tại, sau đó fallback English
                var content = await GetStrictContentForLanguageAsync(_selectedPoi.Id, preferredLang);
                var text = content?.Description;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    await PlayNarration(text, _selectedPoi?.Priority ?? 0);
                    return;
                }

                var t = await GetDialogTextsAsync();
                await DisplayAlert(t["audio"], t["no_tts_for_lang"], t["ok"]);
            }
            catch
            {
                try { var t = await GetDialogTextsAsync(); await DisplayAlert(t["audio"], t["cannot_play_tts"], t["ok"]); } catch { }
            }
        }

        private async Task<ContentModel?> GetStrictContentForLanguageAsync(int poiId, string language)
        {
            try
            {
                var preferredLang = NormalizeLanguageCode(language);

                foreach (var lang in GetLanguageFallbackChain(preferredLang, includeVi: false))
                {
                    var local = await _dbService.GetContentByPoiIdAsync(poiId, lang);
                    if (local != null && !string.IsNullOrWhiteSpace(local.Description))
                    {
                        return local;
                    }
                }

                // API exact language only
                var fromApi = await _apiService.GetContentsByPoiIdAsync(poiId) ?? new List<ContentModel>();
                foreach (var lang in GetLanguageFallbackChain(preferredLang, includeVi: false))
                {
                    var matched = fromApi.FirstOrDefault(c => c != null
                                                              && NormalizeLanguageCode(c.LanguageCode) == lang
                                                              && !string.IsNullOrWhiteSpace(c.Description));
                    if (matched != null)
                    {
                        try { await _dbService.SaveContentAsync(matched); } catch { }
                        return matched;
                    }
                }

                // If API/local still missing, generate translated copy from VI/EN so selected language remains consistent.
                var source = await _dbService.GetContentByPoiIdAsync(poiId, "vi")
                             ?? await _dbService.GetContentByPoiIdAsync(poiId, "en");
                var translated = await BuildTranslatedContentAsync(source, poiId, preferredLang);
                if (translated != null && HasMeaningfulContent(translated))
                {
                    try { await _dbService.SaveContentAsync(translated); } catch { }
                    return translated;
                }
            }
            catch { }

            return null;
        }

        private async Task<string> LocalizeFreeTextAsync(string? source, string language)
        {
            if (string.IsNullOrWhiteSpace(source)) return string.Empty;

            var normalizedLang = NormalizeLanguageCode(language);
            var cacheKey = $"txt:{normalizedLang}:{source.Trim()}";
            if (_dynamicUiTextCache.TryGetValue(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
            {
                return cached;
            }

            var translated = await TranslateTextAsync(source, normalizedLang);
            var value = string.IsNullOrWhiteSpace(translated) ? source : translated;
            _dynamicUiTextCache[cacheKey] = value;
            return value;
        }

        private async Task ShowAudioListForCurrentLanguageAsync()
        {
            try
            {
                if (_selectedPoi == null) return;

                var preferredLang = NormalizeLanguageCode(_currentLanguage);
                var audios = await _apiService.GetAudiosByPoiIdAsync(_selectedPoi.Id) ?? new List<AudioModel>();
                await TrackPoiEventAsync("listen_start", _selectedPoi.Id, $"\"trigger\":\"audio_tab\",\"lang\":\"{preferredLang}\"");

                var uploadedByLang = SelectAudioListByLanguage(audios, preferredLang, isTts: false);

                if (!uploadedByLang.Any())
                {
                    var ttsByLang = SelectAudioListByLanguage(audios, preferredLang, isTts: true);

                    if (ttsByLang.Any())
                    {
                        var selectedTts = ttsByLang.First();
                        var ttsUrl = ToAbsoluteApiUrl(selectedTts.Url);
                        if (!string.IsNullOrWhiteSpace(ttsUrl))
                        {
                            var ttsItem = new AudioItem
                            {
                                Key = $"tts-fallback:{_selectedPoi.Id}:{preferredLang}:{selectedTts.Id}",
                                IsTts = false,
                                FilePath = ttsUrl,
                                Language = NormalizeLanguageCode(selectedTts.LanguageCode),
                                PoiId = _selectedPoi.Id,
                                Priority = _selectedPoi?.Priority ?? 0
                            };
                            _audioQueue.Enqueue(ttsItem);
                            await TrackPoiEventAsync("tts_play", _selectedPoi.Id, $"\"mode\":\"tts_fallback\",\"trigger\":\"audio_tab\",\"lang\":\"{NormalizeLanguageCode(selectedTts.LanguageCode)}\"");
                            return;
                        }
                    }

                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["audio"], t["no_audio_for_lang"], t["ok"]);
                    return;
                }

                var selected = uploadedByLang.FirstOrDefault();
                if (selected == null) return;

                var playUrl = ToAbsoluteApiUrl(selected.Url);
                if (string.IsNullOrWhiteSpace(playUrl))
                {
                    var t3 = await GetDialogTextsAsync();
                    await DisplayAlert(t3["audio"], t3["invalid_audio_file"], t3["ok"]);
                    return;
                }
                var item = new AudioItem
                {
                    Key = $"mp3:{_selectedPoi.Id}:{preferredLang}:{selected.Id}",
                    IsTts = false,
                    FilePath = playUrl,
                    Language = NormalizeLanguageCode(selected.LanguageCode),
                    PoiId = _selectedPoi.Id,
                    Priority = _selectedPoi?.Priority ?? 0
                };
                _audioQueue.Enqueue(item);
                await TrackPoiEventAsync("audio_play", _selectedPoi.Id, $"\"mode\":\"mp3\",\"trigger\":\"audio_tab\",\"lang\":\"{NormalizeLanguageCode(selected.LanguageCode)}\"");
            }
            catch
            {
                try { var t = await GetDialogTextsAsync(); await DisplayAlert(t["audio"], t["cannot_load_audio_list"], t["ok"]); } catch { }
            }
        }

        private string BuildAudioOptionLabel(AudioModel audio)
        {
            var source = string.IsNullOrWhiteSpace(audio?.Url) ? string.Empty : audio.Url;
            var withoutQuery = source.Split('?', '#')[0];
            var name = System.IO.Path.GetFileName(withoutQuery);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Audio #{audio?.Id ?? 0}";
            }
            return name;
        }

        private string NormalizeLanguageCode(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return "vi";
            var normalized = language.Trim().ToLowerInvariant();

            // Normalize culture-like tags (en-US -> en)
            if (normalized.Contains('-'))
            {
                normalized = normalized.Split('-')[0];
            }
            else if (normalized.Contains('_'))
            {
                normalized = normalized.Split('_')[0];
            }

            return normalized;
        }

        private IEnumerable<string> GetLanguageFallbackChain(string language, bool includeVi)
        {
            var preferred = NormalizeLanguageCode(language);
            var ordered = new List<string>();

            void AddIfNeeded(string lang)
            {
                if (string.IsNullOrWhiteSpace(lang)) return;
                if (ordered.Any(x => string.Equals(x, lang, StringComparison.OrdinalIgnoreCase))) return;
                ordered.Add(lang);
            }

            AddIfNeeded(preferred);
            if (!string.Equals(preferred, "en", StringComparison.OrdinalIgnoreCase)) AddIfNeeded("en");
            if (includeVi && !string.Equals(preferred, "vi", StringComparison.OrdinalIgnoreCase)) AddIfNeeded("vi");

            return ordered;
        }

        private List<AudioModel> SelectAudioListByLanguage(IEnumerable<AudioModel> source, string preferredLanguage, bool isTts)
        {
            var audios = source?
                .Where(a => a != null && a.IsTts == isTts && !string.IsNullOrWhiteSpace(a.Url))
                .OrderByDescending(a => a.IsProcessed)
                .ThenByDescending(a => a.CreatedAtUtc)
                .ThenByDescending(a => a.Id)
                .ToList() ?? new List<AudioModel>();

            foreach (var lang in GetLanguageFallbackChain(preferredLanguage, includeVi: false))
            {
                var matched = audios
                    .Where(a => NormalizeLanguageCode(a.LanguageCode) == lang)
                    .ToList();
                if (matched.Any()) return matched;
            }

            return audios;
        }

        private AudioModel? SelectBestAudioByLanguage(IEnumerable<AudioModel> source, string preferredLanguage, bool isTts)
        {
            return SelectAudioListByLanguage(source, preferredLanguage, isTts).FirstOrDefault();
        }

        private async Task PrimeRemoteAudioCacheAsync(string absoluteUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(absoluteUrl)) return;
                if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri)) return;

                var fileName = System.IO.Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrWhiteSpace(fileName)) fileName = $"audio_{Guid.NewGuid():N}.mp3";

                var localPath = System.IO.Path.Combine(FileSystem.AppDataDirectory, fileName);
                if (System.IO.File.Exists(localPath)) return;

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                var bytes = await http.GetByteArrayAsync(uri);
                if (bytes != null && bytes.Length > 0)
                {
                    await System.IO.File.WriteAllBytesAsync(localPath, bytes);
                }
            }
            catch { }
        }

        private string ToAbsoluteApiUrl(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return string.Empty;

            // Emulator-first media authority to avoid HTTPS/self-signed and localhost/LAN mismatch issues.
            if (DeviceInfo.Platform == DevicePlatform.Android && DeviceInfo.DeviceType == DeviceType.Virtual)
            {
                if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var absOnEmu))
                {
                    return $"http://10.0.2.2:5291{absOnEmu.PathAndQuery}";
                }

                var emuPath = rawUrl.StartsWith("/") ? rawUrl : "/" + rawUrl;
                return $"http://10.0.2.2:5291{emuPath}";
            }

            var apiCurrentBase = _apiService?.CurrentBaseUrl;
            if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var absolute))
            {
                // Absolute URL returned by API may contain LAN/localhost that emulator cannot reach.
                // Normalize to current active API authority for runtime stability.
                if (!string.IsNullOrWhiteSpace(apiCurrentBase)
                    && Uri.TryCreate(apiCurrentBase, UriKind.Absolute, out var currentApiUri)
                    && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
                {
                    var currentAuthority = currentApiUri.GetLeftPart(UriPartial.Authority);
                    var absolutePathAndQuery = absolute.PathAndQuery;
                    return $"{currentAuthority}{absolutePathAndQuery}";
                }

                return absolute.ToString();
            }

            if (!string.IsNullOrWhiteSpace(apiCurrentBase)
                && Uri.TryCreate(apiCurrentBase, UriKind.Absolute, out var apiCurrentUri))
            {
                var authorityFromApi = apiCurrentUri.GetLeftPart(UriPartial.Authority);
                var pathFromApi = rawUrl.StartsWith("/") ? rawUrl : "/" + rawUrl;
                return $"{authorityFromApi}{pathFromApi}";
            }

            var preferredBase = Preferences.Default.Get("ApiBaseUrl", string.Empty);
            if (string.IsNullOrWhiteSpace(preferredBase))
            {
                preferredBase = Preferences.Default.Get("VinhKhanh_ApiBaseUrl", string.Empty);
            }

            string authority;
            if (!string.IsNullOrWhiteSpace(preferredBase) && Uri.TryCreate(preferredBase, UriKind.Absolute, out var preferredUri))
            {
                authority = preferredUri.GetLeftPart(UriPartial.Authority);
            }
            else
            {
                // Emu phải ưu tiên 10.0.2.2 để đọc đúng API local chứa dữ liệu admin
                if (DeviceInfo.Platform == DevicePlatform.Android && DeviceInfo.DeviceType == DeviceType.Virtual)
                {
                    authority = "http://10.0.2.2:5291";
                }
                else
                {
                    authority = "http://localhost:5291";
                }
            }

            var normalizedPath = rawUrl.StartsWith("/") ? rawUrl : "/" + rawUrl;
            return $"{authority}{normalizedPath}";
        }

        private async void OnGetDirectionsClicked(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPoi == null)
                {
                    await TryRestoreSelectedPoiFromUiAsync();
                }

                if (_selectedPoi == null)
                {
                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["error"], t["no_selected_poi_directions"], t["ok"]);
                    return;
                }

                var lat = _selectedPoi.Latitude;
                var lng = _selectedPoi.Longitude;
                var label = Uri.EscapeDataString(_selectedPoi.Name ?? "Destination");
                _pendingNavigationPoiId = _selectedPoi.Id;
                Location? currentLocation = null;
                try
                {
                    currentLocation = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)));
                }
                catch { }

                if (currentLocation == null)
                {
                    currentLocation = _lastLocation;
                }

                var originLat = currentLocation?.Latitude;
                var originLng = currentLocation?.Longitude;

                string uri = null;
                try
                {
                    if (DeviceInfo.Platform == DevicePlatform.iOS)
                    {
                        uri = originLat.HasValue && originLng.HasValue
                            ? $"http://maps.apple.com/?saddr={originLat.Value},{originLng.Value}&daddr={lat},{lng}"
                            : $"http://maps.apple.com/?daddr={lat},{lng}";
                    }
                    else if (DeviceInfo.Platform == DevicePlatform.Android)
                    {
                        uri = originLat.HasValue && originLng.HasValue
                            ? $"https://www.google.com/maps/dir/?api=1&origin={originLat.Value},{originLng.Value}&destination={lat},{lng}&travelmode=driving"
                            : $"geo:{lat},{lng}?q={label}";
                    }
                    else
                    {
                        // Fallback to Google Maps web
                        uri = originLat.HasValue && originLng.HasValue
                            ? $"https://www.google.com/maps/dir/?api=1&origin={originLat.Value},{originLng.Value}&destination={lat},{lng}&travelmode=driving"
                            : $"https://www.google.com/maps/dir/?api=1&destination={lat},{lng}";
                    }

                    AddLog($"Điều hướng: mở bản đồ tới {_selectedPoi.Name}");
                    await Launcher.OpenAsync(new Uri(uri));
                    _ = TrackPoiEventAsync("navigation_start", _selectedPoi.Id, $"\"trigger\":\"map_directions\",\"lang\":\"{NormalizeLanguageCode(_currentLanguage)}\"");

                    // phản hồi rõ ràng để user biết đã gọi điều hướng
                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["directions"], string.Format(t["opening_directions_to"], _selectedPoi.Name), t["ok"]);
                }
                catch (Exception ex)
                {
                    // fallback: open google maps web
                    try
                    {
                        var web = originLat.HasValue && originLng.HasValue
                            ? $"https://www.google.com/maps/dir/?api=1&origin={originLat.Value},{originLng.Value}&destination={lat},{lng}&travelmode=driving"
                            : $"https://www.google.com/maps/dir/?api=1&destination={lat},{lng}";
                        AddLog($"Điều hướng fallback: {_selectedPoi.Name} - {ex.Message}");
                        await Launcher.OpenAsync(new Uri(web));
                        var t = await GetDialogTextsAsync();
                        await DisplayAlert(t["directions"], t["opened_web_directions"], t["ok"]);
                    }
                    catch
                    {
                        var t = await GetDialogTextsAsync();
                        await DisplayAlert(t["directions"], t["cannot_open_directions"], t["ok"]);
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"OnGetDirectionsClicked lỗi: {ex.Message}");
            }
        }

        private async void OnWebsiteTapped(object sender, EventArgs e)
        {
            try
            {
                var url = LblWebsite?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(url)) return;

                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }

                await Launcher.OpenAsync(new Uri(url));
            }
            catch { }
        }

        private async void OnPhoneTapped(object sender, EventArgs e)
        {
            try
            {
                var phone = LblPhone?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(phone)) return;
                await Launcher.OpenAsync(new Uri($"tel:{phone}"));
            }
            catch { }
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
                var t = await GetDialogTextsAsync();
                var choice = await DisplayActionSheet(t["saved_places"], t["cancel"], null, names);
                if (!string.IsNullOrEmpty(choice) && !string.Equals(choice, t["cancel"], StringComparison.OrdinalIgnoreCase))
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

        private async void OnSearchPoiTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _searchDebounceCts?.Cancel();
                _searchDebounceCts?.Dispose();
                _searchDebounceCts = new CancellationTokenSource();
                var token = _searchDebounceCts.Token;

                var keyword = (e.NewTextValue ?? string.Empty).Trim();
                _lastSearchKeyword = keyword;

                try
                {
                    await Task.Delay(280, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested) return;

                if (string.IsNullOrWhiteSpace(keyword))
                {
                    var defaultHighlights = _pois.OrderByDescending(p => p.Priority).Take(6).ToList();
                    await RenderHighlightsAsync(defaultHighlights);
                    return;
                }

                var results = await SearchPoisAsync(keyword);
                await RenderHighlightsAsync(results.Take(10));
            }
            catch { }
        }

        private async void OnSearchPoiSearchButtonPressed(object sender, EventArgs e)
        {
            try
            {
                var searchBar = this.FindByName<SearchBar>("SearchPoiBar");
                var keyword = searchBar?.Text?.Trim() ?? _lastSearchKeyword;
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    return;
                }

                var results = await SearchPoisAsync(keyword);
                if (!results.Any())
                {
                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["search"], t["search_not_found"], t["ok"]);
                    return;
                }

                if (results.Count == 1)
                {
                    _selectedPoi = results[0];
                    await ShowPoiDetail(results[0], true);
                    return;
                }

                var options = results.Take(8).Select(p => $"#{p.Id} - {p.Name}").ToArray();
                var t2 = await GetDialogTextsAsync();
                var picked = await DisplayActionSheet(t2["choose_poi"], t2["cancel"], null, options);
                if (string.IsNullOrWhiteSpace(picked) || picked == t2["cancel"]) return;

                var selected = results.FirstOrDefault(p => string.Equals($"#{p.Id} - {p.Name}", picked, StringComparison.Ordinal));
                if (selected == null) return;

                _selectedPoi = selected;
                await ShowPoiDetail(selected, true);
            }
            catch { }
        }

        private async Task<List<PoiModel>> SearchPoisAsync(string keyword)
        {
            var results = new List<PoiModel>();
            if (string.IsNullOrWhiteSpace(keyword) || _pois == null || !_pois.Any()) return results;

            var query = keyword.Trim();
            foreach (var poi in _pois)
            {
                if (ContainsIgnoreCase(poi.Name, query))
                {
                    results.Add(poi);
                    continue;
                }

                try
                {
                    var localized = await _dbService.GetContentByPoiIdAsync(poi.Id, _currentLanguage);
                    var en = await _dbService.GetContentByPoiIdAsync(poi.Id, "en");

                    if (ContainsIgnoreCase(localized?.Title, query) || ContainsIgnoreCase(en?.Title, query))
                    {
                        results.Add(poi);
                    }
                }
                catch { }
            }

            return results
                .DistinctBy(p => p.Id)
                .OrderByDescending(p => p.Priority)
                .ThenBy(p => p.Name)
                .ToList();
        }

        private static bool ContainsIgnoreCase(string? source, string keyword)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(keyword)) return false;
            return source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
        }

        private async Task RenderHighlightsAsync(IEnumerable<PoiModel> sourcePois)
        {
            try
            {
                var normalizedSource = (sourcePois ?? Enumerable.Empty<PoiModel>())
                    .Where(p => p != null)
                    .DistinctBy(p => p.Id)
                    .OrderByDescending(p => p.Priority)
                    .ToList();

                if (!normalizedSource.Any())
                {
                    await EnsurePoiDataReadyAsync();
                    normalizedSource = (_pois ?? new List<PoiModel>())
                        .Where(p => p != null)
                        .DistinctBy(p => p.Id)
                        .OrderByDescending(p => p.Priority)
                        .Take(12)
                        .ToList();
                }

                var vmColl = new System.Collections.ObjectModel.ObservableCollection<VinhKhanh.Shared.HighlightViewModel>();
                var pois = normalizedSource;
                var ui = await BuildDynamicUiTextAsync(_currentLanguage);

                await EnsureLiveStatsCacheAsync();

                pois = pois
                    .OrderByDescending(p => _liveStatsByPoiId.TryGetValue(p.Id, out var s) && s.IsHot)
                    .ThenByDescending(p => _liveStatsByPoiId.TryGetValue(p.Id, out var s) ? s.ActiveUsers : 0)
                    .ThenByDescending(p => _liveStatsByPoiId.TryGetValue(p.Id, out var s) ? s.QrScanCount : 0)
                    .ThenByDescending(p => p.Priority)
                    .ToList();

                var contentMap = new Dictionary<int, ContentModel?>();
                foreach (var poi in pois)
                {
                    var preferred = await _dbService.GetContentByPoiIdAsync(poi.Id, NormalizeLanguageCode(_currentLanguage))
                                   ?? await _dbService.GetContentByPoiIdAsync(poi.Id, "en")
                                   ?? await _dbService.GetContentByPoiIdAsync(poi.Id, "vi");
                    contentMap[poi.Id] = preferred;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var needHydrate = pois.Where(p =>
                            !contentMap.TryGetValue(p.Id, out var c) || !HasMeaningfulContent(c))
                            .Take(4)
                            .ToList();

                        foreach (var poi in needHydrate)
                        {
                            await HydrateContentsFromApiAsync(poi.Id);
                        }

                        if (needHydrate.Any())
                        {
                            await MainThread.InvokeOnMainThreadAsync(async () =>
                            {
                                if (_isLanguageModalOpen) return;
                                var src = (_lastSearchKeyword?.Length > 0)
                                    ? await SearchPoisAsync(_lastSearchKeyword)
                                    : (_pois ?? new List<PoiModel>()).OrderByDescending(p => p.Priority).Take(6).ToList();
                                await RenderHighlightsAsync(src.Take(10));
                            });
                        }
                    }
                    catch { }
                });

                foreach (var h in pois)
                {
                    contentMap.TryGetValue(h.Id, out var content);
                    var resolvedImageUrl = ResolveHighlightImageUrl(h.ImageUrl);
                    var cachedImage = await ResolveHighlightImageSourceAsync(resolvedImageUrl, h.Id);
                    var openStatus = string.Empty;
                    var openColorHex = "#9E9E9E";
                    if (content != null && !string.IsNullOrEmpty(content.OpeningHours))
                    {
                        var parts = content.OpeningHours.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                        if (parts.Length == 2 && TimeSpan.TryParse(parts[0], out var s) && TimeSpan.TryParse(parts[1], out var e))
                        {
                            var now = DateTime.Now.TimeOfDay;
                            var isOpen = s <= e ? now >= s && now <= e : now >= s || now <= e;
                            openStatus = isOpen ? ui["open_now"] : ui["closed"];
                            openColorHex = isOpen ? "#388E3C" : "#D32F2F";
                        }
                    }

                    vmColl.Add(new VinhKhanh.Shared.HighlightViewModel
                    {
                        Poi = h,
                        ImageUrl = cachedImage,
                        Name = content?.Title ?? h.Name,
                        Category = await LocalizeFreeTextAsync(h.Category, _currentLanguage),
                        Address = content?.Address ?? string.Empty,
                        RatingDisplay = content != null ? (content.Rating > 0 ? string.Format("{0:0.0} ★", content.Rating) : ui["no_rating"]) : ui["no_rating"],
                        PriceDisplay = content?.GetNormalizedPriceRangeDisplay() ?? string.Empty,
                        ReviewCount = content != null && content.Rating > 0 ? Math.Max(1, (int)Math.Round(content.Rating * 20)) : 0,
                        OpeningHours = content?.OpeningHours ?? string.Empty,
                        OpenStatus = openStatus,
                        OpenStatusColorHex = openColorHex,
                        BadgeText = (_liveStatsByPoiId.TryGetValue(h.Id, out var live) && live.IsHot)
                            ? "HOT"
                            : (content?.Rating > 0 ? $"★ {content.Rating:0.0}" : ui["new_badge"]),
                        PopularHint = (_liveStatsByPoiId.TryGetValue(h.Id, out var liveHint) && liveHint.IsHot)
                            ? ui["popular_hint_hot"]
                            : string.Empty,
                        IsHot = _liveStatsByPoiId.TryGetValue(h.Id, out var isHotLive) && isHotLive.IsHot
                    });
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    CvHighlights.ItemsSource = vmColl;
                    try { CvHighlights.SelectedItem = null; } catch { }
                    HighlightsPanel.IsVisible = true;
                    if (vmColl.Any())
                    {
                        SetHighlightsExpandedState(_isHighlightsExpanded);
                    }
                });

                _ = PrimeHighlightImagesCacheAsync(pois);
            }
            catch { }
        }

        private async Task EnsureLiveStatsCacheAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                if (_liveStatsByPoiId.Count > 0 && (now - _lastLiveStatsFetchUtc).TotalSeconds < 25)
                {
                    return;
                }

                await EnsureApiBaseReadyAsync();
                var stats = await _apiService.GetPoiLiveStatsAsync(_lastLocation?.Latitude, _lastLocation?.Longitude, 100) ?? new List<PoiLiveStatsDto>();
                _liveStatsByPoiId = stats
                    .Where(x => x != null && x.PoiId > 0)
                    .GroupBy(x => x.PoiId)
                    .ToDictionary(g => g.Key, g => g.First());
                _lastLiveStatsFetchUtc = now;
            }
            catch { }
        }

        private async Task PrimeHighlightImagesCacheAsync(IEnumerable<PoiModel> pois)
        {
            try
            {
                var list = (pois ?? Enumerable.Empty<PoiModel>()).Where(p => p != null).Take(12).ToList();
                // Prefetch images in parallel but limit concurrency to reduce network/IO contention
                var sem = new SemaphoreSlim(3);
                var tasks = new List<Task>();
                foreach (var poi in list)
                {
                    await sem.WaitAsync();
                    var p = poi;
                    tasks.Add(Task.Run(async () =>
                    {
                        try { await ResolveHighlightImageSourceAsync(p.ImageUrl, p.Id); } catch { }
                        finally { try { sem.Release(); } catch { } }
                    }));
                }
                try { await Task.WhenAll(tasks); } catch { }
                await Task.CompletedTask;
            }
            catch { }
        }

        private async Task<string> ResolveHighlightImageSourceAsync(string? source, int poiId)
        {
            if (string.IsNullOrWhiteSpace(source)) return source ?? string.Empty;

            var raw = ResolveHighlightImageUrl(source);
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            {
                return raw;
            }

            // Bypass local cache for uploaded POI images to reflect Admin changes immediately.
            var isUploadedImage = uri.AbsolutePath.Contains("/uploads/", StringComparison.OrdinalIgnoreCase);

            if (!isUploadedImage && _highlightImageCache.TryGetValue(raw, out var existing) && !string.IsNullOrWhiteSpace(existing) && System.IO.File.Exists(existing))
            {
                return existing;
            }

            var cacheDir = System.IO.Path.Combine(FileSystem.CacheDirectory, "highlight-img");
            Directory.CreateDirectory(cacheDir);

            var ext = System.IO.Path.GetExtension(uri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5) ext = ".jpg";
            var key = ComputeStableHash(raw);
            var localPath = System.IO.Path.Combine(cacheDir, $"poi_{poiId}_{key}{ext}");

            if (!isUploadedImage && System.IO.File.Exists(localPath))
            {
                _highlightImageCache[raw] = localPath;
                return localPath;
            }

            try
            {
                await _highlightImageDownloadGate.WaitAsync();

                if (!isUploadedImage && System.IO.File.Exists(localPath))
                {
                    _highlightImageCache[raw] = localPath;
                    return localPath;
                }

                var bytes = await _highlightImageHttpClient.GetByteArrayAsync(uri);
                if (bytes != null && bytes.Length > 0)
                {
                    await System.IO.File.WriteAllBytesAsync(localPath, bytes);
                    _highlightImageCache[raw] = localPath;
                    return localPath;
                }
            }
            catch { }
            finally
            {
                try { _highlightImageDownloadGate.Release(); } catch { }
            }

            return raw;
        }

        private string ResolveHighlightImageUrl(string? source)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(source)) return string.Empty;

                var raw = source
                    .Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x?.Trim())
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                    ?? source.Trim();

                return ToAbsoluteApiUrl(raw);
            }
            catch
            {
                return source ?? string.Empty;
            }
        }

        private static string ComputeStableHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty));
            return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
        }

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
        private Task PlayNarration(string text, int priority = -1)
        {
            // Use AudioQueueService to manage TTS and audio items (prevents duplicates, handles priority)
            try
            {
                var effectivePriority = priority >= 0 ? priority : (_selectedPoi?.Priority ?? 0);
                var normalizedLang = NarrationService.NormalizeLanguageCode(_currentLanguage);
                var key = _selectedPoi != null ? $"poi:{_selectedPoi.Id}:{normalizedLang}" : (text?.GetHashCode().ToString() ?? Guid.NewGuid().ToString());
                var item = new AudioItem
                {
                    Key = key,
                    IsTts = true,
                    Language = normalizedLang,
                    Text = text,
                    PoiId = _selectedPoi?.Id ?? 0,
                    Priority = effectivePriority
                };

                _audioQueue?.Enqueue(item); // Enqueue narration item
                // Track analytics: send trace that user played this POI
                try // Attempt to log analytics trace
                {
                    _ = TrackPoiEventAsync("tts_play", item.PoiId, $"\"mode\":\"queue_tts\",\"lang\":\"{normalizedLang}\"");
                }
                catch { }
            }
            catch { }

            return Task.CompletedTask;
        }

        // Language selection handlers
        private async void OnSelectVietnameseClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("vi");
        }

        private async void OnSelectEnglishClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("en");
        }

        private async void OnSelectRussianClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("ru");
        }

        private async void OnSelectFrenchClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("fr");
        }

        private async void OnVoiceViClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("vi");
        }

        private async void OnVoiceEnClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("en");
        }

        private async void OnVoiceZhClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("zh");
        }

        private async void OnVoiceJaClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("ja");
        }

        private async void OnVoiceKoClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("ko");
        }

        private async void OnSelectThaiClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("th");
        }

        private async void OnSelectChineseClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("zh");
        }

        private async void OnSelectSpanishClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("es");
        }

        private async void OnSelectJapaneseClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("ja");
        }

        private async void OnSelectKoreanClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("ko");
        }

        private async void OnApplyCustomLanguageClicked(object sender, EventArgs e)
        {
            try
            {
                var raw = TxtCustomLanguageCode?.Text?.Trim();
                var normalized = NormalizeLanguageCode(raw);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["language"], t["invalid_language_code"], t["ok"]);
                    return;
                }

                await ApplyLanguageSelectionAsync(normalized);
            }
            catch { }
        }

        private async Task ApplyLanguageSelectionAsync(string languageCode)
        {
            var normalized = NormalizeLanguageCode(languageCode);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            _languageRefreshCts?.Cancel();
            _languageRefreshCts?.Dispose();
            _languageRefreshCts = new CancellationTokenSource();
            var token = _languageRefreshCts.Token;

            _currentLanguage = normalized;
            try { Preferences.Default.Set("selected_language", _currentLanguage); } catch { }
            try { Preferences.Default.Set("IncludeUnpublishedPois", true); } catch { }
            try { if (TxtCustomLanguageCode != null) TxtCustomLanguageCode.Text = _currentLanguage; } catch { }
            try { await MainThread.InvokeOnMainThreadAsync(UpdateLanguageSelectionUI); } catch { }

            try
            {
                await _uiRefreshLock.WaitAsync(token);
                if (token.IsCancellationRequested) return;
                if (_isPageInitializing) return;

                await UpdateUiStringsAsync();
                if (token.IsCancellationRequested) return;

                try
                {
                    if (_realtimeSyncManager != null)
                    {
                        // Ensure all localized payloads are refreshed from API for the selected language.
                        await RunSingleFullSyncAndApplyUiAsync();
                    }
                }
                catch { }

                if (token.IsCancellationRequested) return;

                var effectiveLanguage = await EnsureLanguageHasDataOrFallbackToEnglishAsync(_currentLanguage);
                if (!string.Equals(effectiveLanguage, _currentLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    _currentLanguage = effectiveLanguage;
                    try { Preferences.Default.Set("selected_language", _currentLanguage); } catch { }
                    try { if (TxtCustomLanguageCode != null) TxtCustomLanguageCode.Text = _currentLanguage; } catch { }
                    await UpdateUiStringsAsync();
                }

                if (token.IsCancellationRequested) return;

                await DisplayAllPois(token);

                if (token.IsCancellationRequested) return;

                try
                {
                    if (_selectedPoi != null && PoiDetailPanel?.IsVisible == true)
                    {
                        await ShowPoiDetail(_selectedPoi);
                    }
                }
                catch { }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                try
                {
                    if (_uiRefreshLock.CurrentCount == 0)
                    {
                        _uiRefreshLock.Release();
                    }
                }
                catch { }
            }
        }

        private async Task<string> EnsureLanguageHasDataOrFallbackToEnglishAsync(string language)
        {
            try
            {
                var normalized = NormalizeLanguageCode(language);
                // Keep exactly the user selected language.
                // Missing content will be auto-translated at content level instead of forcing whole app to English.
                return normalized;
            }
            catch
            {
                return NormalizeLanguageCode(language);
            }
        }


        // Update visual state of language buttons in the menu
        private void UpdateLanguageSelectionUI()
        {
            try
            {
                // Ensure language buttons exist
                if (BtnLangVI == null || BtnLangEN == null || BtnLangJA == null || BtnLangKO == null || BtnLangRU == null || BtnLangFR == null || BtnLangTH == null || BtnLangZH == null || BtnLangES == null) return;

                var lang = NormalizeLanguageCode(_currentLanguage);

                // reset all
                BtnLangVI.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangVI.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangVI.FontAttributes = FontAttributes.None;
                BtnLangEN.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangEN.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangEN.FontAttributes = FontAttributes.None;
                BtnLangJA.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangJA.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangJA.FontAttributes = FontAttributes.None;
                BtnLangKO.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangKO.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangKO.FontAttributes = FontAttributes.None;
                BtnLangRU.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangRU.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangRU.FontAttributes = FontAttributes.None;
                BtnLangFR.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangFR.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangFR.FontAttributes = FontAttributes.None;
                BtnLangTH.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangTH.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangTH.FontAttributes = FontAttributes.None;
                BtnLangZH.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangZH.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangZH.FontAttributes = FontAttributes.None;
                BtnLangES.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangES.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangES.FontAttributes = FontAttributes.None;

                // set selected
                switch (lang)
                {
                    case "vi":
                        BtnLangVI.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangVI.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangVI.FontAttributes = FontAttributes.Bold;
                        break;
                    case "en":
                        BtnLangEN.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangEN.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangEN.FontAttributes = FontAttributes.Bold;
                        break;
                    case "ja":
                        BtnLangJA.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangJA.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangJA.FontAttributes = FontAttributes.Bold;
                        break;
                    case "ko":
                        BtnLangKO.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangKO.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangKO.FontAttributes = FontAttributes.Bold;
                        break;
                    case "ru":
                        BtnLangRU.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangRU.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangRU.FontAttributes = FontAttributes.Bold;
                        break;
                    case "fr":
                        BtnLangFR.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangFR.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangFR.FontAttributes = FontAttributes.Bold;
                        break;
                    case "th":
                        BtnLangTH.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangTH.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangTH.FontAttributes = FontAttributes.Bold;
                        break;
                    case "zh":
                        BtnLangZH.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangZH.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangZH.FontAttributes = FontAttributes.Bold;
                        break;
                    case "es":
                        BtnLangES.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangES.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangES.FontAttributes = FontAttributes.Bold;
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
                _isLanguageModalOpen = false;
                LanguagePanel.IsVisible = false;

                if (GpsButtonFrame != null)
                    GpsButtonFrame.IsVisible = true;

                if (HighlightsPanel != null && _selectedPoi == null)
                    HighlightsPanel.IsVisible = true;

                if (_isPageInitializing)
                {
                    return;
                }

                _languageRefreshCts?.Cancel();
                _languageRefreshCts?.Dispose();
                _languageRefreshCts = new CancellationTokenSource();
                await DisplayAllPois(_languageRefreshCts.Token);
            }
            catch { }
        }

        private async void OnEnableOfflineMapClicked(object sender, EventArgs e)
        {
            try
            {
                if (_mapOfflinePackService == null)
                {
                    UpdateOfflineMapStatusUi(await GetOfflineMapStatusTextAsync("service_missing"));
                    return;
                }

                if (BtnEnableOfflineMap != null) BtnEnableOfflineMap.IsEnabled = false;
                UpdateOfflineMapStatusUi(await GetOfflineMapStatusTextAsync("downloading_pack"));
                UpdateOfflineMapProgressUi(0, await GetOfflineMapProgressTextAsync(0));

                var progress = new Progress<MapOfflineProgress>(p =>
                {
                    var status = FormatOfflineMapDownloadingStatus(p.Stage, p.DownloadedFiles, p.TotalFiles, p.Percent);
                    UpdateOfflineMapStatusUi(status);
                    UpdateOfflineMapProgressUi(p.Percent / 100d, FormatOfflineMapProgressText(p.Percent, p.DownloadedFiles, p.TotalFiles));
                });

                var result = await _mapOfflinePackService.DownloadPackAsync("q4-v1", progress);
                if (result == null || !result.Success)
                {
                    UpdateOfflineMapStatusUi(await GetOfflineMapStatusTextAsync("download_failed", result?.Error ?? "unknown"));
                    UpdateOfflineMapProgressUi(0, await GetOfflineMapProgressTextAsync(0, failed: true));
                    return;
                }

                _offlineMapEnabled = true;
                _offlineMapLocalEntry = result.LocalEntryHtml;
                try
                {
                    Preferences.Default.Set("offline_map_enabled", true);
                    Preferences.Default.Set("offline_map_local_entry", _offlineMapLocalEntry ?? string.Empty);
                }
                catch { }

                UpdateOfflineMapStatusUi(await GetOfflineMapStatusTextAsync("ready", result.DownloadedFiles.ToString()));
                UpdateOfflineMapProgressUi(1, await GetOfflineMapProgressTextAsync(100, completed: true));

                await TrySwitchToOfflineMapAsync();
            }
            catch
            {
                UpdateOfflineMapStatusUi(await GetOfflineMapStatusTextAsync("download_failed"));
                UpdateOfflineMapProgressUi(0, await GetOfflineMapProgressTextAsync(0, failed: true));
            }
            finally
            {
                if (BtnEnableOfflineMap != null) BtnEnableOfflineMap.IsEnabled = true;
            }
        }

        private void UpdateOfflineMapStatusUi(string text)
        {
            try
            {
                if (LblOfflineMapStatus != null)
                {
                    LblOfflineMapStatus.Text = text;
                }
            }
            catch { }
        }

        private void UpdateOfflineMapProgressUi(double progress, string text)
        {
            try
            {
                var normalized = Math.Clamp(progress, 0d, 1d);
                if (PbOfflineMapProgress != null)
                {
                    PbOfflineMapProgress.Progress = normalized;
                }

                if (LblOfflineMapProgress != null)
                {
                    LblOfflineMapProgress.Text = text;
                }
            }
            catch { }
        }

        private async Task TrySwitchToOfflineMapAsync()
        {
            try
            {
                if (MapboxOfflineWebView != null)
                {
                    MapboxOfflineWebView.InputTransparent = !_offlineMapEnabled;
                }

                var hasOfflineToken = !string.IsNullOrWhiteSpace(_runtimeMapboxToken)
                    || !string.IsNullOrWhiteSpace(Preferences.Default.Get("runtime_mapbox_token", string.Empty));
                var shouldUseOffline = _offlineMapEnabled
                    && Connectivity.NetworkAccess != NetworkAccess.Internet
                    && hasOfflineToken;
                if (!shouldUseOffline)
                {
                    if (MapboxOfflineWebView != null && vinhKhanhMap != null)
                    {
                        MapboxOfflineWebView.IsVisible = false;
                        MapboxOfflineWebView.InputTransparent = true;
                        vinhKhanhMap.IsVisible = true;
                        vinhKhanhMap.InputTransparent = false;
                    }

                    if (_offlineMapEnabled && Connectivity.NetworkAccess != NetworkAccess.Internet && !hasOfflineToken)
                    {
                        AddLog("Offline map chưa có runtime token, giữ Google Map để tránh màn hình trắng.");
                    }

                    return;
                }

                if (MapboxOfflineWebView == null || vinhKhanhMap == null)
                {
                    return;
                }

                await EnsureMapboxOfflineSourceAsync();

                if (MapboxOfflineWebView.IsVisible)
                {
                    await PushPoisToOfflineMapAsync();
                    return;
                }

                MapboxOfflineWebView.IsVisible = true;
                MapboxOfflineWebView.InputTransparent = false;
                vinhKhanhMap.IsVisible = false;
                vinhKhanhMap.InputTransparent = true;

                await Task.Delay(350);
                await PushPoisToOfflineMapAsync();
            }
            catch { }
        }

        private async Task EnsureMapboxOfflineSourceAsync()
        {
            try
            {
                if (MapboxOfflineWebView == null) return;

                if (string.IsNullOrWhiteSpace(_runtimeMapboxToken))
                {
                    _runtimeMapboxToken = Preferences.Default.Get("runtime_mapbox_token", string.Empty);
                }

                if (string.IsNullOrWhiteSpace(_runtimeMapboxToken))
                {
                    var cfg = await _apiService.GetMapRuntimeConfigAsync();
                    _runtimeMapboxToken = cfg?.MapboxAccessToken?.Trim();
                    if (!string.IsNullOrWhiteSpace(_runtimeMapboxToken))
                    {
                        Preferences.Default.Set("runtime_mapbox_token", _runtimeMapboxToken);
                    }
                }

                var tokenQuery = string.IsNullOrWhiteSpace(_runtimeMapboxToken)
                    ? string.Empty
                    : $"?token={Uri.EscapeDataString(_runtimeMapboxToken)}";

                var target = "mapbox-offline.html" + tokenQuery;
                if (MapboxOfflineWebView.Source is UrlWebViewSource current
                    && string.Equals(current.Url, target, StringComparison.Ordinal))
                {
                    return;
                }

                MapboxOfflineWebView.Source = new UrlWebViewSource { Url = target };
            }
            catch { }
        }

        private async Task PushPoisToOfflineMapAsync()
        {
            try
            {
                if (MapboxOfflineWebView == null || !MapboxOfflineWebView.IsVisible) return;

                var pois = (_pois ?? new List<PoiModel>()).Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    category = p.Category,
                    lat = p.Latitude,
                    lng = p.Longitude,
                    radius = p.Radius,
                    priority = p.Priority
                }).ToList();

                var payload = System.Text.Json.JsonSerializer.Serialize(pois);
                var js = $"window.setPoisFromApp({payload});";
                await MapboxOfflineWebView.EvaluateJavaScriptAsync(js);
            }
            catch { }
        }

        // Update static UI text strings according to current language
        private async Task UpdateUiStringsAsync()
        {
            try
            {
                var dynamicUi = await BuildDynamicUiTextAsync(_currentLanguage);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    try
                    {
                        UpdateLanguageSelectionUI();

                        if (TabOverview != null && TabIntro != null)
                        {
                            var tabReview = this.FindByName<Button>("TabReview");
                            TabOverview.Text = dynamicUi["tab_overview"];
                            TabIntro.Text = dynamicUi["tab_intro"];
                            if (tabReview != null) tabReview.Text = dynamicUi["tab_review"];
                        }

                        var btnToggle = this.FindByName<Button>("BtnToggleDescription");
                        if (btnToggle != null)
                        {
                            btnToggle.Text = dynamicUi["read_more"];
                        }

                        // Action labels
                        var lbDir = this.FindByName<Label>("LblActDirections"); if (lbDir != null) lbDir.Text = dynamicUi["act_directions"];
                        var lbAudio = this.FindByName<Label>("LblActAudio"); if (lbAudio != null) lbAudio.Text = dynamicUi["act_listen_now"];
                        var lbNarr = this.FindByName<Label>("LblActNarration"); if (lbNarr != null) lbNarr.Text = dynamicUi["act_audio"];
                        var lbShare = this.FindByName<Label>("LblActShare"); if (lbShare != null) lbShare.Text = dynamicUi["act_share"];
                        var lbSave = this.FindByName<Label>("LblActSave"); if (lbSave != null) lbSave.Text = dynamicUi["act_save"];
                        var lbQr = this.FindByName<Label>("LblActQr"); if (lbQr != null) lbQr.Text = dynamicUi["act_qr"];

                        var lblAddressTitle = this.FindByName<Label>("LblAddressTitle"); if (lblAddressTitle != null) lblAddressTitle.Text = dynamicUi["field_address"];
                        var lblDistanceTitle = this.FindByName<Label>("LblDistanceTitle"); if (lblDistanceTitle != null) lblDistanceTitle.Text = dynamicUi["field_distance"];
                        var lblOpeningTitle = this.FindByName<Label>("LblOpeningHoursTitle"); if (lblOpeningTitle != null) lblOpeningTitle.Text = dynamicUi["field_opening_hours"];
                        var lblWebsiteTitle = this.FindByName<Label>("LblWebsiteTitle"); if (lblWebsiteTitle != null) lblWebsiteTitle.Text = dynamicUi["field_website"];
                        var lblPhoneTitle = this.FindByName<Label>("LblPhoneTitle"); if (lblPhoneTitle != null) lblPhoneTitle.Text = dynamicUi["field_phone"];

                        var lblHighlightsTitle = this.FindByName<Label>("LblHighlightsTitle"); if (lblHighlightsTitle != null) lblHighlightsTitle.Text = dynamicUi["highlights_title"];
                        var btnSavedShortcut = this.FindByName<Button>("BtnSavedShortcut"); if (btnSavedShortcut != null) btnSavedShortcut.Text = dynamicUi["saved"];
                        var btnViewAllHighlights = this.FindByName<Button>("BtnViewAllHighlights"); if (btnViewAllHighlights != null) btnViewAllHighlights.Text = dynamicUi["view_more"];
                        var btnForceSync = this.FindByName<Button>("BtnForceSyncNow"); if (btnForceSync != null) btnForceSync.Text = dynamicUi["force_sync_now"];
                        var btnCloseLangX = this.FindByName<Button>("BtnCloseLangPanelX"); if (btnCloseLangX != null) btnCloseLangX.Text = dynamicUi["close_x"];
                        var btnApplyCustomLang = this.FindByName<Button>("BtnApplyCustomLanguage"); if (btnApplyCustomLang != null) btnApplyCustomLang.Text = dynamicUi["apply"];
                        var btnConfirmMenu = this.FindByName<Button>("BtnConfirmMenu"); if (btnConfirmMenu != null) btnConfirmMenu.Text = dynamicUi["ok"];
                        var btnEnableOfflineMap = this.FindByName<Button>("BtnEnableOfflineMap"); if (btnEnableOfflineMap != null) btnEnableOfflineMap.Text = dynamicUi["offline_map_download"];
                        var btnShowSaved = this.FindByName<Button>("BtnShowSaved"); if (btnShowSaved != null) btnShowSaved.Text = dynamicUi["show_saved"];
                        var lblOfflineMapTitle = this.FindByName<Label>("LblOfflineMapTitle"); if (lblOfflineMapTitle != null) lblOfflineMapTitle.Text = dynamicUi["offline_map_title"];
                        var lblCustomLanguageTitle = this.FindByName<Label>("LblCustomLanguageTitle"); if (lblCustomLanguageTitle != null) lblCustomLanguageTitle.Text = dynamicUi["custom_language_title"];
                        var txtCustomLanguageCode = this.FindByName<Entry>("TxtCustomLanguageCode"); if (txtCustomLanguageCode != null) txtCustomLanguageCode.Placeholder = dynamicUi["custom_language_placeholder"];
                        var btnStartTracking = this.FindByName<Button>("BtnStartTracking"); if (btnStartTracking != null) btnStartTracking.Text = dynamicUi["tracking_start"];
                        var btnStopTracking = this.FindByName<Button>("BtnStopTracking"); if (btnStopTracking != null) btnStopTracking.Text = dynamicUi["tracking_stop"];
                        var lblMapLoading = this.FindByName<Label>("LblMapLoading"); if (lblMapLoading != null) lblMapLoading.Text = dynamicUi["map_loading"];

                        // Search placeholder and language menu labels
                        if (SearchPoiBar != null) SearchPoiBar.Placeholder = dynamicUi["search_placeholder"];
                        if (SearchPoiBar != null) SearchPoiBar.CancelButtonColor = Microsoft.Maui.Graphics.Color.FromArgb("#9AA0A6");
                        var langTitle = this.FindByName<Label>("LblLangTitle"); if (langTitle != null) langTitle.Text = dynamicUi["select_language"];
                        var btnClose = this.FindByName<Button>("BtnCloseMenu"); if (btnClose != null) btnClose.Text = dynamicUi["close"];

                        var reviewHint = this.FindByName<Label>("LblReviewHint"); if (reviewHint != null) reviewHint.Text = dynamicUi["review_hint"];

                        if (BtnLangVI != null) BtnLangVI.Text = "🇻🇳 " + dynamicUi["lang_vi"];
                        if (BtnLangEN != null) BtnLangEN.Text = "🇺🇸 " + dynamicUi["lang_en"];
                        if (BtnLangJA != null) BtnLangJA.Text = "🇯🇵 " + dynamicUi["lang_ja"];
                        if (BtnLangKO != null) BtnLangKO.Text = "🇰🇷 " + dynamicUi["lang_ko"];
                        if (BtnLangRU != null) BtnLangRU.Text = "🇷🇺 " + dynamicUi["lang_ru"];
                        if (BtnLangFR != null) BtnLangFR.Text = "🇫🇷 " + dynamicUi["lang_fr"];
                        if (BtnLangTH != null) BtnLangTH.Text = "🇹🇭 " + dynamicUi["lang_th"];
                        if (BtnLangZH != null) BtnLangZH.Text = "🇨🇳 " + dynamicUi["lang_zh"];
                        if (BtnLangES != null) BtnLangES.Text = "🇪🇸 " + dynamicUi["lang_es"];

                        if (LblOfflineMapStatus != null && (LblOfflineMapStatus.Text?.Contains(":") == true))
                        {
                            var statusBody = LblOfflineMapStatus.Text[(LblOfflineMapStatus.Text.IndexOf(':') + 1)..].Trim();
                            LblOfflineMapStatus.Text = $"{dynamicUi["offline_status_prefix"]}: {statusBody}";
                        }

                        if (LblOfflineMapProgress != null && (LblOfflineMapProgress.Text?.Contains(":") == true))
                        {
                            var progressBody = LblOfflineMapProgress.Text[(LblOfflineMapProgress.Text.IndexOf(':') + 1)..].Trim();
                            LblOfflineMapProgress.Text = $"{dynamicUi["offline_progress_prefix"]}: {progressBody}";
                        }

                        _ = UpdateOfflineMapUiLocalizedAsync();
                    }
                    catch { }
                });
            }
            catch { }
        }

        private async Task<Dictionary<string, string>> BuildDynamicUiTextAsync(string language)
        {
            var lang = NormalizeLanguageCode(language);
            var viTexts = new Dictionary<string, string>
            {
                ["tab_overview"] = "Tổng quan",
                ["tab_intro"] = "Giới thiệu",
                ["tab_review"] = "Đánh giá",
                ["read_more"] = "Xem thêm",
                ["act_directions"] = "Dẫn đường",
                ["act_listen_now"] = "Nghe ngay",
                ["act_audio"] = "Audio",
                ["act_share"] = "Chia sẻ",
                ["act_save"] = "Lưu",
                ["act_qr"] = "Mã QR",
                ["field_address"] = "Địa chỉ",
                ["field_distance"] = "Khoảng cách",
                ["field_opening_hours"] = "Giờ mở cửa",
                ["field_website"] = "Website",
                ["field_phone"] = "Điện thoại",
                ["language"] = "Language",
                ["select_language"] = "Select language",
                ["change_language"] = "Change language",
                ["field_address_en"] = "Address",
                ["field_opening_hours_en"] = "Opening hours",
                ["field_price_en"] = "Price",
                ["listen_narration"] = "Listen narration",
                ["stop_narration"] = "Stop",
                ["priority_chip"] = "Ưu tiên {value}",
                ["search_placeholder"] = "Tìm kiếm...",
                ["lang_settings"] = "⚙️ Cài đặt ngôn ngữ",
                ["highlights_title"] = "Nổi bật trong khu vực",
                ["saved"] = "Đã lưu",
                ["view_more"] = "Xem thêm",
                ["force_sync_now"] = "Force Sync now",
                ["close"] = "Đóng",
                ["close_x"] = "✕",
                ["apply"] = "Áp dụng",
                ["ok"] = "OK",
                ["cancel"] = "Hủy",
                ["offline_map_download"] = "Tải map offline",
                ["offline_map_title"] = "🗺️ Bản đồ offline",
                ["custom_language_title"] = "Ngôn ngữ khác (custom)",
                ["custom_language_placeholder"] = "Ví dụ: de, it, ar, hi...",
                ["map_loading"] = "Đang tải bản đồ...",
                ["show_saved"] = "Hiện những địa điểm đã lưu",
                ["review_hint"] = "Nội dung đánh giá đang được cập nhật",
                ["no_description"] = "Chưa có mô tả cho địa điểm này.",
                ["open_now"] = "Đang mở cửa",
                ["closed"] = "Đóng cửa",
                ["no_rating"] = "Chưa có đánh giá",
                ["rating_label"] = "Đánh giá",
                ["read_less"] = "Rút gọn",
                ["new_badge"] = "Mới",
                ["popular_hint_hot"] = "Địa điểm có nhiều khách ghé qua",
                ["tracking_start"] = "Bắt đầu",
                ["tracking_stop"] = "Dừng",
                ["tracking_status_tracking"] = "Trạng thái: đang theo dõi",
                ["tracking_status_stopped"] = "Trạng thái: đã dừng",
                ["offline_status_prefix"] = "Trạng thái",
                ["offline_progress_prefix"] = "Tiến độ",
                ["offline_files_suffix"] = "tệp",
                ["offline_status_service_missing"] = "Thiếu dịch vụ bản đồ offline",
                ["offline_status_downloading_pack"] = "Đang tải gói bản đồ offline...",
                ["offline_status_download_failed"] = "Tải bản đồ offline thất bại",
                ["offline_status_ready"] = "Bản đồ offline đã sẵn sàng",
                ["offline_status_online"] = "Có mạng, bản đồ online đang hoạt động",
                ["offline_status_online_with_offline_ready"] = "Có mạng, bản đồ online đang hoạt động (offline đã sẵn sàng)",
                ["offline_status_offline_using"] = "Mất mạng, đang dùng bản đồ offline",
                ["offline_status_offline_no_pack"] = "Mất mạng, chưa có bản đồ offline (vẫn dùng Google Map)",
                ["offline_status_downloading_template"] = "{stage} {downloaded}/{total} ({percent}%)",
                ["progress_done"] = "hoàn tất",
                ["progress_failed"] = "thất bại",
                ["lang_vi"] = "Tiếng Việt",
                ["lang_en"] = "English",
                ["lang_ja"] = "日本語",
                ["lang_ko"] = "한국어",
                ["lang_ru"] = "Русский",
                ["lang_fr"] = "Français",
                ["lang_th"] = "ไทย",
                ["lang_zh"] = "中文",
                ["lang_es"] = "Español"
            };

            if (lang == "vi") return viTexts;

            var enTexts = new Dictionary<string, string>
            {
                ["tab_overview"] = "Overview",
                ["tab_intro"] = "Introduction",
                ["tab_review"] = "Reviews",
                ["read_more"] = "Read more",
                ["act_directions"] = "Directions",
                ["act_listen_now"] = "Listen now",
                ["act_audio"] = "Audio",
                ["act_share"] = "Share",
                ["act_save"] = "Save",
                ["act_qr"] = "QR code",
                ["field_address"] = "Address",
                ["field_distance"] = "Distance",
                ["field_opening_hours"] = "Opening hours",
                ["field_website"] = "Website",
                ["field_phone"] = "Phone",
                ["language"] = "Language",
                ["select_language"] = "Select language",
                ["change_language"] = "Change language",
                ["field_address_en"] = "Address",
                ["field_opening_hours_en"] = "Opening hours",
                ["field_price_en"] = "Price",
                ["listen_narration"] = "Listen narration",
                ["stop_narration"] = "Stop",
                ["priority_chip"] = "Priority {value}",
                ["search_placeholder"] = "Search...",
                ["lang_settings"] = "⚙️ Language settings",
                ["highlights_title"] = "Highlights in this area",
                ["saved"] = "Saved",
                ["view_more"] = "View more",
                ["force_sync_now"] = "Force Sync now",
                ["close"] = "Close",
                ["close_x"] = "✕",
                ["apply"] = "Apply",
                ["ok"] = "OK",
                ["cancel"] = "Cancel",
                ["offline_map_download"] = "Download offline map",
                ["offline_map_title"] = "🗺️ Offline map",
                ["custom_language_title"] = "Other language (custom)",
                ["custom_language_placeholder"] = "Example: de, it, ar, hi...",
                ["map_loading"] = "Loading map...",
                ["show_saved"] = "Show saved places",
                ["review_hint"] = "Review content is being updated",
                ["no_description"] = "No description available.",
                ["open_now"] = "Open now",
                ["closed"] = "Closed",
                ["no_rating"] = "No rating",
                ["rating_label"] = "Rating",
                ["read_less"] = "Show less",
                ["new_badge"] = "New",
                ["popular_hint_hot"] = "Popular place with high live traffic",
                ["tracking_start"] = "Start",
                ["tracking_stop"] = "Stop",
                ["tracking_status_tracking"] = "Status: tracking",
                ["tracking_status_stopped"] = "Status: stopped",
                ["offline_status_prefix"] = "Status",
                ["offline_progress_prefix"] = "Progress",
                ["offline_files_suffix"] = "files",
                ["offline_status_service_missing"] = "Offline map service missing",
                ["offline_status_downloading_pack"] = "Downloading offline map pack...",
                ["offline_status_download_failed"] = "Offline map download failed",
                ["offline_status_ready"] = "Offline map is ready",
                ["offline_status_online"] = "Internet available, online map is active",
                ["offline_status_online_with_offline_ready"] = "Internet available, online map is active (offline ready)",
                ["offline_status_offline_using"] = "No internet, using offline map",
                ["offline_status_offline_no_pack"] = "No internet, offline map is not downloaded yet (still using Google Map)",
                ["offline_status_downloading_template"] = "{stage} {downloaded}/{total} ({percent}%)",
                ["progress_done"] = "done",
                ["progress_failed"] = "failed",
                ["lang_vi"] = "Vietnamese",
                ["lang_en"] = "English",
                ["lang_ja"] = "Japanese",
                ["lang_ko"] = "Korean",
                ["lang_ru"] = "Russian",
                ["lang_fr"] = "French",
                ["lang_th"] = "Thai",
                ["lang_zh"] = "Chinese",
                ["lang_es"] = "Spanish"
            };

            if (lang == "en") return enTexts;

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in enTexts)
            {
                var cacheKey = $"ui:{lang}:{kv.Key}";
                if (_dynamicUiTextCache.TryGetValue(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
                {
                    result[kv.Key] = cached;
                    continue;
                }

                var translated = await TranslateTextAsync(kv.Value, lang);
                var value = string.IsNullOrWhiteSpace(translated) ? kv.Value : translated;
                _dynamicUiTextCache[cacheKey] = value;
                result[kv.Key] = value;
            }

            return result;
        }

        private async Task<string> GetTrackingStatusTextAsync(string state)
        {
            var ui = await BuildDynamicUiTextAsync(_currentLanguage);
            return string.Equals(state, "tracking", StringComparison.OrdinalIgnoreCase)
                ? ui["tracking_status_tracking"]
                : ui["tracking_status_stopped"];
        }

        private async Task<string> GetOfflineMapStatusTextAsync(string key, string? arg = null)
        {
            var ui = await BuildDynamicUiTextAsync(_currentLanguage);
            var prefix = ui["offline_status_prefix"];
            var body = key switch
            {
                "service_missing" => ui["offline_status_service_missing"],
                "downloading_pack" => ui["offline_status_downloading_pack"],
                "download_failed" => string.IsNullOrWhiteSpace(arg)
                    ? ui["offline_status_download_failed"]
                    : $"{ui["offline_status_download_failed"]} ({arg})",
                "ready" => string.IsNullOrWhiteSpace(arg)
                    ? ui["offline_status_ready"]
                    : $"{ui["offline_status_ready"]} ({arg} {ui["offline_files_suffix"]})",
                "online" => ui["offline_status_online"],
                "online_with_offline_ready" => ui["offline_status_online_with_offline_ready"],
                "offline_using" => ui["offline_status_offline_using"],
                "offline_no_pack" => ui["offline_status_offline_no_pack"],
                _ => ui["offline_status_online"]
            };

            return $"{prefix}: {body}";
        }

        private string FormatOfflineMapDownloadingStatus(string stage, int downloadedFiles, int totalFiles, double percent)
        {
            var lang = NormalizeLanguageCode(_currentLanguage);
            var prefix = _dynamicUiTextCache.TryGetValue($"ui:{lang}:offline_status_prefix", out var p)
                ? p
                : "Status";
            var template = _dynamicUiTextCache.TryGetValue($"ui:{lang}:offline_status_downloading_template", out var t)
                ? t
                : "{stage} {downloaded}/{total} ({percent}%)";

            var body = template
                .Replace("{stage}", stage ?? string.Empty)
                .Replace("{downloaded}", downloadedFiles.ToString())
                .Replace("{total}", totalFiles.ToString())
                .Replace("{percent}", percent.ToString("0.0"));

            return $"{prefix}: {body}";
        }

        private async Task<string> GetOfflineMapProgressTextAsync(double percent, bool completed = false, bool failed = false)
        {
            var ui = await BuildDynamicUiTextAsync(_currentLanguage);
            var prefix = ui["offline_progress_prefix"];
            var value = percent.ToString("0.0");

            if (completed)
            {
                return $"{prefix}: {value}% ({ui["progress_done"]})";
            }

            if (failed)
            {
                return $"{prefix}: {value}% ({ui["progress_failed"]})";
            }

            return $"{prefix}: {value}%";
        }

        private string FormatOfflineMapProgressText(double percent, int downloadedFiles, int totalFiles)
        {
            var lang = NormalizeLanguageCode(_currentLanguage);
            var prefix = _dynamicUiTextCache.TryGetValue($"ui:{lang}:offline_progress_prefix", out var p)
                ? p
                : "Progress";
            var filesSuffix = _dynamicUiTextCache.TryGetValue($"ui:{lang}:offline_files_suffix", out var f)
                ? f
                : "files";
            return $"{prefix}: {percent:0.0}% ({downloadedFiles}/{totalFiles} {filesSuffix})";
        }

        private async Task UpdateOfflineMapUiLocalizedAsync()
        {
            try
            {
                var progressValue = Math.Clamp(PbOfflineMapProgress?.Progress ?? 0d, 0d, 1d) * 100d;
                var isFailed = LblOfflineMapProgress?.Text?.Contains("failed", StringComparison.OrdinalIgnoreCase) == true
                               || LblOfflineMapProgress?.Text?.Contains("thất bại", StringComparison.OrdinalIgnoreCase) == true;
                var progressText = await GetOfflineMapProgressTextAsync(progressValue,
                    completed: progressValue >= 99.9,
                    failed: isFailed);
                UpdateOfflineMapProgressUi(progressValue / 100d, progressText);

                if (LblTrackingStatus != null)
                {
                    var isTracking = LblTrackingStatus.Text?.Contains("tracking", StringComparison.OrdinalIgnoreCase) == true
                                     || LblTrackingStatus.Text?.Contains("đang theo dõi", StringComparison.OrdinalIgnoreCase) == true;
                    LblTrackingStatus.Text = await GetTrackingStatusTextAsync(isTracking ? "tracking" : "stopped");
                }
            }
            catch { }
        }

        protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
        {
            base.OnNavigatedFrom(args);
            _isTrackingActive = false;
            Interlocked.Increment(ref _detailRequestVersion);
            try
            {
                _appearingCts?.Cancel();
                _appearingCts?.Dispose();
                _appearingCts = null;

                _searchDebounceCts?.Cancel();
                _searchDebounceCts?.Dispose();
                _searchDebounceCts = null;

                _detailCts?.Cancel();
                _detailCts?.Dispose();
                _detailCts = null;

                _realtimeMapRefreshCts?.Cancel();
                _realtimeMapRefreshCts?.Dispose();
                _realtimeMapRefreshCts = null;

                _realtimeHighlightsRefreshCts?.Cancel();
                _realtimeHighlightsRefreshCts?.Dispose();
                _realtimeHighlightsRefreshCts = null;

                _realtimeDetailRefreshCts?.Cancel();
                _realtimeDetailRefreshCts?.Dispose();
                _realtimeDetailRefreshCts = null;

                _languageRefreshCts?.Cancel();
                _languageRefreshCts?.Dispose();
                _languageRefreshCts = null;

                try
                {
                    Connectivity.ConnectivityChanged -= OnConnectivityChanged;
                }
                catch { }
            }
            catch { }
        }

        private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (e.NetworkAccess != NetworkAccess.Internet && _offlineMapEnabled)
                    {
                        await TrySwitchToOfflineMapAsync();
                        UpdateOfflineMapStatusUi(await GetOfflineMapStatusTextAsync("offline_using"));
                        return;
                    }

                    if (MapboxOfflineWebView != null && vinhKhanhMap != null)
                    {
                        MapboxOfflineWebView.IsVisible = false;
                        MapboxOfflineWebView.InputTransparent = true;
                        vinhKhanhMap.IsVisible = true;
                        UpdateOfflineMapStatusUi(_offlineMapEnabled
                            ? await GetOfflineMapStatusTextAsync("online_with_offline_ready")
                            : await GetOfflineMapStatusTextAsync("online"));
                    }

                    if (e.NetworkAccess != NetworkAccess.Internet && !_offlineMapEnabled)
                    {
                        UpdateOfflineMapStatusUi(await GetOfflineMapStatusTextAsync("offline_no_pack"));
                    }
                });
            }
            catch { }
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (_isPageInitializing) return;

            _appearingCts?.Cancel();
            _appearingCts?.Dispose();
            _appearingCts = new CancellationTokenSource();

            await InitializeOnAppearingAsync(_appearingCts.Token);
        }

        private async Task InitializeOnAppearingAsync(CancellationToken cancellationToken)
        {
            _isPageInitializing = true;
            Interlocked.Increment(ref _mapRefreshVersion);
            SetMapLoadingState(false);

            try
            {
                EnsureRealtimeSyncSubscriptions();
                if (_realtimeSyncManager != null && !_realtimeSyncManager.IsConnected)
                {
                    AddLog("Realtime chưa kết nối, đang kết nối lại...");
                    _ = Task.Run(async () =>
                    {
                        try { await _realtimeSyncManager.StartAsync(); } catch { }
                    });
                }

                // Load local data first to avoid startup ANR/freeze.
                _pois = await _dbService.GetPoisAsync();
                if (cancellationToken.IsCancellationRequested) return;
                if (Shell.Current?.CurrentPage is not MapPage) return;

                // Keep old seed behavior when completely empty.
                if (_pois == null || !_pois.Any())
                {
                    try
                    {
                        await EnsureApiBaseReadyAsync();
                        if (_realtimeSyncManager != null)
                        {
                            await RunSingleFullSyncAndApplyUiAsync("Synced {0} POIs from Admin/API");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"Sync from Admin/API failed: {ex.Message}");
                    }

                    AddLog("No POIs from server, seeding sample data...");
                    await SeedFullData();
                    _pois = await _dbService.GetPoisAsync();
                }
                else
                {
                    AddLog($"Loaded {_pois.Count} POIs from server");
                }

                if (cancellationToken.IsCancellationRequested) return;
                if (Shell.Current?.CurrentPage is not MapPage) return;

                // Fast pin render first
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    try
                    {
                        if (MapboxOfflineWebView != null)
                        {
                            MapboxOfflineWebView.Source = null;
                            MapboxOfflineWebView.IsVisible = false;
                            MapboxOfflineWebView.InputTransparent = true;
                        }

                        if (vinhKhanhMap != null)
                        {
                            vinhKhanhMap.IsVisible = true;
                            vinhKhanhMap.Opacity = 1;
                            vinhKhanhMap.InputTransparent = false;
                        }

                        AddPoisToMap();
                        BtnShowSaved.IsVisible = _pois.Any(p => p.IsSaved);

                        // reset map visibility to avoid stale blank state from previous fallback
                        if (MapboxOfflineWebView != null && vinhKhanhMap != null)
                        {
                            var hasOfflineToken = !string.IsNullOrWhiteSpace(_runtimeMapboxToken)
                                || !string.IsNullOrWhiteSpace(Preferences.Default.Get("runtime_mapbox_token", string.Empty));
                            var canUseOffline = _offlineMapEnabled
                                && Connectivity.NetworkAccess != NetworkAccess.Internet
                                && hasOfflineToken;

                            MapboxOfflineWebView.IsVisible = canUseOffline;
                            MapboxOfflineWebView.InputTransparent = !canUseOffline;
                            vinhKhanhMap.IsVisible = !canUseOffline;
                            vinhKhanhMap.InputTransparent = canUseOffline;
                        }
                    }
                    catch { }
                });

                // Defer heavier localized pin rendering to background to avoid blocking first frame
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        if (Shell.Current?.CurrentPage is not MapPage) return;
                        // Use background thread to build data then update UI on MainThread
                        await DisplayAllPois(cancellationToken);
                    }
                    catch { }
                });

                try
                {
                    var highlights = _pois.OrderByDescending(p => p.Priority).Take(6).ToList();
                    await RenderHighlightsAsync(highlights);
                }
                catch { }

                _geofenceEngine?.UpdatePois(_pois);

                // Do not block UI thread while checking map rendering/location
                _ = Task.Run(async () =>
                {
                    try { await CenterMapOnUserFirstAsync(); } catch { }
                    if (cancellationToken.IsCancellationRequested) return;
                    if (Shell.Current?.CurrentPage is not MapPage) return;
                    try { await CheckMapDisplayAsync(); } catch { }
                });

                // Keep one background sync task only; skip if an existing one is still running.
                if (_realtimeSyncManager != null)
                {
                    if (_backgroundFullSyncTask == null || _backgroundFullSyncTask.IsCompleted)
                    {
                        _backgroundFullSyncTask = Task.Run(async () =>
                        {
                            try
                            {
                                AddLog("Syncing POIs from server...");
                                await RunSingleFullSyncAndApplyUiAsync();
                            }
                            catch { }
                        });
                    }
                }

                try
                {
                    Connectivity.ConnectivityChanged -= OnConnectivityChanged;
                    Connectivity.ConnectivityChanged += OnConnectivityChanged;
                }
                catch { }

                await TrySwitchToOfflineMapAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi load dữ liệu: {ex.Message}");
                SetMapLoadingState(false);
            }
            finally
            {
                _isPageInitializing = false;
            }
        }

        private async Task RunSingleFullSyncAndApplyUiAsync(string? successLogFormat = null)
        {
            if (_realtimeSyncManager == null) return;

            await _fullSyncGate.WaitAsync();
            try
            {
                _suppressNextRealtimeFullSyncEvent = true;
                await _realtimeSyncManager.SyncAllPoisAsync();
                var syncedPois = await _dbService.GetPoisAsync();
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    _pois = syncedPois ?? new List<PoiModel>();
                    AddPoisToMap();
                    try { BtnShowSaved.IsVisible = _pois.Any(p => p.IsSaved); } catch { }
                    var highlights = _pois.OrderByDescending(p => p.Priority).Take(6).ToList();
                    await RenderHighlightsAsync(highlights);
                    if (!string.IsNullOrWhiteSpace(successLogFormat))
                    {
                        AddLog(string.Format(successLogFormat, _pois.Count));
                    }
                });
            }
            finally
            {
                _fullSyncGate.Release();
            }
        }

        private void AddPoisToMap()
        {
            if (!MainThread.IsMainThread)
            {
                MainThread.BeginInvokeOnMainThread(AddPoisToMap);
                return;
            }
        try
        {
            // Maintain a mapping of rendered pins to avoid clearing and recreating all pins every time.
            if (_pinByPoiId == null) _pinByPoiId = new Dictionary<int, Microsoft.Maui.Controls.Maps.Pin>();

            // Determine candidate pois to render: prioritize those within visible region + by priority
            var candidates = (_pois ?? new List<PoiModel>()).Where(p => p != null).ToList();

            Location center = null;
            try { center = vinhKhanhMap?.VisibleRegion?.Center; } catch { }

            if (center != null)
            {
                // compute distance for each poi, prefer closer ones
                candidates = candidates
                    .OrderBy(p => HaversineDistanceMeters(center.Latitude, center.Longitude, p.Latitude, p.Longitude))
                    .ThenByDescending(p => p.Priority)
                    .ToList();
            }
            else
            {
                candidates = candidates.OrderByDescending(p => p.Priority).ToList();
            }

            var desired = candidates.Take(MaxPinsToRender).ToList();
            var desiredIds = new HashSet<int>(desired.Select(d => d.Id));

            // Remove pins that are no longer desired
            var toRemove = _pinByPoiId.Keys.Where(id => !desiredIds.Contains(id)).ToList();
            foreach (var id in toRemove)
            {
                try
                {
                    if (_pinByPoiId.TryGetValue(id, out var pin))
                    {
                        try { vinhKhanhMap.Pins.Remove(pin); } catch { }
                        try { _pinByPoiId.Remove(id); } catch { }
                    }
                }
                catch { }
            }

            // Add pins that are missing
            foreach (var poi in desired)
            {
                try
                {
                    if (_pinByPoiId.ContainsKey(poi.Id)) continue;

                    var pin = new Microsoft.Maui.Controls.Maps.Pin
                    {
                        Label = poi.Name,
                        Address = poi.Category,
                        Location = new Microsoft.Maui.Devices.Sensors.Location(poi.Latitude, poi.Longitude),
                        Type = Microsoft.Maui.Controls.Maps.PinType.Place
                    };

                    // attach handler
                    pin.MarkerClicked += async (s, e) =>
                    {
                        try
                        {
                            try { e.HideInfoWindow = true; } catch { }
                            await OpenPoiDetailFromSelectionAsync(poi, "map_pin", userInitiated: true);
                        }
                        catch { }
                    };

                    vinhKhanhMap.Pins.Add(pin);
                    _pinByPoiId[poi.Id] = pin;
                }
                catch { }
            }
        }
        catch { }
        }

        private async void OnPinClicked(object sender, Microsoft.Maui.Controls.Maps.PinClickedEventArgs e)
        {
            try
            {
                var pin = sender as Microsoft.Maui.Controls.Maps.Pin;
                if (pin == null || pin.Location == null || _pois == null || !_pois.Any()) return;

                var poi = _pois
                    .OrderBy(p => Math.Abs(p.Latitude - pin.Location.Latitude) + Math.Abs(p.Longitude - pin.Location.Longitude))
                    .FirstOrDefault(p => Math.Abs(p.Latitude - pin.Location.Latitude) < 0.0003 && Math.Abs(p.Longitude - pin.Location.Longitude) < 0.0003);

                if (poi != null)
                {
                    e.HideInfoWindow = true;
                    await OpenPoiDetailFromSelectionAsync(poi, "map_pin", userInitiated: true);
                }
            }
            catch (Exception ex)
            {
                AddLog($"OnPinClicked error: {ex.Message}");
            }
        }

        private async Task TryRestoreSelectedPoiFromUiAsync()
        {
            try
            {
                if (_selectedPoi != null) return;
                if (PoiDetailPanel == null || !PoiDetailPanel.IsVisible) return;

                var title = LblPoiName?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(title) || _pois == null || !_pois.Any()) return;

                var normalizedTitle = title.Trim();
                var matched = _pois.FirstOrDefault(p => string.Equals((p.Name ?? string.Empty).Trim(), normalizedTitle, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    _selectedPoi = matched;
                    return;
                }

                foreach (var poi in _pois)
                {
                    try
                    {
                        var content = await _dbService.GetContentByPoiIdAsync(poi.Id, NormalizeLanguageCode(_currentLanguage))
                                     ?? await _dbService.GetContentByPoiIdAsync(poi.Id, "en")
                                     ?? await _dbService.GetContentByPoiIdAsync(poi.Id, "vi");
                        if (content == null) continue;

                        var contentTitle = content.Title?.Trim();
                        if (!string.IsNullOrWhiteSpace(contentTitle)
                            && string.Equals(contentTitle, normalizedTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            _selectedPoi = poi;
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private async Task TrackPoiEventAsync(string eventName, int poiId, string? extraFields = null)
        {
            try
            {
                if (_apiService == null || poiId <= 0 || string.IsNullOrWhiteSpace(eventName)) return;

                var extra = string.IsNullOrWhiteSpace(extraFields)
                    ? $"{{\"event\":\"{eventName}\",\"source\":\"mobile_app\"}}"
                    : $"{{\"event\":\"{eventName}\",\"source\":\"mobile_app\",{extraFields}}}";

                var trace = new VinhKhanh.Shared.TraceLog
                {
                    PoiId = poiId,
                    DeviceId = BuildDeviceAnalyticsId(),
                    Latitude = _lastLocation?.Latitude ?? 0,
                    Longitude = _lastLocation?.Longitude ?? 0,
                    ExtraJson = extra,
                    DurationSeconds = null
                };

                await _apiService.PostTraceAsync(trace);
            }
            catch { }
        }

        // Existing async ShowPoiDetail is defined earlier; remove this duplicate sync overload.
    }
}