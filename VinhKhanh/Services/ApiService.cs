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
using System.Text.Json.Serialization;
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
        private readonly ConcurrentDictionary<string, CacheEnvelope> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _singleFlightLocks = new(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan PoiListCacheTtl = TimeSpan.FromSeconds(35);
        private static readonly TimeSpan LoadAllCacheTtl = TimeSpan.FromSeconds(25);
        private static readonly TimeSpan ContentByPoiCacheTtl = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan AudioByPoiCacheTtl = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan LiveStatsCacheTtl = TimeSpan.FromSeconds(12);

        // LƯU Ý: Nếu dùng máy ảo Android, thay localhost bằng 10.0.2.2
        // Nếu dùng iPhone/Máy thật, dùng IP của máy tính (ví dụ: 192.168.1.x)
        // API Backend runs on port 7001 (https) or 5000 (http) for development
        private string BaseUrl;
        public string CurrentBaseUrl => BaseUrl;

        private sealed class CacheEnvelope
        {
            public DateTime ExpiresUtc { get; init; }
            public object? Value { get; init; }
        }

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
            // Shorter timeouts on emulator reduce "loading forever" during startup when API is not ready yet.
            var timeoutSeconds = (DeviceInfo.Platform == DevicePlatform.Android && DeviceInfo.DeviceType == DeviceType.Virtual) ? 3 : 6;
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

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

        private bool TryGetCached<T>(string key, out T? value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(key)) return false;

            if (!_memoryCache.TryGetValue(key, out var entry) || entry == null)
            {
                return false;
            }

            if (entry.ExpiresUtc <= DateTime.UtcNow)
            {
                _memoryCache.TryRemove(key, out _);
                return false;
            }

            if (entry.Value is T typed)
            {
                value = typed;
                return true;
            }

            return false;
        }

        private void SetCache<T>(string key, T value, TimeSpan ttl)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            _memoryCache[key] = new CacheEnvelope
            {
                Value = value,
                ExpiresUtc = DateTime.UtcNow.Add(ttl <= TimeSpan.Zero ? TimeSpan.FromSeconds(20) : ttl)
            };
        }

        private SemaphoreSlim GetSingleFlightLock(string key)
        {
            return _singleFlightLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
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
            const string cacheKey = "pois:list";
            if (TryGetCached(cacheKey, out List<PoiModel>? cachedPois) && cachedPois != null)
            {
                return cachedPois;
            }

            var gate = GetSingleFlightLock(cacheKey);
            await gate.WaitAsync();
            try
            {
                if (TryGetCached(cacheKey, out cachedPois) && cachedPois != null)
                {
                    return cachedPois;
                }

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
                        var payload = result ?? new List<PoiModel>();
                        SetCache(cacheKey, payload, PoiListCacheTtl);
                        return payload;
                    }
                    catch (Exception ex)
                    {
                        MarkCandidateFailure(candidate);
                        _logger?.LogWarning(ex, "[Android] Cannot reach POI endpoint {BaseUrl}", candidate);
                    }
                }

                var empty = new List<PoiModel>();
                SetCache(cacheKey, empty, TimeSpan.FromSeconds(8));
                return empty;
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
                    var payload = result ?? new List<PoiModel>();
                    SetCache(cacheKey, payload, PoiListCacheTtl);
                    return payload;
                }
                catch (Exception ex)
                {
                    MarkCandidateFailure(candidate);
                    _logger?.LogWarning(ex, "Không gọi được endpoint POI {BaseUrl}", candidate);
                }
            }

            _logger?.LogError("Lỗi gọi API POI: không endpoint nào hoạt động trong danh sách fallback");
            var fallback = new List<PoiModel>();
            SetCache(cacheKey, fallback, TimeSpan.FromSeconds(8));
            return fallback;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<List<PoiModel>> GetPublishedPoisAsync()
        {
            foreach (var candidate in GetPrioritizedBaseUrlCandidates())
            {
                try
                {
                    var url = $"{candidate}poi";
                    var result = await _httpClient.GetFromJsonAsync<List<PoiModel>>(url) ?? new List<PoiModel>();
                    MarkCandidateSuccess(candidate);
                    BaseUrl = candidate;
                    return result;
                }
                catch (Exception ex)
                {
                    MarkCandidateFailure(candidate);
                    _logger?.LogWarning(ex, "Không gọi được endpoint published POI {BaseUrl}", candidate);
                }
            }

            return new List<PoiModel>();
        }

        public async Task<PoiLoadAllResult?> GetPoisLoadAllAsync(string lang = "vi")
        {
            var l = string.IsNullOrWhiteSpace(lang) ? "vi" : lang.Trim().ToLowerInvariant();
            var includeUnpublished = Preferences.Get("IncludeUnpublishedPois", true);
            var cacheKey = $"pois:load-all:{l}:{includeUnpublished}";

            if (TryGetCached(cacheKey, out PoiLoadAllResult? cachedLoadAll) && cachedLoadAll != null)
            {
                return cachedLoadAll;
            }

            var gate = GetSingleFlightLock(cacheKey);
            await gate.WaitAsync();
            try
            {
                if (TryGetCached(cacheKey, out cachedLoadAll) && cachedLoadAll != null)
                {
                    return cachedLoadAll;
                }

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
                        SetCache(cacheKey, result, LoadAllCacheTtl);
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
            finally
            {
                gate.Release();
            }
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
            var cacheKey = $"contents:poi:{poiId}";
            if (TryGetCached(cacheKey, out List<ContentModel>? cachedContents) && cachedContents != null)
            {
                return cachedContents;
            }

            var gate = GetSingleFlightLock(cacheKey);
            await gate.WaitAsync();
            try
            {
                if (TryGetCached(cacheKey, out cachedContents) && cachedContents != null)
                {
                    return cachedContents;
                }

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
                    SetCache(cacheKey, result, ContentByPoiCacheTtl);
                    return result;
                }
                catch (Exception ex)
                {
                    MarkCandidateFailure(candidate);
                    _logger?.LogWarning(ex, "Lỗi gọi API content by poiId={PoiId} to {BaseUrl}", poiId, candidate);
                }
            }

            var empty = new List<ContentModel>();
            SetCache(cacheKey, empty, TimeSpan.FromSeconds(10));
            return empty;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<List<AudioModel>> GetAudiosByPoiIdAsync(int poiId)
        {
            var cacheKey = $"audios:poi:{poiId}";
            if (TryGetCached(cacheKey, out List<AudioModel>? cachedAudios) && cachedAudios != null)
            {
                return cachedAudios;
            }

            var gate = GetSingleFlightLock(cacheKey);
            await gate.WaitAsync();
            try
            {
                if (TryGetCached(cacheKey, out cachedAudios) && cachedAudios != null)
                {
                    return cachedAudios;
                }

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
                    SetCache(cacheKey, result, AudioByPoiCacheTtl);
                    return result;
                }
                catch (Exception ex)
                {
                    MarkCandidateFailure(candidate);
                    _logger?.LogWarning(ex, "Lỗi gọi API audio by poiId={PoiId} to {BaseUrl}", poiId, candidate);
                }
            }

            var empty = new List<AudioModel>();
            SetCache(cacheKey, empty, TimeSpan.FromSeconds(10));
            return empty;
            }
            finally
            {
                gate.Release();
            }
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
            var latKey = userLat?.ToString("0.000", CultureInfo.InvariantCulture) ?? "na";
            var lngKey = userLng?.ToString("0.000", CultureInfo.InvariantCulture) ?? "na";
            var cacheKey = $"analytics:poi-live:{top}:{latKey}:{lngKey}";
            if (TryGetCached(cacheKey, out List<PoiLiveStatsDto>? cachedLiveStats) && cachedLiveStats != null)
            {
                return cachedLiveStats;
            }

            var gate = GetSingleFlightLock(cacheKey);
            await gate.WaitAsync();
            try
            {
                if (TryGetCached(cacheKey, out cachedLiveStats) && cachedLiveStats != null)
                {
                    return cachedLiveStats;
                }

            try
            {
                var query = $"{BaseUrl}analytics/poi-live-stats?top={top}";
                if (userLat.HasValue && userLng.HasValue)
                {
                    query += $"&userLat={userLat.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}&userLng={userLng.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                }

                var payload = await _httpClient.GetFromJsonAsync<List<PoiLiveStatsDto>>(query) ?? new List<PoiLiveStatsDto>();
                SetCache(cacheKey, payload, LiveStatsCacheTtl);
                return payload;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lỗi gọi API poi-live-stats {BaseUrl}", BaseUrl);
                var empty = new List<PoiLiveStatsDto>();
                SetCache(cacheKey, empty, TimeSpan.FromSeconds(5));
                return empty;
            }
            }
            finally
            {
                gate.Release();
            }
        }

        // Post a trace log to analytics endpoint
        public async Task<bool> PostTraceAsync(TraceLog trace)
        {
            try
            {
                if (trace == null) return false;
                // ensure device id set and normalized to a stable per-install identity
                trace.DeviceId = NormalizeTraceDeviceId(trace.DeviceId);

                // If caller didn't include an explicit event in ExtraJson, provide a sensible default
                if (string.IsNullOrWhiteSpace(trace.ExtraJson))
                {
                    trace.ExtraJson = "{\"event\":\"audio_play\",\"source\":\"app_default\"}";
                }

                trace.TimestampUtc = DateTime.UtcNow;
                if (trace.Latitude is < -90 or > 90) trace.Latitude = 0;
                if (trace.Longitude is < -180 or > 180) trace.Longitude = 0;

                var posted = false;

                var res = await _httpClient.PostAsJsonAsync($"{BaseUrl}analytics", trace);
                if (res.IsSuccessStatusCode)
                {
                    // Flush queue opportunistically when network is available
                    _ = FlushTraceQueueAsync();
                    posted = true;
                }

                if (!posted)
                {
                    // fallback across candidate base urls to avoid stale BaseUrl issues
                    foreach (var candidate in GetPrioritizedBaseUrlCandidates())
                    {
                        try
                        {
                            if (string.Equals(candidate, BaseUrl, StringComparison.OrdinalIgnoreCase)) continue;

                            var altRes = await _httpClient.PostAsJsonAsync($"{candidate}analytics", trace);
                            if (!altRes.IsSuccessStatusCode) continue;

                            MarkCandidateSuccess(candidate);
                            BaseUrl = candidate;
                            _ = FlushTraceQueueAsync();
                            posted = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            MarkCandidateFailure(candidate);
                            _logger?.LogWarning(ex, "PostTrace fallback failed for {Candidate}", candidate);
                        }
                    }
                }

                if (posted) return true;

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
                        item.DeviceId = NormalizeTraceDeviceId(item.DeviceId);
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

        private string NormalizeTraceDeviceId(string? rawDeviceId)
        {
            var installId = string.IsNullOrWhiteSpace(_deviceId) ? Guid.NewGuid().ToString("N") : _deviceId;
            if (string.IsNullOrWhiteSpace(rawDeviceId))
            {
                return installId;
            }

            var parts = rawDeviceId
                .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (!parts.Any())
            {
                return installId;
            }

            if (parts.Count >= 5)
            {
                return string.Join('|', parts);
            }

            parts.Add(installId);
            return string.Join('|', parts);
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

        // Backward-compatible parsing for server payload variants
        [JsonPropertyName("poi")]
        public PoiModel? PoiRaw
        {
            get => Poi;
            set
            {
                if (value != null)
                {
                    Poi = value;
                }
            }
        }

        [JsonPropertyName("localization")]
        public JsonElement? LocalizationRaw
        {
            get => Localization;
            set
            {
                if (value.HasValue)
                {
                    Localization = value;
                }
            }
        }

        [JsonPropertyName("fallback_tier")]
        public int? FallbackTierSnakeCase
        {
            get => FallbackTier;
            set
            {
                if (value.HasValue) FallbackTier = value.Value;
            }
        }

        [JsonPropertyName("fallbackTier")]
        public int? FallbackTierCamelCase
        {
            get => FallbackTier;
            set
            {
                if (value.HasValue) FallbackTier = value.Value;
            }
        }

        // Allow flat-item payloads (legacy) to still map into Poi
        [JsonPropertyName("id")]
        public int? FlatId
        {
            get => Poi?.Id;
            set
            {
                if (!value.HasValue) return;
                Poi ??= new PoiModel();
                Poi.Id = value.Value;
            }
        }

        [JsonPropertyName("name")]
        public string? FlatName
        {
            get => Poi?.Name;
            set
            {
                if (value == null) return;
                Poi ??= new PoiModel();
                Poi.Name = value;
            }
        }

        [JsonPropertyName("latitude")]
        public double? FlatLatitude
        {
            get => Poi?.Latitude;
            set
            {
                if (!value.HasValue) return;
                Poi ??= new PoiModel();
                Poi.Latitude = value.Value;
            }
        }

        [JsonPropertyName("longitude")]
        public double? FlatLongitude
        {
            get => Poi?.Longitude;
            set
            {
                if (!value.HasValue) return;
                Poi ??= new PoiModel();
                Poi.Longitude = value.Value;
            }
        }

        [JsonPropertyName("category")]
        public string? FlatCategory
        {
            get => Poi?.Category;
            set
            {
                if (value == null) return;
                Poi ??= new PoiModel();
                Poi.Category = value;
            }
        }

        [JsonPropertyName("radius")]
        public double? FlatRadius
        {
            get => Poi?.Radius;
            set
            {
                if (!value.HasValue) return;
                Poi ??= new PoiModel();
                Poi.Radius = value.Value;
            }
        }

        [JsonPropertyName("imageUrl")]
        public string? FlatImageUrl
        {
            get => Poi?.ImageUrl;
            set
            {
                Poi ??= new PoiModel();
                Poi.ImageUrl = value;
            }
        }

        [JsonPropertyName("qrCode")]
        public string? FlatQrCode
        {
            get => Poi?.QrCode;
            set
            {
                Poi ??= new PoiModel();
                Poi.QrCode = value;
            }
        }

        [JsonPropertyName("isPublished")]
        public bool? FlatIsPublished
        {
            get => Poi?.IsPublished;
            set
            {
                if (!value.HasValue) return;
                Poi ??= new PoiModel();
                Poi.IsPublished = value.Value;
            }
        }

        [JsonPropertyName("ownerId")]
        public int? FlatOwnerId
        {
            get => Poi?.OwnerId;
            set
            {
                Poi ??= new PoiModel();
                Poi.OwnerId = value;
            }
        }

        [JsonPropertyName("cooldownSeconds")]
        public int? FlatCooldownSeconds
        {
            get => Poi?.CooldownSeconds;
            set
            {
                if (!value.HasValue) return;
                Poi ??= new PoiModel();
                Poi.CooldownSeconds = value.Value;
            }
        }

        [JsonPropertyName("priority")]
        public int? FlatPriority
        {
            get => Poi?.Priority;
            set
            {
                if (!value.HasValue) return;
                Poi ??= new PoiModel();
                Poi.Priority = value.Value;
            }
        }

        [JsonPropertyName("contents")]
        public List<ContentModel>? FlatContents
        {
            get => Poi?.Contents;
            set
            {
                if (value == null) return;
                Poi ??= new PoiModel();
                Poi.Contents = value;
            }
        }
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
