#if ANDROID
using System;
using Android.Content;

namespace VinhKhanh.Platforms.Android
{
    [BroadcastReceiver(Enabled = true, Exported = true)]
    public class AndroidGeofenceBroadcastReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            try
            {
                var geofencingEvent = global::Android.Gms.Location.GeofencingEvent.FromIntent(intent);
                if (geofencingEvent == null) return;

                if (geofencingEvent.HasError)
                {
                    var err = geofencingEvent.ErrorCode;
                    global::Android.Util.Log.Warn("VinhKhanh", "Geofence event error: " + err);
                    return;
                }

                var transition = geofencingEvent.GeofenceTransition;
                var triggeringLocation = geofencingEvent.TriggeringLocation;
                var geofences = geofencingEvent.TriggeringGeofences;

                // Broadcast a location update so shared engine can process it
                // If we have triggering geofences, try to call into shared engine via broadcast
                try
                {
                    if (geofences != null && geofences.Count > 0)
                    {
                        foreach (var gf in geofences)
                        {
                            if (int.TryParse(gf.RequestId, out var pid))
                            {
                                var intent2 = new Intent("com.vinhkhanh.TRIGGER_POI_BY_ID");
                                intent2.PutExtra("poiId", pid);
                                context.SendBroadcast(intent2);
                            }
                        }
                    }
                    else if (triggeringLocation != null)
                    {
                        var b = new Intent("com.vinhkhanh.LOCATION_UPDATE");
                        b.PutExtra("lat", triggeringLocation.Latitude);
                        b.PutExtra("lon", triggeringLocation.Longitude);
                        context.SendBroadcast(b);
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("VinhKhanh", "GeofenceBroadcastReceiver error: " + ex.Message);
            }
        }
    }
}
#endif