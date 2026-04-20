using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VinhKhanh.Services;
using VinhKhanh.Data;
using Microsoft.Maui.Devices.Sensors;

namespace VinhKhanh.Services
{
    // Background polling service for location (POC)
    public class LocationPollingService : IDisposable
    {
        private readonly ILogger<LocationPollingService> _logger;
        private readonly IGeofenceEngine _geofenceEngine;
        private readonly PoiRepository _poiRepository;
        private readonly DatabaseService _dbService;
        private CancellationTokenSource _cts;
        private Task _executingTask;
        #if ANDROID
        private Android.Content.BroadcastReceiver _androidReceiver;
        #endif

        // Poll interval milliseconds
        private int _intervalMs = 5000;

        public event Action<double, double>? LocationUpdated;

        public LocationPollingService(ILogger<LocationPollingService> logger, IGeofenceEngine geofenceEngine, PoiRepository poiRepository, DatabaseService dbService)
        {
            _logger = logger;
            _geofenceEngine = geofenceEngine;
            _poiRepository = poiRepository;
            _dbService = dbService;
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
                // Load POIs once at startup
                var pois = await _poiRepository.ListAsync();
                if (pois == null || pois.Count == 0)
                {
                    _logger.LogInformation("[LocationPollingService] No local POIs found. Skipping seeding (POIs are managed by Admin web via API).");
                    pois = new System.Collections.Generic.List<VinhKhanh.Shared.PoiModel>();
                }
                _logger.LogInformation($"[LocationPollingService] Loaded {pois.Count} POIs");
                _geofenceEngine.UpdatePois(pois);

#if ANDROID
                try
                {
                    VinhKhanh.Platforms.Android.AndroidGeofenceManager.RegisterGeofences(pois);
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
                        var request = new GeolocationRequest(GeolocationAccuracy.Best);
                        // Use a short timeout for location requests so the background loop
                        // doesn't hang indefinitely if the platform/location provider
                        // blocks or takes too long (especially on emulators).
                        using var locCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        locCts.CancelAfter(TimeSpan.FromSeconds(8));
                        var location = await Geolocation.Default.GetLocationAsync(request, locCts.Token);
                        if (location != null)
                        {
                            _logger.LogInformation($"LocationPollingService obtained location: {location.Latitude},{location.Longitude}");
                            _geofenceEngine.ProcessLocation(location.Latitude, location.Longitude);
                            try { LocationUpdated?.Invoke(location.Latitude, location.Longitude); } catch { }
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
    }
}
