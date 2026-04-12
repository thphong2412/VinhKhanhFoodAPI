#if ANDROID
using Android.App;
using Android.Content.PM;
using Android.Content;
using Android.OS;
using Android.Locations;
using System;

namespace VinhKhanh.Platforms.Android
{
    // Cần ghi rõ namespace để tránh lỗi CS0104 (Location của Android vs MAUI)
    [Service(Enabled = true, Exported = false, ForegroundServiceType = ForegroundService.TypeLocation | ForegroundService.TypeDataSync)]
    public class LocationForegroundService : Service, ILocationListener
    {
        public const string ChannelId = "VinhKhanh.LocationChannel";
        public const int NotificationId = 1001;
        private LocationManager _locationManager;
        private string _provider;

        public override void OnCreate()
        {
            base.OnCreate();
            CreateNotificationChannel();
            _locationManager = (LocationManager)GetSystemService(LocationService);
            _provider = _locationManager.GetBestProvider(new Criteria { Accuracy = Accuracy.Fine }, true);
        }

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            // Fix lỗi CS0103: Khai báo notification trước khi dùng
            var notification = BuildNotification("Hệ thống đang hoạt động...");

            try
            {
                // Fix lỗi CS0117: Dùng số 34 thay cho 'U' nếu SDK cũ không nhận
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

            if (_provider != null) _locationManager.RequestLocationUpdates(_provider, 5000, 1, this);

            return StartCommandResult.Sticky;
        }

        private Notification BuildNotification(string text)
        {
            var intent = new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop);
            var pending = PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            return new Notification.Builder(this, ChannelId)
                .SetContentTitle("VinhKhanh Food Street")
                .SetContentText(text)
                .SetSmallIcon(Resource.Mipmap.appicon) // Chắc chắn file appicon có trong Resources/mipmap
                .SetContentIntent(pending)
                .SetOngoing(true)
                .Build();
        }

        private void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(ChannelId, "Location Service", NotificationImportance.Low);
                // Use the NotificationService constant here. LocationService returns a LocationManager
                // which caused InvalidCastException at runtime when cast to NotificationManager.
                ((NotificationManager)GetSystemService(NotificationService)).CreateNotificationChannel(channel);
            }
        }

        public override IBinder OnBind(Intent intent) => null;

        // Fix lỗi CS0535: Dùng đúng kiểu Android.Locations.Location
        public void OnLocationChanged(global::Android.Locations.Location location)
        {
            if (location == null) return;
            var intent = new Intent("com.vinhkhanh.LOCATION_UPDATE");
            intent.PutExtra("lat", location.Latitude);
            intent.PutExtra("lon", location.Longitude);
            SendBroadcast(intent);
        }

        public void OnProviderDisabled(string provider) { }
        public void OnProviderEnabled(string provider) { }
        public void OnStatusChanged(string provider, Availability status, Bundle extras) { }
    }
}
#endif