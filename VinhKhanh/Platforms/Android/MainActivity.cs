using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
using System;

namespace VinhKhanh
{
    [Activity(
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        const int RequestLocationPermissionsId = 1000;
        const int RequestNotificationPermissionsId = 1001;
        private const string OpenPoiAction = "com.vinhkhanh.OPEN_POI_FROM_NOTIFICATION";

        protected override void OnCreate(Android.OS.Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Ensure location and foreground-service-location permissions are requested on startup
            TryRequestLocationPermissions();
            TryRequestNotificationPermission();
            TryCapturePoiNotificationIntent(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            TryCapturePoiNotificationIntent(intent);
        }

        void TryRequestLocationPermissions()
        {
            try
            {
                // Required runtime permissions
                var perms = new[] {
                    Android.Manifest.Permission.AccessFineLocation,
                    Android.Manifest.Permission.AccessCoarseLocation,
                    // FOREGROUND_SERVICE_LOCATION is a newer permission name; request it on Android 14+
                    "android.permission.FOREGROUND_SERVICE_LOCATION"
                };

                var missing = new System.Collections.Generic.List<string>();
                foreach (var p in perms)
                {
                    if (CheckSelfPermission(p) != Android.Content.PM.Permission.Granted)
                        missing.Add(p);
                }

                if (missing.Count > 0)
                {
                    AndroidX.Core.App.ActivityCompat.RequestPermissions(this, missing.ToArray(), RequestLocationPermissionsId);
                }
            }
            catch { }
        }

        void TryRequestNotificationPermission()
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
                    return;

                var permission = Android.Manifest.Permission.PostNotifications;
                if (CheckSelfPermission(permission) != Android.Content.PM.Permission.Granted)
                {
                    AndroidX.Core.App.ActivityCompat.RequestPermissions(this, new[] { permission }, RequestNotificationPermissionsId);
                }
            }
            catch { }
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Android.Content.PM.Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            // Do nothing special here; having permissions granted allows the foreground service to start with location type.
        }

        private void TryCapturePoiNotificationIntent(Intent? intent)
        {
            try
            {
                if (intent == null) return;

                var fromPoiNotification = intent.GetBooleanExtra("fromPoiNotification", false)
                    || string.Equals(intent.Action, OpenPoiAction, StringComparison.OrdinalIgnoreCase);
                if (!fromPoiNotification) return;

                var poiId = intent.GetIntExtra("poiId", 0);
                if (poiId <= 0) return;

                var autoPlayTts = intent.GetBooleanExtra("autoPlayTts", true);
                var poiName = intent.GetStringExtra("poiName") ?? string.Empty;

                Preferences.Default.Set("pending_poi_id", poiId);
                Preferences.Default.Set("pending_poi_autoplay", autoPlayTts);
                Preferences.Default.Set("pending_poi_name", poiName);
                Preferences.Default.Set("pending_poi_received_utc", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                Preferences.Default.Set("pending_poi_from_notification", true);
            }
            catch { }
        }
    }
}