using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Storage;
using VinhKhanh.Data;
using VinhKhanh.Shared;
using VinhKhanh.Services;

namespace VinhKhanh.Services
{
    // Background polling service for location (POC)
    public class LocationPollingService : IDisposable
    {
        private readonly ILogger<LocationPollingService> _logger;
        private readonly IGeofenceEngine _geofenceEngine;
        private readonly PoiRepository _poiRepository;
        private readonly DatabaseService _dbService;
        private readonly ApiService _apiService;
        private List<PoiModel> _pois = new();
        private CancellationTokenSource _cts;
        private Task _executingTask;
        #if ANDROID
        private Android.Content.BroadcastReceiver _androidReceiver;
        #endif

        // Poll interval milliseconds
        private int _intervalMs = 5000;
        private const int IntervalMsForegroundFast = 3000;
        private const int IntervalMsBackgroundSlow = 3500;
        private const int MinMoveMetersToEmit = 10;
        private DateTime _lastEmitUtc = DateTime.MinValue;
        private double? _lastEmitLat;
        private double? _lastEmitLng;

        public event Action<double, double>? LocationUpdated;

        public LocationPollingService(ILogger<LocationPollingService> logger, IGeofenceEngine geofenceEngine, PoiRepository poiRepository, DatabaseService dbService, ApiService apiService)
        {
            _logger = logger;
            _geofenceEngine = geofenceEngine;
            _poiRepository = poiRepository;
            _dbService = dbService;
            _apiService = apiService;
        }


        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("LocationPollingService starting");
            if (_executingTask != null && !_executingTask.IsCompleted)
            {
                _logger.LogInformation("LocationPollingService already running");
                return Task.CompletedTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _executingTask = Task.Run(() => ExecuteAsync(_cts.Token));
            return Task.CompletedTask;
        }

        private async Task ExecuteAsync(CancellationToken token)
        {
            try
            {
                // Load POIs from app's main SQLite source-of-truth first (DatabaseService)
                var pois = await _dbService.GetPoisAsync();
                if (pois == null || pois.Count == 0)
                {
                    // Fallback for legacy path
                    pois = await _poiRepository.ListAsync();
                }

                if (pois == null || pois.Count == 0)
                {
                    _logger.LogInformation("[LocationPollingService] No local POIs found. Skipping geofence registration.");
                    pois = new List<PoiModel>();
                }

                _pois = pois;
                _logger.LogInformation($"[LocationPollingService] Loaded {_pois.Count} POIs for geofence");
                _geofenceEngine.UpdatePois(_pois);

#if ANDROID
                try
                {
                    VinhKhanh.Platforms.Android.AndroidGeofenceManager.RegisterGeofences(_pois);
                }
                catch { }
#endif

                // On Android start a foreground service for continued background tracking (POC)
                #if ANDROID
                try
                {
                    var ctx = Android.App.Application.Context;
                    var intent = new Android.Content.Intent(ctx, typeof(global::VinhKhanh.Platforms.Android.LocationForegroundService));
                    if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
                    {
                        _logger.LogInformation("Starting Android foreground service (StartForegroundService)...");
                        ctx.StartForegroundService(intent);
                        _logger.LogInformation("Requested StartForegroundService intent sent");
                    }
                    else
                    {
                        _logger.LogInformation("Starting Android service (StartService)...");
                        ctx.StartService(intent);
                        _logger.LogInformation("Requested StartService intent sent");
                    }

                    // Register broadcast receiver to receive updates from the native foreground service
                    if (_androidReceiver == null)
                    {
                        _androidReceiver = new AndroidLocationReceiver(_geofenceEngine);
                        var filt = new Android.Content.IntentFilter("com.vinhkhanh.LOCATION_UPDATE");
                        Android.App.Application.Context.RegisterReceiver(_androidReceiver, filt);
                        _logger.LogInformation("Registered Android location broadcast receiver");
                    }
                }
                catch { }
                #endif

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        _intervalMs = GetAdaptiveIntervalMs();

                        var request = new GeolocationRequest(GeolocationAccuracy.Best);
                        // Use a short timeout for location requests so the background loop
                        // doesn't hang indefinitely if the platform/location provider
                        // blocks or takes too long (especially on emulators).
                        using var locCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        locCts.CancelAfter(TimeSpan.FromSeconds(8));
                        var location = await Geolocation.Default.GetLocationAsync(request, locCts.Token);
                        if (location != null)
                        {
                            if (ShouldSkipLocationEmit(location.Latitude, location.Longitude))
                            {
                                await Task.Delay(_intervalMs, token);
                                continue;
                            }

                            _logger.LogInformation($"LocationPollingService obtained location: {location.Latitude},{location.Longitude}");
                            _geofenceEngine.ProcessLocation(location.Latitude, location.Longitude);
                            try { LocationUpdated?.Invoke(location.Latitude, location.Longitude); } catch { }
                            try { await TrackAnonymousRouteAsync(location.Latitude, location.Longitude, _pois); } catch { }
                            _lastEmitLat = location.Latitude;
                            _lastEmitLng = location.Longitude;
                            _lastEmitUtc = DateTime.UtcNow;
                        }
                        else
                        {
                            _logger.LogInformation("LocationPollingService: location==null");
                        }

                        // nothing else
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error obtaining location");
                    }

                    await Task.Delay(_intervalMs, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LocationPollingService failed");
            }
        }

        private async Task TrackAnonymousRouteAsync(double latitude, double longitude, IReadOnlyList<PoiModel> pois)
        {
            try
            {
                if (_apiService == null) return;

                var nearest = (pois ?? Array.Empty<PoiModel>())
                    .Where(p => p != null && p.Id > 0 && p.IsPublished)
                    .Where(p => p != null && p.Id > 0)
                    .Select(p => new { Poi = p, Dist = HaversineMeters(latitude, longitude, p.Latitude, p.Longitude) })
                    .OrderBy(x => x.Dist)
                    .FirstOrDefault();

                var poiId = nearest?.Poi?.Id ?? 0;
                var distance = nearest?.Dist ?? 0d;
                var trace = new TraceLog
                {
                    PoiId = poiId,
                    DeviceId = BuildDeviceAnalyticsId(),
                    Latitude = latitude,
                    Longitude = longitude,
                    ExtraJson = $"{{\"event\":\"poi_heartbeat\",\"source\":\"mobile_app\",\"trigger\":\"background_location\",\"distance\":{Math.Round(distance, 2).ToString(System.Globalization.CultureInfo.InvariantCulture)},\"hasPoi\":{(poiId > 0 ? "true" : "false")}}}",
                    TimestampUtc = DateTime.UtcNow
                };

                await _apiService.PostTraceAsync(trace);
            }
            catch { }
        }

        private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000;
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double ToRadians(double deg) => deg * (Math.PI / 180.0);

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

#if ANDROID
        class AndroidLocationReceiver : Android.Content.BroadcastReceiver
        {
            private readonly IGeofenceEngine _engine;
            public AndroidLocationReceiver(IGeofenceEngine engine) { _engine = engine; }
            public override void OnReceive(Android.Content.Context context, Android.Content.Intent intent)
            {
                try
                {
                    double lat = intent.GetDoubleExtra("lat", 0);
                    double lon = intent.GetDoubleExtra("lon", 0);
                    _engine.ProcessLocation(lat, lon);
                }
                catch { }
            }
        }
#endif

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_executingTask == null) return;

            _logger.LogInformation("LocationPollingService stopping");
            _cts.Cancel();
            try
            {
                await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken));
            }
            catch (OperationCanceledException) { }
            finally
            {
                // Unregister receiver if registered
                #if ANDROID
                try
                {
                    if (_androidReceiver != null)
                    {
                        Android.App.Application.Context.UnregisterReceiver(_androidReceiver);
                        _androidReceiver = null;
                        _logger.LogInformation("AndroidLocationReceiver unregistered");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to unregister Android receiver");
                }
                #endif
                _executingTask = null;
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        private bool ShouldSkipLocationEmit(double lat, double lng)
        {
            if (!_lastEmitLat.HasValue || !_lastEmitLng.HasValue)
            {
                return false;
            }

            var distance = HaversineMeters(_lastEmitLat.Value, _lastEmitLng.Value, lat, lng);
            var elapsed = DateTime.UtcNow - _lastEmitUtc;
            if (distance < MinMoveMetersToEmit && elapsed < TimeSpan.FromSeconds(3.5))
            {
                return true;
            }

            return false;
        }

        private static int GetAdaptiveIntervalMs()
        {
            try
            {
                if (DeviceInfo.DeviceType == DeviceType.Virtual)
                {
                    // Emulator thường chạy cùng nhiều project -> giảm tần suất polling để tránh nghẽn UI/CPU
                    return IntervalMsBackgroundSlow;
                }

                return IntervalMsForegroundFast;
            }
            catch
            {
                return IntervalMsBackgroundSlow;
            }
        }
    }
}
