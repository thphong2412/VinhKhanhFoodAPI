#if ANDROID
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using Android.Runtime;
using Microsoft.Maui.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VinhKhanh.Data;
using VinhKhanh.Shared;

namespace VinhKhanh.Platforms.Android
{
    [Service(Enabled = true, Exported = false, ForegroundServiceType = ForegroundService.TypeLocation | ForegroundService.TypeDataSync)]
    public class LocationForegroundService : Service, global::Android.Locations.ILocationListener
    {
        public const string ChannelId = "VinhKhanh.LocationChannel";
        public const int NotificationId = 1001;

        private const string ArrivalChannelId = "VinhKhanh.ArrivalChannel";
        private const int ArrivalBaseNotificationId = 200000;
        private const int ArrivalCooldownSeconds = 35;
        private const int PoiReloadSeconds = 90;

        private global::Android.Gms.Location.FusedLocationProviderClient _fusedClient;
        private global::Android.Gms.Location.LocationRequest _locationRequest;
        private global::Android.Gms.Location.LocationCallback _locationCallback;

        private readonly object _arrivalLock = new();
        private readonly HashSet<int> _insidePoiIds = new();
        private readonly Dictionary<int, DateTime> _lastArrivalByPoiId = new();
        private List<PoiModel> _pois = new();
        private DateTime _lastPoiLoadUtc = DateTime.MinValue;

        public override void OnCreate()
        {
            base.OnCreate();
            CreateNotificationChannel();
            CreateArrivalNotificationChannel();
            try
            {
                _fusedClient = global::Android.Gms.Location.LocationServices.GetFusedLocationProviderClient(this);

                _locationRequest = global::Android.Gms.Location.LocationRequest.Create()
                    .SetPriority(global::Android.Gms.Location.LocationRequest.PriorityHighAccuracy)
                    .SetInterval(5000)
                    .SetFastestInterval(2000)
                    .SetSmallestDisplacement(1);

                _locationCallback = new FusedLocationCallback(this);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("VinhKhanh", "FusedLocation init error: " + ex.Message);
            }
        }

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            var notification = BuildNotification("Hệ thống đang hoạt động...");

            try
            {
                if ((int)Build.VERSION.SdkInt >= 34)
                {
                    var hasLoc = CheckSelfPermission(global::Android.Manifest.Permission.AccessFineLocation) == Permission.Granted;
                    var hasFgs = CheckSelfPermission("android.permission.FOREGROUND_SERVICE_LOCATION") == Permission.Granted;

                    if (hasLoc && hasFgs)
                        StartForeground(NotificationId, notification, ForegroundService.TypeLocation);
                    else
                        StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
                }
                else
                {
                    StartForeground(NotificationId, notification);
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("VinhKhanh", "StartForeground Error: " + ex.Message);
            }

            try
            {
                if (_fusedClient != null)
                {
                    if (CheckSelfPermission(global::Android.Manifest.Permission.AccessFineLocation) == Permission.Granted)
                    {
                        _fusedClient.RequestLocationUpdates(_locationRequest, _locationCallback, Looper.MainLooper);
                    }
                }
                else
                {
                    global::Android.Util.Log.Warn("VinhKhanh", "FusedLocationProviderClient unavailable; cannot request location updates in foreground service");
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("VinhKhanh", "RequestLocationUpdates error: " + ex.Message);
            }

            return StartCommandResult.Sticky;
        }

        private Notification BuildNotification(string text)
        {
            var intent = new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop);
            var pending = PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            return new Notification.Builder(this, ChannelId)
                .SetContentTitle("VinhKhanh Food Street")
                .SetContentText(text)
                .SetSmallIcon(Resource.Mipmap.appicon)
                .SetContentIntent(pending)
                .SetOngoing(true)
                .Build();
        }

        private void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(ChannelId, "Location Service", NotificationImportance.Low);
                ((NotificationManager)GetSystemService(NotificationService)).CreateNotificationChannel(channel);
            }
        }

        private void CreateArrivalNotificationChannel()
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
                var manager = GetSystemService(NotificationService) as NotificationManager;
                if (manager == null) return;

                var channel = manager.GetNotificationChannel(ArrivalChannelId);
                if (channel != null) return;

                channel = new NotificationChannel(ArrivalChannelId, "POI Arrival", NotificationImportance.High)
                {
                    Description = "Thông báo khi người dùng đến POI"
                };
                channel.EnableVibration(true);
                manager.CreateNotificationChannel(channel);
            }
            catch { }
        }

        public override IBinder OnBind(Intent intent) => null;

        public void OnLocationChanged(global::Android.Locations.Location location)
        {
            if (location == null) return;
            _ = HandleLocationUpdateAsync(location.Latitude, location.Longitude);
        }

        private async Task HandleLocationUpdateAsync(double latitude, double longitude)
        {
            try
            {
                var intent = new Intent("com.vinhkhanh.LOCATION_UPDATE");
                intent.PutExtra("lat", latitude);
                intent.PutExtra("lon", longitude);
                SendBroadcast(intent);

                await EnsurePoisLoadedAsync();
                if (_pois == null || _pois.Count == 0) return;

                var candidates = _pois
                    .Where(p => p != null && p.Id > 0 && p.IsPublished)
                    .Select(p => new
                    {
                        Poi = p,
                        Distance = HaversineDistanceMeters(latitude, longitude, p.Latitude, p.Longitude),
                        Radius = Math.Max(1d, p.Radius)
                    })
                    .Where(x => x.Distance <= x.Radius)
                    .OrderByDescending(x => x.Poi.Priority)
                    .ThenBy(x => x.Distance)
                    .ToList();

                var insideNow = candidates.Select(x => x.Poi.Id).ToHashSet();

                PoiModel? toNotify = null;
                lock (_arrivalLock)
                {
                    var exited = _insidePoiIds.Where(id => !insideNow.Contains(id)).ToList();
                    foreach (var id in exited)
                    {
                        _insidePoiIds.Remove(id);
                    }

                    foreach (var c in candidates)
                    {
                        if (_insidePoiIds.Contains(c.Poi.Id))
                        {
                            continue;
                        }

                        if (_lastArrivalByPoiId.TryGetValue(c.Poi.Id, out var lastArrivalUtc)
                            && (DateTime.UtcNow - lastArrivalUtc).TotalSeconds < ArrivalCooldownSeconds)
                        {
                            continue;
                        }

                        toNotify = c.Poi;
                        _insidePoiIds.Add(c.Poi.Id);
                        _lastArrivalByPoiId[c.Poi.Id] = DateTime.UtcNow;
                        break;
                    }

                    foreach (var id in insideNow)
                    {
                        _insidePoiIds.Add(id);
                    }
                }

                if (toNotify != null)
                {
                    ShowArrivalNotification(toNotify.Id, toNotify.Name);
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn("VinhKhanh", "HandleLocationUpdate error: " + ex.Message);
            }
        }

        private void ShowArrivalNotification(int poiId, string? poiName)
        {
            try
            {
                var manager = GetSystemService(NotificationService) as NotificationManager;
                if (manager == null) return;

                var safePoiName = string.IsNullOrWhiteSpace(poiName) ? $"POI {poiId}" : poiName.Trim();

                var openIntent = new Intent(this, typeof(MainActivity));
                openIntent.SetAction("com.vinhkhanh.OPEN_POI_FROM_NOTIFICATION");
                openIntent.PutExtra("poiId", poiId);
                openIntent.PutExtra("poiName", safePoiName);
                openIntent.PutExtra("autoPlayTts", true);
                openIntent.PutExtra("fromPoiNotification", true);
                openIntent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop | ActivityFlags.NewTask);

                var pendingIntent = PendingIntent.GetActivity(
                    this,
                    poiId,
                    openIntent,
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

                var body = $"Bạn đã đến địa điểm: {safePoiName} !";

                var notification = new Notification.Builder(this, ArrivalChannelId)
                    .SetContentTitle("VinhKhanh Food Street")
                    .SetContentText(body)
                    .SetStyle(new Notification.BigTextStyle().BigText(body))
                    .SetSmallIcon(Resource.Mipmap.appicon)
                    .SetAutoCancel(true)
                    .SetContentIntent(pendingIntent)
                    .Build();

                manager.Notify(ArrivalBaseNotificationId + Math.Abs(poiId), notification);
            }
            catch { }
        }

        private async Task EnsurePoisLoadedAsync()
        {
            try
            {
                if (_pois.Count > 0 && (DateTime.UtcNow - _lastPoiLoadUtc).TotalSeconds < PoiReloadSeconds)
                {
                    return;
                }

                var dbService = MauiApplication.Current?.Services?.GetService(typeof(DatabaseService)) as DatabaseService;
                if (dbService == null) return;

                var pois = await dbService.GetPoisAsync();
                if (pois == null || pois.Count == 0) return;

                _pois = pois;
                _lastPoiLoadUtc = DateTime.UtcNow;
            }
            catch { }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            try
            {
                if (_fusedClient != null && _locationCallback != null)
                {
                    _fusedClient.RemoveLocationUpdates(_locationCallback);
                }
            }
            catch { }
        }

        public void OnProviderDisabled(string provider) { }
        public void OnProviderEnabled(string provider) { }
        public void OnStatusChanged(string provider, Availability status, Bundle extras) { }

        private static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
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

        private class FusedLocationCallback : global::Android.Gms.Location.LocationCallback
        {
            private readonly LocationForegroundService _svc;
            public FusedLocationCallback(LocationForegroundService svc) { _svc = svc; }

            public override void OnLocationResult(global::Android.Gms.Location.LocationResult result)
            {
                try
                {
                    var loc = result.LastLocation;
                    if (loc == null) return;
                    _ = _svc.HandleLocationUpdateAsync(loc.Latitude, loc.Longitude);
                }
                catch { }
            }
        }
    }
}
#endif
