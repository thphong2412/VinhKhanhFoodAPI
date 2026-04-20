using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Collections.Concurrent;
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
using System.Diagnostics;

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
        private readonly SemaphoreSlim _loadAllFetchLock = new(1, 1);
        private readonly Dictionary<string, string> _dynamicUiTextCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTime> _eventTraceGuard = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<int, DateTime> _poiDetailHydrateUtc = new();
        private readonly ConcurrentDictionary<string, LoadAllCacheEntry> _loadAllCacheByLang = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<int, PoiLiveStatsDto> _liveStatsByPoiId = new();
        private DateTime _lastLiveStatsFetchUtc = DateTime.MinValue;
        private static readonly HttpClient _highlightImageHttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
        private bool _apiBaseReady;
        private Task? _backgroundFullSyncTask;
        private DateTime _lastHeartbeatUtc = DateTime.MinValue;
        private int _lastHeartbeatPoiId;
        private DateTime _lastPoiRefreshForGeofenceUtc = DateTime.MinValue;
        private int _pendingNavigationPoiId;
        private DateTime _lastNearestHighlightUtc = DateTime.MinValue;
    // Limit number of pins rendered to keep map responsive on low-end devices/emulators
    private const int MaxPinsToRender = 90;
    private const int MaxPinsToRenderOnEmulator = 32;
        // Cache rendered Pin instances by POI id for incremental updates
        private Dictionary<int, Microsoft.Maui.Controls.Maps.Pin> _pinByPoiId;
        private CancellationTokenSource? _mapMoveDebounceCts;

        private sealed class LoadAllCacheEntry
        {
            public DateTime CreatedUtc { get; init; }
            public string Language { get; init; } = "en";
            public List<PoiLoadAllItem> Items { get; init; } = new();
        }

        private int GetMapPinRenderLimit()
        {
            try
            {
                if (DeviceInfo.Platform == DevicePlatform.Android && DeviceInfo.DeviceType == DeviceType.Virtual)
                {
                    return MaxPinsToRenderOnEmulator;
                }
            }
            catch { }

            return MaxPinsToRender;
        }

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
            _locationPollingService.LocationUpdated += OnLocationUpdatedFromPolling;
            try { vinhKhanhMap.PropertyChanged += OnMapPropertyChanged; } catch { }
            // close POI when tapping on empty map area
            try { vinhKhanhMap.MapClicked += OnMapClicked; } catch { }

            // placeholder: action button images are now inside pill Frames (no direct named ImageButtons)

            try
            {
                // Always start with English by default.
                _currentLanguage = "en";
                Preferences.Default.Set("selected_language", _currentLanguage);
            }
            catch
            {
                _currentLanguage = "en";
            }

            // ensure language UI state and strings reflect current selection at startup
            UpdateLanguageSelectionUI();
            _ = UpdateUiStringsAsync();

            // Always show language panel when entering app.
            try
            {
                _isLanguageModalOpen = true;
                LanguagePanel.IsVisible = true;
            }
            catch { }

            // init logs collection
            _logItems = new System.Collections.ObjectModel.ObservableCollection<string>();
            try { CvLog.ItemsSource = _logItems; } catch { }

            try
            {
                // Force-disable offline map (Mapbox WebView) to prioritize online Google Map for performance/stability.
                _offlineMapEnabled = false;
                Preferences.Default.Set("offline_map_enabled", false);
            }
            catch
            {
                _offlineMapEnabled = false;
            }

            try
            {
                _offlineMapLocalEntry = string.Empty;
                Preferences.Default.Set("offline_map_local_entry", string.Empty);
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

            // Hide Mapbox WebView permanently (offline map disabled)
            try
            {
                if (MapboxOfflineWebView != null)
                {
                    MapboxOfflineWebView.Source = null;
                    MapboxOfflineWebView.IsVisible = false;
                    MapboxOfflineWebView.InputTransparent = true;
                }
            }
            catch { }

            // Highlights collection placeholder
            try { CvHighlights.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<PoiModel>(); } catch { }

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

        private void OnLocationUpdatedFromPolling(double latitude, double longitude)
        {
            try
            {
                _lastLocation = new Location(latitude, longitude);
                _ = TrackRealtimeHeartbeatAsync(latitude, longitude);
            }
            catch { }
        }

        private async Task TrackRealtimeHeartbeatAsync(double latitude, double longitude)
        {
            try
            {
                if (_apiService == null) return;
                var hasPois = _pois != null && _pois.Any();

                var now = DateTime.UtcNow;
                var nearest = hasPois
                    ? _pois
                        .Where(p => p != null && p.Id > 0)
                        .Select(p => new { Poi = p, Dist = HaversineDistanceMeters(latitude, longitude, p.Latitude, p.Longitude) })
                        .OrderBy(x => x.Dist)
                        .FirstOrDefault()
                    : null;

                var nearestPoiId = nearest?.Poi?.Id ?? 0;
                var distanceMeters = nearest?.Dist ?? -1d;

                // heartbeat mỗi 10 giây hoặc khi đổi POI gần nhất
                if ((now - _lastHeartbeatUtc).TotalSeconds < 10 && nearestPoiId == _lastHeartbeatPoiId)
                {
                    return;
                }

                _lastHeartbeatUtc = now;
                _lastHeartbeatPoiId = nearestPoiId;

                var trace = new VinhKhanh.Shared.TraceLog
                {
                    PoiId = nearestPoiId,
                    DeviceId = BuildDeviceAnalyticsId(),
                    Latitude = latitude,
                    Longitude = longitude,
                    ExtraJson = $"{{\"event\":\"poi_heartbeat\",\"source\":\"mobile_app\",\"distance\":{Math.Round(distanceMeters, 2).ToString(System.Globalization.CultureInfo.InvariantCulture)},\"lang\":\"{NormalizeLanguageCode(_currentLanguage)}\",\"hasPoi\":{(hasPois ? "true" : "false")}}}"
                };

                await _apiService.PostTraceAsync(trace);
            }
            catch { }
        }

        private async Task EnsureTrackingStartedAsync()
        {
            try
            {
                if (_isTrackingActive) return;

                var ok = await _permissionService.EnsureLocationPermissionsAsync();
                if (!ok)
                {
                    AddLog("Tracking chưa khởi động: chưa có quyền vị trí.");
                    return;
                }

                try
                {
                    await _locationPollingService.StartAsync();
                    _isTrackingActive = true;
                    AddLog("Tracking geofence đã tự khởi động.");
                }
                catch (Exception ex)
                {
                    AddLog($"Không thể khởi động tracking: {ex.Message}");
                }
            }
            catch { }
        }

        private void RefreshGeofencePoisFromCurrentState()
        {
            try
            {
                if (_pois == null || !_pois.Any()) return;
                var now = DateTime.UtcNow;
                if ((now - _lastPoiRefreshForGeofenceUtc).TotalSeconds < 1.5)
                {
                    return;
                }

                _geofenceEngine.UpdatePois(_pois);
                _lastPoiRefreshForGeofenceUtc = now;
            }
            catch { }
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

            if (lang == "ja")
            {
                return MergeLocalizedMap(enMap, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["close"] = "閉じる",
                    ["cancel"] = "キャンセル",
                    ["error"] = "エラー",
                    ["notification"] = "通知",
                    ["language"] = "言語",
                    ["search"] = "検索",
                    ["directions"] = "ルート案内",
                    ["permission_denied_title"] = "権限が拒否されました",
                    ["permission_denied_msg"] = "位置情報の権限が必要です。許可して再試行してください。",
                    ["open_settings"] = "設定を開く",
                    ["no_selected_poi_qr"] = "QR用のPOIが選択されていません。",
                    ["no_saved_poi"] = "保存済みのPOIはありません。",
                    ["saved_places"] = "保存済みスポット",
                    ["sync_success"] = "Admin/API から最新データを同期しました。",
                    ["sync_failed"] = "強制同期に失敗しました。もう一度お試しください。",
                    ["syncing"] = "同期中...",
                    ["listening"] = "再生",
                    ["stop"] = "停止",
                    ["fallback_to_english"] = "選択した言語のデータがありません。英語を使用します。"
                });
            }

            if (lang == "ko")
            {
                return MergeLocalizedMap(enMap, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["close"] = "닫기",
                    ["cancel"] = "취소",
                    ["error"] = "오류",
                    ["notification"] = "알림",
                    ["language"] = "언어",
                    ["search"] = "검색",
                    ["directions"] = "길찾기",
                    ["permission_denied_title"] = "권한 거부됨",
                    ["permission_denied_msg"] = "위치 권한이 필요합니다. 권한을 허용한 후 다시 시도하세요.",
                    ["open_settings"] = "설정 열기",
                    ["no_selected_poi_qr"] = "QR용 POI가 선택되지 않았습니다.",
                    ["no_saved_poi"] = "저장된 POI가 없습니다.",
                    ["saved_places"] = "저장된 장소",
                    ["sync_success"] = "Admin/API에서 최신 데이터가 동기화되었습니다.",
                    ["sync_failed"] = "강제 동기화에 실패했습니다. 다시 시도하세요.",
                    ["syncing"] = "동기화 중...",
                    ["listening"] = "듣기",
                    ["stop"] = "중지",
                    ["fallback_to_english"] = "선택한 언어 데이터가 없어 영어로 대체합니다."
                });
            }

            if (lang == "ru")
            {
                return MergeLocalizedMap(enMap, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["close"] = "Закрыть",
                    ["cancel"] = "Отмена",
                    ["error"] = "Ошибка",
                    ["notification"] = "Уведомление",
                    ["language"] = "Язык",
                    ["search"] = "Поиск",
                    ["directions"] = "Маршрут",
                    ["permission_denied_title"] = "Доступ запрещен",
                    ["permission_denied_msg"] = "Требуется доступ к геолокации. Разрешите доступ и повторите попытку.",
                    ["open_settings"] = "Открыть настройки",
                    ["no_selected_poi_qr"] = "POI для QR не выбран.",
                    ["no_saved_poi"] = "Нет сохраненных POI.",
                    ["saved_places"] = "Сохраненные места",
                    ["sync_success"] = "Последние данные синхронизированы из Admin/API.",
                    ["sync_failed"] = "Принудительная синхронизация не удалась. Повторите попытку.",
                    ["syncing"] = "Синхронизация...",
                    ["listening"] = "Слушать",
                    ["stop"] = "Остановить",
                    ["fallback_to_english"] = "Для выбранного языка нет данных. Используется английский."
                });
            }

            if (lang == "fr")
            {
                return MergeLocalizedMap(enMap, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["close"] = "Fermer",
                    ["cancel"] = "Annuler",
                    ["error"] = "Erreur",
                    ["notification"] = "Notification",
                    ["language"] = "Langue",
                    ["search"] = "Rechercher",
                    ["directions"] = "Itinéraire",
                    ["permission_denied_title"] = "Autorisation refusée",
                    ["permission_denied_msg"] = "L'autorisation de localisation est requise. Veuillez autoriser puis réessayer.",
                    ["open_settings"] = "Ouvrir les paramètres",
                    ["no_selected_poi_qr"] = "Aucun POI sélectionné pour le QR.",
                    ["no_saved_poi"] = "Aucun POI enregistré.",
                    ["saved_places"] = "Lieux enregistrés",
                    ["sync_success"] = "Les dernières données ont été synchronisées depuis Admin/API.",
                    ["sync_failed"] = "La synchronisation forcée a échoué. Veuillez réessayer.",
                    ["syncing"] = "Synchronisation...",
                    ["listening"] = "Écouter",
                    ["stop"] = "Arrêter",
                    ["fallback_to_english"] = "Aucune donnée pour la langue sélectionnée. Repli sur l'anglais."
                });
            }

            if (lang == "th")
            {
                return MergeLocalizedMap(enMap, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["close"] = "ปิด",
                    ["cancel"] = "ยกเลิก",
                    ["error"] = "ข้อผิดพลาด",
                    ["notification"] = "การแจ้งเตือน",
                    ["language"] = "ภาษา",
                    ["search"] = "ค้นหา",
                    ["directions"] = "นำทาง",
                    ["permission_denied_title"] = "ปฏิเสธสิทธิ์",
                    ["permission_denied_msg"] = "ต้องการสิทธิ์ตำแหน่ง โปรดอนุญาตแล้วลองใหม่อีกครั้ง",
                    ["open_settings"] = "เปิดการตั้งค่า",
                    ["no_selected_poi_qr"] = "ยังไม่ได้เลือก POI สำหรับ QR",
                    ["no_saved_poi"] = "ไม่มี POI ที่บันทึกไว้",
                    ["saved_places"] = "สถานที่ที่บันทึกไว้",
                    ["sync_success"] = "ซิงก์ข้อมูลล่าสุดจาก Admin/API เรียบร้อยแล้ว",
                    ["sync_failed"] = "ซิงก์แบบบังคับไม่สำเร็จ โปรดลองอีกครั้ง",
                    ["syncing"] = "กำลังซิงก์...",
                    ["listening"] = "ฟัง",
                    ["stop"] = "หยุด",
                    ["fallback_to_english"] = "ไม่มีข้อมูลสำหรับภาษาที่เลือก ระบบจะใช้ภาษาอังกฤษแทน"
                });
            }

            if (lang == "zh")
            {
                return MergeLocalizedMap(enMap, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["close"] = "关闭",
                    ["cancel"] = "取消",
                    ["error"] = "错误",
                    ["notification"] = "通知",
                    ["language"] = "语言",
                    ["search"] = "搜索",
                    ["directions"] = "导航",
                    ["permission_denied_title"] = "权限被拒绝",
                    ["permission_denied_msg"] = "需要位置权限。请授予权限后重试。",
                    ["open_settings"] = "打开设置",
                    ["no_selected_poi_qr"] = "未选择用于 QR 的 POI。",
                    ["no_saved_poi"] = "暂无已保存 POI。",
                    ["saved_places"] = "已保存地点",
                    ["sync_success"] = "已从 Admin/API 同步最新数据。",
                    ["sync_failed"] = "强制同步失败，请重试。",
                    ["syncing"] = "同步中...",
                    ["listening"] = "收听",
                    ["stop"] = "停止",
                    ["fallback_to_english"] = "所选语言暂无数据，已回退到英语。"
                });
            }

            if (lang == "es")
            {
                return MergeLocalizedMap(enMap, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["close"] = "Cerrar",
                    ["cancel"] = "Cancelar",
                    ["error"] = "Error",
                    ["notification"] = "Notificación",
                    ["language"] = "Idioma",
                    ["search"] = "Buscar",
                    ["directions"] = "Cómo llegar",
                    ["permission_denied_title"] = "Permiso denegado",
                    ["permission_denied_msg"] = "Se requiere permiso de ubicación. Concédelo e inténtalo de nuevo.",
                    ["open_settings"] = "Abrir configuración",
                    ["no_selected_poi_qr"] = "No hay POI seleccionado para QR.",
                    ["no_saved_poi"] = "No hay POI guardados.",
                    ["saved_places"] = "Lugares guardados",
                    ["sync_success"] = "Datos más recientes sincronizados desde Admin/API.",
                    ["sync_failed"] = "La sincronización forzada falló. Inténtalo de nuevo.",
                    ["syncing"] = "Sincronizando...",
                    ["listening"] = "Escuchar",
                    ["stop"] = "Detener",
                    ["fallback_to_english"] = "No hay datos para el idioma seleccionado. Se usará inglés."
                });
            }

            return enMap;
        }

        private static Dictionary<string, string> MergeLocalizedMap(
            Dictionary<string, string> baseMap,
            Dictionary<string, string> overrides)
        {
            var result = new Dictionary<string, string>(baseMap, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in overrides)
            {
                result[kv.Key] = kv.Value;
            }

            return result;
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
                // Clear selection so tapping the same card again still triggers SelectionChanged
                try
                {
                    if (sender is CollectionView cv) cv.SelectedItem = null;
                }
                catch { }
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
                    // IMPORTANT: Do NOT clear and recreate all pins. That causes severe stutter/crashes on Android emulator.
                    // Instead, update existing pins and add missing ones incrementally.
                    if (_pinByPoiId == null) _pinByPoiId = new Dictionary<int, Microsoft.Maui.Controls.Maps.Pin>();

                    var toRender = pinInfos.Take(MaxPinsToRender).ToList();
                    var desiredIds = new HashSet<int>(toRender.Select(x => x.Poi.Id));

                    // Remove pins that are no longer desired
                    var removeIds = _pinByPoiId.Keys.Where(id => !desiredIds.Contains(id)).ToList();
                    foreach (var id in removeIds)
                    {
                        try
                        {
                            if (_pinByPoiId.TryGetValue(id, out var pin))
                            {
                                try { vinhKhanhMap.Pins.Remove(pin); } catch { }
                            }
                            _pinByPoiId.Remove(id);
                        }
                        catch { }
                    }

                    foreach (var info in toRender)
                    {
                        var currentPoi = info.Poi;
                        if (currentPoi == null || currentPoi.Id <= 0) continue;

                        if (_pinByPoiId.TryGetValue(currentPoi.Id, out var existingPin) && existingPin != null)
                        {
                            // Update label in-place (localized title)
                            try { existingPin.Label = info.Label; } catch { }
                            continue;
                        }

                        var pin = new Pin
                        {
                            Label = info.Label,
                            Location = new Location(currentPoi.Latitude, currentPoi.Longitude),
                            Type = currentPoi.Category == "BusStop" ? PinType.SearchResult : PinType.Place
                        };

                        pin.MarkerClicked += async (s, e) =>
                        {
                            try
                            {
                                try { e.HideInfoWindow = true; } catch { }
                                await OpenPoiDetailFromSelectionAsync(currentPoi, "map_pin", userInitiated: true);
                            }
                            catch { }
                        };

                        vinhKhanhMap.Pins.Add(pin);
                        _pinByPoiId[currentPoi.Id] = pin;
                    }

                    // nearest highlight updates are throttled elsewhere to keep map rendering smooth
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
                var now = DateTime.UtcNow;
                if ((now - _lastNearestHighlightUtc).TotalSeconds < 7)
                {
                    return;
                }

                _lastNearestHighlightUtc = now;
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

        // Legacy fallback - viewport throttling is handled in MapPage.MapRendering.cs
        private void OnMapVisibleRegionChanged(object sender, EventArgs e) { }

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
            _loadAllCacheByLang.Clear();
            _poiDetailHydrateUtc.Clear();
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

                var serverContents = loadAll?.Items?
                    .Where(i => i?.Poi != null)
                    .SelectMany(i => (i?.Poi?.Contents ?? new List<ContentModel>())
                        .Where(c => c != null)
                        .Select(c =>
                        {
                            c.PoiId = i!.Poi.Id;
                            c.LanguageCode = NormalizeLanguageCode(c.LanguageCode);
                            return c;
                        }))
                    .GroupBy(c => new { c.PoiId, Lang = NormalizeLanguageCode(c.LanguageCode) })
                    .Select(g => g.OrderByDescending(ComputeContentQualityScore).ThenByDescending(x => x.Id).First())
                    .ToList() ?? new List<ContentModel>();

                if (!serverPois.Any())
                {
                    serverPois = await _apiService.GetPublishedPoisAsync() ?? new List<PoiModel>();
                }

                if (!serverPois.Any())
                {
                    AddLog("Fast sync skipped prune: server returned 0 POIs.");
                    return _pois?.Count ?? 0;
                }

                try
                {
                    await _dbService.PrunePoisNotInSnapshotAsync(serverPois.Select(p => p.Id));
                }
                catch { }

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

                if (serverContents.Any())
                {
                    var contentSemaphore = new SemaphoreSlim(Environment.ProcessorCount >= 4 ? 10 : 4);
                    var contentTasks = new List<Task>();

                    foreach (var content in serverContents)
                    {
                        await contentSemaphore.WaitAsync();
                        var c = content;
                        contentTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                await _dbService.SaveContentAsync(c);
                            }
                            catch { }
                            finally
                            {
                                try { contentSemaphore.Release(); } catch { }
                            }
                        }));
                    }

                    try { await Task.WhenAll(contentTasks); } catch { }
                }

                var syncedPois = await _dbService.GetPoisAsync();
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    _pois = syncedPois ?? new List<PoiModel>();
                    RefreshGeofencePoisFromCurrentState();
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

    }
}