// For now keep a lightweight stub to avoid complex GMS binding issues in CI.
// The GeofenceEngine (Haversine) + LocationForegroundService provide reliable
// proximity detection for the POC. This stub preserves the API so native
// geofencing can be added later with the proper Play Services binding.
#if ANDROID
using System;
using System.Collections.Generic;
using Android.Content;
using Android.App;
using Android.Locations;
using VinhKhanh.Shared;

namespace VinhKhanh.Platforms.Android
{
    // Simple native proximity alerts using LocationManager.AddProximityAlert.
    // This is less feature-rich than Play Services Geofencing but avoids
    // additional bindings and works on most Android devices.
    public static class AndroidGeofenceManager
    {
        private static Context _context;
        private static LocationManager _locationManager;
        private static readonly Dictionary<int, PendingIntent> _pendingMap = new();

        public static void Initialize(Context ctx)
        {
            try
            {
                _context = ctx ?? global::Android.App.Application.Context;
                _locationManager = (LocationManager)_context.GetSystemService(Context.LocationService);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("VinhKhanh", "AndroidGeofenceManager init error: " + ex.Message);
            }
        }

        public static void RegisterGeofences(IEnumerable<PoiModel> pois)
        {
            try
            {
                if (_context == null) Initialize(global::Android.App.Application.Context);
                if (_locationManager == null) Initialize(_context);
                if (_locationManager == null) return;

                // Remove existing first
                RemoveGeofences();

                foreach (var p in pois)
                {
                    try
                    {
                        var radius = (float)Math.Max(1.0, p.Radius);
                        var intent = new Intent(_context, typeof(AndroidGeofenceBroadcastReceiver));
                        intent.SetAction("com.vinhkhanh.GEOFENCE_EVENT");
                        intent.PutExtra("poiId", p.Id);

                        var pi = PendingIntent.GetBroadcast(_context, p.Id, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
                        // -1 means no expiration
                        _locationManager.AddProximityAlert(p.Latitude, p.Longitude, radius, -1, pi);
                        _pendingMap[p.Id] = pi;
                    }
                    catch (Exception ex)
                    {
                        global::Android.Util.Log.Warn("VinhKhanh", "Register geofence for POI " + p.Id + " failed: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("VinhKhanh", "RegisterGeofences error: " + ex.Message);
            }
        }

        public static void RemoveGeofences()
        {
            try
            {
                if (_locationManager == null)
                {
                    if (_context == null) Initialize(global::Android.App.Application.Context);
                    _locationManager = (LocationManager)_context?.GetSystemService(Context.LocationService);
                }

                if (_locationManager == null) return;

                foreach (var kv in _pendingMap)
                {
                    try
                    {
                        _locationManager.RemoveProximityAlert(kv.Value);
                        kv.Value.Cancel();
                    }
                    catch { }
                }
                _pendingMap.Clear();
            }
            catch { }
        }
    }
}
#endif
