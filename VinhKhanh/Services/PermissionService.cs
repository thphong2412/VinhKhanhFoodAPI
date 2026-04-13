using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Extensions.Logging;

namespace VinhKhanh.Services
{
    public class PermissionService
    {
        private readonly ILogger<PermissionService> _logger;

        public PermissionService(ILogger<PermissionService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> EnsureLocationPermissionsAsync()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                    if (status != PermissionStatus.Granted)
                        return false;
                }

                // Try to request LocationAlways for background tracking on Android/iOS
                // Note: on Android LocationAlways may require a second runtime prompt
                // and on some devices the OS will direct user to Settings.
#if ANDROID
                try
                {
                    var bg = await Permissions.CheckStatusAsync<Permissions.LocationAlways>();
                    if (bg != PermissionStatus.Granted)
                    {
                        bg = await Permissions.RequestAsync<Permissions.LocationAlways>();
                        if (bg != PermissionStatus.Granted)
                        {
                            _logger.LogWarning("LocationAlways not granted; background tracking may be limited");
                            // Still return true because foreground was granted; caller can decide to prompt user
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    _logger.LogWarning(ex, "Failed requesting LocationAlways");
                }
#endif

                // If LocationWhenInUse granted at least, return true
                return true;
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "Permission request failed");
                return false;
            }
        }

#if ANDROID
        public async Task<bool> IsBackgroundLocationGrantedAsync()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.LocationAlways>();
                return status == PermissionStatus.Granted;
            }
            catch { return false; }
        }
#else
        public Task<bool> IsBackgroundLocationGrantedAsync() => Task.FromResult(true);
#endif
    }
}
