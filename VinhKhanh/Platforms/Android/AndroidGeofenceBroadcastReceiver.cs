#if ANDROID
using System;
using Android.Content;
using Android.App;
using Android.OS;
using AndroidX.Core.App;

namespace VinhKhanh.Platforms.Android
{
    [BroadcastReceiver(Enabled = true, Exported = true)]
    [IntentFilter(new[] { "com.vinhkhanh.GEOFENCE_EVENT" })]
    public class AndroidGeofenceBroadcastReceiver : BroadcastReceiver
    {
        private const string ArrivalChannelId = "VinhKhanh.ArrivalChannel";
        private const string ArrivalDedupPrefs = "vinhkhanh_arrival_dedup";
        private const int ArrivalDedupSeconds = 20;

        public override void OnReceive(Context context, Intent intent)
        {
            try
            {
                if (context == null || intent == null) return;
                if (!string.Equals(intent.Action, "com.vinhkhanh.GEOFENCE_EVENT", StringComparison.OrdinalIgnoreCase)) return;

                var entering = intent.GetBooleanExtra(global::Android.Locations.LocationManager.KeyProximityEntering, false);
                if (!entering)
                {
                    return;
                }

                var poiId = intent.GetIntExtra("poiId", 0);
                if (poiId <= 0)
                {
                    return;
                }

                if (ShouldSkipDuplicateArrival(context, poiId))
                {
                    return;
                }

                var poiName = intent.GetStringExtra("poiName") ?? $"POI {poiId}";

                try
                {
                    var triggerIntent = new Intent("com.vinhkhanh.TRIGGER_POI_BY_ID");
                    triggerIntent.PutExtra("poiId", poiId);
                    triggerIntent.PutExtra("poiName", poiName);
                    context.SendBroadcast(triggerIntent);
                }
                catch { }

                ShowArrivalNotification(context, poiId, poiName);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("VinhKhanh", "GeofenceBroadcastReceiver error: " + ex.Message);
            }
        }

        private static void ShowArrivalNotification(Context context, int poiId, string poiName)
        {
            try
            {
                var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
                if (manager == null) return;

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    var channel = manager.GetNotificationChannel(ArrivalChannelId);
                    if (channel == null)
                    {
                        channel = new NotificationChannel(ArrivalChannelId, "POI Arrival", NotificationImportance.High)
                        {
                            Description = "Thông báo khi đến vùng POI"
                        };
                        channel.EnableVibration(true);
                        manager.CreateNotificationChannel(channel);
                    }
                }

                var openIntent = new Intent(context, typeof(MainActivity));
                openIntent.SetAction("com.vinhkhanh.OPEN_POI_FROM_NOTIFICATION");
                openIntent.PutExtra("poiId", poiId);
                openIntent.PutExtra("poiName", poiName ?? string.Empty);
                openIntent.PutExtra("autoPlayTts", true);
                openIntent.PutExtra("fromPoiNotification", true);
                openIntent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop | ActivityFlags.NewTask);

                var pendingIntent = PendingIntent.GetActivity(
                    context,
                    poiId,
                    openIntent,
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

                var title = "VinhKhanh Food Street";
                var body = $"Bạn đã đến điểm: {poiName}!";

                var notification = new NotificationCompat.Builder(context, ArrivalChannelId)
                    .SetSmallIcon(Resource.Mipmap.appicon)
                    .SetContentTitle(title)
                    .SetContentText(body)
                    .SetStyle(new NotificationCompat.BigTextStyle().BigText(body))
                    .SetPriority((int)NotificationPriority.High)
                    .SetAutoCancel(true)
                    .SetContentIntent(pendingIntent)
                    .Build();

                manager.Notify(200000 + Math.Abs(poiId), notification);
            }
            catch { }
        }

        private static bool ShouldSkipDuplicateArrival(Context context, int poiId)
        {
            try
            {
                var prefs = context.GetSharedPreferences(ArrivalDedupPrefs, FileCreationMode.Private);
                if (prefs == null) return false;

                var key = $"poi:{poiId}";
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var last = prefs.GetLong(key, 0);
                if (last > 0 && (now - last) < ArrivalDedupSeconds)
                {
                    return true;
                }

                prefs.Edit()?.PutLong(key, now)?.Apply();
            }
            catch { }

            return false;
        }
    }
}
#endif