using System; // FIX LỖI: Exception, TimeSpan, Console
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Collections.Concurrent;
using System.Net.Http; // FIX LỖI: HttpClient
using System.Net.Http.Json; // FIX LỖI: GetFromJsonAsync
using System.Threading.Tasks;
using System.Text.Json;
using System.Threading;
using Microsoft.Maui.Devices; // FIX LỖI: DeviceInfo, DevicePlatform
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using VinhKhanh.Shared; // Đảm bảo đúng namespace của Model bên Shared

namespace VinhKhanh.Services
{
    public class ApiService
    {
        private HttpClient _httpClient;
        private readonly Microsoft.Extensions.Logging.ILogger<ApiService> _logger;
        private readonly string _deviceId;
        private readonly List<string> _baseUrlCandidates;
        private readonly string _traceQueuePath;
        private readonly SemaphoreSlim _traceQueueLock = new(1, 1);
        private bool _isFlushingTraceQueue = false;
        private readonly ConcurrentDictionary<string, int> _candidateFailureCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTime> _candidateBlockedUntilUtc = new(StringComparer.OrdinalIgnoreCase);

        // LƯU Ý: Nếu dùng máy ảo Android, thay localhost bằng 10.0.2.2
        // Nếu dùng iPhone/Máy thật, dùng IP của máy tính (ví dụ: 192.168.1.x)
        // API Backend runs on port 7001 (https) or 5000 (http) for development
        private string BaseUrl;
        public string CurrentBaseUrl => BaseUrl;

        public ApiService(Microsoft.Extensions.Logging.ILogger<ApiService>? logger = null)
        {
            _logger = logger;

            // Ưu tiên endpoint có thể override bằng Preferences để chạy trên máy thật (LAN IP)
            var preferredBaseUrl = Preferences.Get("ApiBaseUrl", string.Empty);
            if (string.IsNullOrWhiteSpace(preferredBaseUrl))
            {
                preferredBaseUrl = Preferences.Get("VinhKhanh_ApiBaseUrl", string.Empty);
            }

            // Default endpoint: emulator dùng 10.0.2.2, máy thật ưu tiên localhost (hỗ trợ adb reverse) hoặc ApiBaseUrl override
            // Emulator: luôn ưu tiên endpoint local, bỏ ảnh hưởng ApiBaseUrl đã lưu từ máy thật
            if (DeviceInfo.Platform == DevicePlatform.Android && DeviceInfo.DeviceType == DeviceType.Virtual)
            {
                BaseUrl = "http://10.0.2.2:5291/api/";
            }
            else
            {
                BaseUrl = !string.IsNullOrWhiteSpace(preferredBaseUrl)
                    ? NormalizeBaseUrl(preferredBaseUrl)
                    : (DeviceInfo.Platform == DevicePlatform.Android
                        ? $"http://{GetPreferredAndroidHost()}:5291/api/"
                        : "http://localhost:5291/api/");
            }

            _baseUrlCandidates = BuildBaseUrlCandidates(BaseUrl);

            // Create HTTP client with certificate validation disabled for dev (self-signed cert)
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (msg, cert, chain, errs) => true;
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };

            _logger?.LogInformation("ApiService initialized with BaseUrl = {BaseUrl}", BaseUrl);

            // Device identity for anonymous analytics
            try
            {
                _deviceId = Preferences.Get("VinhKhanh_DeviceId", null);
                if (string.IsNullOrEmpty(_deviceId))
                {
                    _deviceId = Guid.NewGuid().ToString();
                    Preferences.Set("VinhKhanh_DeviceId", _deviceId);
                }
            }
            catch { _deviceId = Guid.NewGuid().ToString(); }

            _traceQueuePath = Path.Combine(FileSystem.AppDataDirectory, "trace_queue.json");
        }

        public async Task<LocalizationPrepareResult?> PrepareLocalizationHotsetAsync(List<int> poiIds, string lang)
        {
            try
            {
                var req = new LocalizationPrepareRequest
                {
                    PoiIds = poiIds ?? new List<int>(),
                    Lang = string.IsNullOrWhiteSpace(lang) ? "en" : lang
                };

                var res = await _httpClient.PostAsJsonAsync($"{BaseUrl}localizations/prepare-hotset", req);
                if (!res.IsSuccessStatusCode) return null;
                return await res.Content.ReadFromJsonAsync<LocalizationPrepareResult>();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lỗi prepare-hotset {BaseUrl}", BaseUrl);
                return null;
            }
        }

        public async Task<MapRuntimeConfigDto?> GetMapRuntimeConfigAsync()
        {
            foreach (var candidate in GetPrioritizedBaseUrlCandidates())
            {
                try
                {
                    var result = await _httpClient.GetFromJsonAsync<MapRuntimeConfigDto>($"{candidate}maps/runtime-config");
                    if (result != null)
                    {
                        MarkCandidateSuccess(candidate);
                        BaseUrl = candidate;
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    MarkCandidateFailure(candidate);
                    _logger?.LogWarning(ex, "Lỗi map runtime-config {BaseUrl}", candidate);
                }
            }

            return null;
        }

        public async Task<LocalizationOnDemandResult?> LocalizationOnDemandAsync(int poiId, string lang)
        {
            try
            {
                var req = new LocalizationOnDemandRequest
                {
                    PoiId = poiId,
                    Lang = string.IsNullOrWhiteSpace(lang) ? "en" : lang
                };
                var res = await _httpClient.PostAsJsonAsync($"{BaseUrl}localizations/on-demand", req);
                if (!res.IsSuccessStatusCode) return null;
                return await res.Content.ReadFromJsonAsync<LocalizationOnDemandResult>();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lỗi on-demand localization {BaseUrl}", BaseUrl);
                return null;
            }
        }

        public async Task<LocalizationWarmupStatusDto?> StartLocalizationWarmupAsync(string lang)
        {
            try
            {
                var req = new LocalizationWarmupRequest { Lang = string.IsNullOrWhiteSpace(lang) ? "en" : lang };
                var res = await _httpClient.PostAsJsonAsync($"{BaseUrl}localizations/warmup", req);
                if (!res.IsSuccessStatusCode) return null;
                return await res.Content.ReadFromJsonAsync<LocalizationWarmupStatusDto>();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lỗi warmup localization {BaseUrl}", BaseUrl);
                return null;
            }
        }

        public async Task<LocalizationWarmupStatusDto?> GetLocalizationWarmupStatusAsync(string lang)
        {
            try
            {
                var l = string.IsNullOrWhiteSpace(lang) ? "en" : lang.Trim().ToLower();
                return await _httpClient.GetFromJsonAsync<LocalizationWarmupStatusDto>($"{BaseUrl}localizations/warmup/{l}/status");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lỗi get warmup status {BaseUrl}", BaseUrl);
                return null;
            }
        }

        public async Task<MapOfflineManifestDto?> GetMapOfflineManifestAsync(string version = "q4-v1")
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<MapOfflineManifestDto>($"{BaseUrl}maps/offline-manifest?version={Uri.EscapeDataString(version)}");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lỗi map offline manifest {BaseUrl}", BaseUrl);
                return null;
            }
        }

        public async Task<List<PoiModel>> GetPoisAsync()    
        {
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                var androidCandidates = GetPrioritizedBaseUrlCandidates().ToList();
                if (!androidCandidates.Any())
                {
                    androidCandidates = new List<string>
                    {
                        "http://10.0.2.2:5291/api/",
                        "https://10.0.2.2:7001/api/",
                        "http://localhost:5291/api/"
                    };
                }

                foreach (var candidate in androidCandidates)
                {
                    try
                    {
                        var url = $"{candidate}poi";
                        _logger?.LogInformation("[Android] Fetching POIs from {Url}", url);
                        var result = await _httpClient.GetFromJsonAsync<List<PoiModel>>(url);
                        MarkCandidateSuccess(candidate);
                        BaseUrl = candidate;
                        _logger?.LogInformation("[Android] Successfully fetched {Count} POIs from {BaseUrl}", result?.Count ?? 0, BaseUrl);
                        return result ?? new List<PoiModel>();
                    }
                    catch (Exception ex)
                    {
                        MarkCandidateFailure(candidate);
                        _logger?.LogWarning(ex, "[Android] Cannot reach POI endpoint {BaseUrl}", candidate);
                    }
                }

                return new List<PoiModel>();
            }

            foreach (var candidate in GetPrioritizedBaseUrlCandidates())
            {
                try
                {
                    var url = $"{candidate}poi";
                    _logger?.LogInformation("Fetching POIs from {Url}", url);
                    var result = await _httpClient.GetFromJsonAsync<List<PoiModel>>(url);

                    MarkCandidateSuccess(candidate);
                    BaseUrl = candidate;
                    _logger?.LogInformation("Successfully fetched {Count} POIs from {BaseUrl}", result?.Count ?? 0, BaseUrl);
                    return result ?? new List<PoiModel>();
                }
                catch (Exception ex)
                {
                    MarkCandidateFailure(candidate);
                    _logger?.LogWarning(ex, "Không gọi được endpoint POI {BaseUrl}", candidate);
                }
            }

            _logger?.LogError("Lỗi gọi API POI: không endpoint nào hoạt động trong danh sách fallback");
            return new List<PoiModel>();
        }

        public async Task<PoiLoadAllResult?> GetPoisLoadAllAsync(string lang = "vi")
        {
            var l = string.IsNullOrWhiteSpace(lang) ? "vi" : lang.Trim().ToLowerInvariant();
            var includeUnpublished = Preferences.Get("IncludeUnpublishedPois", true);

            foreach (var candidate in GetPrioritizedBaseUrlCandidates())
            {
                try
                {
                    var query = $"{candidate}poi/load-all?lang={Uri.EscapeDataString(l)}&includeUnpublished={(includeUnpublished ? "true" : "false")}";
                    var result = await _httpClient.GetFromJsonAsync<PoiLoadAllResult>(query);
                    if (result != null)
                    {
                        MarkCandidateSuccess(candidate);
                        BaseUrl = candidate;
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    MarkCandidateFailure(candidate);
                    _logger?.LogWarning(ex, "Lỗi gọi API poi/load-all {BaseUrl}", candidate);
                }
            }

            return null;
        }

        public async Task<PoiNearbyResult?> GetNearbyPoisAsync(double lat, double lng, double radiusMeters = 1500, int top = 10)
        {
            foreach (var candidate in GetPrioritizedBaseUrlCandidates())
            {
                try
                {
                    var query = $"{candidate}poi/nearby?lat={lat.ToString(CultureInfo.InvariantCulture)}&lng={lng.ToString(CultureInfo.InvariantCulture)}&radiusMeters={radiusMeters.ToString(CultureInfo.InvariantCulture)}&top={top}";
                    var result = await _httpClient.GetFromJsonAsync<PoiNearbyResult>(query);
                    if (result != null)
                    {
                        MarkCandidateSuccess(candidate);
                        BaseUrl = candidate;
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    MarkCandidateFailure(candidate);
                    _logger?.LogWarning(ex, "Lỗi gọi API poi/nearby {BaseUrl}", candidate);
                }
            }

            return null;
        }

        public async Task<AudioPackManifestResult?> GetAudioPackManifestAsync(string lang)
        {
            try
            {
                var l = string.IsNullOrWhiteSpace(lang) ? "vi" : lang.Trim().ToLowerInvariant();
                return await _httpClient.GetFromJsonAsync<AudioPackManifestResult>($"{BaseUrl}audio/pack-manifest?lang={Uri.EscapeDataString(l)}");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lỗi gọi API audio/pack-manifest {BaseUrl}", BaseUrl);
                return null;
            }
        }

        public async Task<List<ContentModel>> GetContentsByPoiIdAsync(int poiId)
        {
            foreach (var candidate in GetPrioritizedBaseUrlCandidates())
            {
                try
                {
                    var result = await _httpClient.GetFromJsonAsync<List<ContentModel>>($"{candidate}Content/by-poi/{poiId}") ?? new List<ContentModel>();
                    foreach (var item in result.Where(x => x != null))
                    {
                        item.PoiId = poiId;
                        item.LanguageCode = string.IsNullOrWhiteSpace(item.LanguageCode)
                            ? "vi"
                            : item.LanguageCode.Trim().ToLowerInvariant();
                    }

                    BaseUrl = candidate;
                    MarkCandidateSuccess(candidate);
                    return result;
                }
                catch (Exception ex)
                {
                    MarkCandidateFailure(candidate);
                    _logger?.LogWarning(ex, "Lỗi gọi API content by poiId={PoiId} to {BaseUrl}", poiId, candidate);
                }
            }

            return new List<ContentModel>();
        }

        public async Task<List<AudioModel>> GetAudiosByPoiIdAsync(int poiId)
        {
            foreach (var candidate in GetPrioritizedBaseUrlCandidates())
            {
                try
                {
                    var result = await _httpClient.GetFromJsonAsync<List<AudioModel>>($"{candidate}audio/by-poi/{poiId}") ?? new List<AudioModel>();
                    foreach (var item in result.Where(x => x != null))
                    {
                        item.PoiId = poiId;
                        item.LanguageCode = string.IsNullOrWhiteSpace(item.LanguageCode)
                            ? "vi"
                            : item.LanguageCode.Trim().ToLowerInvariant();
                    }

                    BaseUrl = candidate;
                    MarkCandidateSuccess(candidate);
                    return result;
                }
                catch (Exception ex)
                {
                    MarkCandidateFailure(candidate);
                    _logger?.LogWarning(ex, "Lỗi gọi API audio by poiId={PoiId} to {BaseUrl}", poiId, candidate);
                }
            }

            return new List<AudioModel>();
        }

        public async Task<List<TourModel>> GetToursAsync()
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<TourModel>>($"{BaseUrl}tour");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lỗi gọi API tour {BaseUrl}", BaseUrl);
                return new List<TourModel>();
            }
        }

        public async Task<List<PoiLiveStatsDto>> GetPoiLiveStatsAsync(double? userLat = null, double? userLng = null, int top = 50)
        {
            try
            {
                var query = $"{BaseUrl}analytics/poi-live-stats?top={top}";
                if (userLat.HasValue && userLng.HasValue)
                {
                    query += $"&userLat={userLat.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}&userLng={userLng.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                }

                return await _httpClient.GetFromJsonAsync<List<PoiLiveStatsDto>>(query) ?? new List<PoiLiveStatsDto>();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lỗi gọi API poi-live-stats {BaseUrl}", BaseUrl);
                return new List<PoiLiveStatsDto>();
            }
        }

        // Post a trace log to analytics endpoint
        public async Task<bool> PostTraceAsync(TraceLog trace)
        {
            try
            {
                if (trace == null) return false;
                // ensure device id set
                if (string.IsNullOrEmpty(trace.DeviceId)) trace.DeviceId = _deviceId;

                // If caller didn't include an explicit event in ExtraJson, provide a sensible default
                if (string.IsNullOrWhiteSpace(trace.ExtraJson))
                {
                    trace.ExtraJson = "{\"event\":\"audio_play\",\"source\":\"app_default\"}";
                }

                var res = await _httpClient.PostAsJsonAsync($"{BaseUrl}analytics", trace);
                if (res.IsSuccessStatusCode)
                {
                    // Flush queue opportunistically when network is available
                    _ = FlushTraceQueueAsync();
                    return true;
                }

                await EnqueueTraceAsync(trace);
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to post trace to API");
                try { await EnqueueTraceAsync(trace); } catch { }
                return false;
            }
        }

        private async Task EnqueueTraceAsync(TraceLog trace)
        {
            if (trace == null) return;

            await _traceQueueLock.WaitAsync();
            try
            {
                var queue = await LoadTraceQueueAsync();

                // Avoid unbounded growth on long offline sessions
                if (queue.Count >= 2000)
                {
                    queue = queue.OrderByDescending(x => x.TimestampUtc).Take(1500).ToList();
                }

                queue.Add(trace);
                await SaveTraceQueueAsync(queue);
            }
            finally
            {
                _traceQueueLock.Release();
            }
        }

        private async Task FlushTraceQueueAsync()
        {
            if (_isFlushingTraceQueue) return;
            _isFlushingTraceQueue = true;

            try
            {
                await _traceQueueLock.WaitAsync();
                List<TraceLog> queue;
                try
                {
                    queue = await LoadTraceQueueAsync();
                }
                finally
                {
                    _traceQueueLock.Release();
                }

                if (queue.Count == 0) return;

                var remaining = new List<TraceLog>();
                foreach (var item in queue.Take(200))
                {
                    try
                    {
                        if (string.IsNullOrEmpty(item.DeviceId)) item.DeviceId = _deviceId;
                        var res = await _httpClient.PostAsJsonAsync($"{BaseUrl}analytics", item);
                        if (!res.IsSuccessStatusCode)
                        {
                            remaining.Add(item);
                        }
                    }
                    catch
                    {
                        remaining.Add(item);
                    }
                }

                // Keep items that were not attempted in this batch
                remaining.AddRange(queue.Skip(200));

                await _traceQueueLock.WaitAsync();
                try
                {
                    await SaveTraceQueueAsync(remaining);
                }
                finally
                {
                    _traceQueueLock.Release();
                }
            }
            finally
            {
                _isFlushingTraceQueue = false;
            }
        }

        private async Task<List<TraceLog>> LoadTraceQueueAsync()
        {
            try
            {
                if (!File.Exists(_traceQueuePath)) return new List<TraceLog>();
                var json = await File.ReadAllTextAsync(_traceQueuePath);
                if (string.IsNullOrWhiteSpace(json)) return new List<TraceLog>();
                return JsonSerializer.Deserialize<List<TraceLog>>(json) ?? new List<TraceLog>();
            }
            catch
            {
                return new List<TraceLog>();
            }
        }

        private async Task SaveTraceQueueAsync(List<TraceLog> queue)
        {
            try
            {
                var json = JsonSerializer.Serialize(queue ?? new List<TraceLog>());
                await File.WriteAllTextAsync(_traceQueuePath, json);
            }
            catch
            {
                // no-op
            }
        }

        private static string NormalizeBaseUrl(string rawBaseUrl)
        {
            var value = rawBaseUrl?.Trim() ?? string.Empty;
            if (!value.EndsWith("/")) value += "/";
            if (!value.EndsWith("api/", StringComparison.OrdinalIgnoreCase)) value += "api/";
            return value;
        }

        private static List<string> BuildBaseUrlCandidates(string primary)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(primary))
            {
                candidates.Add(NormalizeBaseUrl(primary));
            }

            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                if (DeviceInfo.DeviceType == DeviceType.Virtual)
                {
                    candidates.Add("http://10.0.2.2:5291/api/");
                    candidates.Add("http://localhost:5291/api/");
                }
                else
                {
                    // Real device: ưu tiên LAN IP của máy dev, sau đó mới fallback localhost/10.0.2.2
                            candidates.Add("http://host.docker.internal:5291/api/");
                            candidates.Add("http://192.168.1.7:5291/api/");
                    candidates.Add("http://localhost:5291/api/");
                    candidates.Add("http://10.0.2.2:5291/api/");
                }
            }
            else
            {
                candidates.Add("http://localhost:5291/api/");
            }

            return candidates;
        }

        private static string GetPreferredAndroidHost()
        {
            // Emulators should keep 10.0.2.2; physical devices should prefer LAN IP.
            if (DeviceInfo.DeviceType == DeviceType.Virtual)
            {
                return "10.0.2.2";
            }

            return "192.168.1.7";
        }

        private IEnumerable<string> GetPrioritizedBaseUrlCandidates()
        {
            var merged = BuildBaseUrlCandidates(BaseUrl)
                .Concat(_baseUrlCandidates)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeBaseUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(x => string.Equals(x, BaseUrl, StringComparison.OrdinalIgnoreCase) || !IsCandidateBlocked(x))
                .ToList();

            if (DeviceInfo.Platform == DevicePlatform.Android && DeviceInfo.DeviceType == DeviceType.Virtual)
            {
                var emulatorFirst = new[]
                {
                    "http://10.0.2.2:5291/api/",
                    "http://localhost:5291/api/"
                };

                var ordered = emulatorFirst
                    .Concat(merged)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeBaseUrl)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return ordered;
            }

            // Ưu tiên endpoint đã thành công gần nhất để tránh timeout lặp lại
            merged.Sort((a, b) => string.Equals(a, BaseUrl, StringComparison.OrdinalIgnoreCase) ? -1 :
                                string.Equals(b, BaseUrl, StringComparison.OrdinalIgnoreCase) ? 1 : 0);

            return merged;
        }

        private bool IsCandidateBlocked(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            if (_candidateBlockedUntilUtc.TryGetValue(candidate, out var until))
            {
                if (until > DateTime.UtcNow)
                {
                    return true;
                }

                _candidateBlockedUntilUtc.TryRemove(candidate, out _);
            }

            return false;
        }

        private void MarkCandidateSuccess(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return;
            _candidateFailureCounts.TryRemove(candidate, out _);
            _candidateBlockedUntilUtc.TryRemove(candidate, out _);
        }

        private void MarkCandidateFailure(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return;

            var failures = _candidateFailureCounts.AddOrUpdate(candidate, 1, (_, current) => Math.Min(current + 1, 8));
            var backoffSeconds = Math.Min(120, 5 * (int)Math.Pow(2, Math.Max(0, failures - 1)));
            _candidateBlockedUntilUtc[candidate] = DateTime.UtcNow.AddSeconds(backoffSeconds);
        }
    }

    public class PoiLoadAllResult
    {
        public string Lang { get; set; } = "vi";
        public int Total { get; set; }
        public List<PoiLoadAllItem> Items { get; set; } = new();
    }

    public class PoiLoadAllItem
    {
        public PoiModel Poi { get; set; }
        public JsonElement? Localization { get; set; }
        public int FallbackTier { get; set; }
    }

    public class PoiNearbyResult
    {
        public JsonElement? Center { get; set; }
        public double RadiusMeters { get; set; }
        public int Total { get; set; }
        public List<PoiNearbyItem> Items { get; set; } = new();
    }

    public class PoiNearbyItem
    {
        public PoiModel Poi { get; set; }
        public double DistanceMeters { get; set; }
    }

    public class AudioPackManifestResult
    {
        public string Lang { get; set; } = "vi";
        public string PackVersion { get; set; } = "empty";
        public int TotalFiles { get; set; }
        public long TotalBytes { get; set; }
        public List<AudioPackFileItem> Files { get; set; } = new();
    }

    public class AudioPackFileItem
    {
        public string File { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public DateTime MtimeUtc { get; set; }
    }

    public class MapRuntimeConfigDto
    {
        public string MapboxAccessToken { get; set; } = string.Empty;
    }
}
