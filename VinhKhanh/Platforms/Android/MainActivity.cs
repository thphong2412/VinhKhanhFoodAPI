using Android.App;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

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

        protected override void OnCreate(Android.OS.Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Ensure location and foreground-service-location permissions are requested on startup
            TryRequestLocationPermissions();
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

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Android.Content.PM.Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            // Do nothing special here; having permissions granted allows the foreground service to start with location type.
        }
    }
}