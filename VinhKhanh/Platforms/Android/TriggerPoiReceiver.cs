#if ANDROID
using Android.Content;
using VinhKhanh.Services;
using Microsoft.Maui.Hosting;

namespace VinhKhanh.Platforms.Android
{
    [BroadcastReceiver(Enabled = true, Exported = true)]
    public class TriggerPoiReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            try
            {
                if (intent?.Action != "com.vinhkhanh.TRIGGER_POI_BY_ID") return;
                var id = intent.GetIntExtra("poiId", 0);
                if (id <= 0) return;

                // Resolve GeofenceEngine from MAUI app services
                try
                {
                    var maui = MauiApplication.Current.Services;
                    var engine = maui.GetService(typeof(IGeofenceEngine)) as IGeofenceEngine;
                    engine?.TriggerPoiById(id);
                }
                catch { }
            }
            catch { }
        }
    }
}
#endif
