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

                // Background (LocationAlways) permission on Android/iOS often requires
                // a separate user action in OS settings or a different flow. Requesting
                // LocationAlways programmatically can show intrusive system dialogs or
                // block the UI in some emulator/device combinations. For stability we
                // only request LocationWhenInUse here and return success if that is
                // granted. If you need LocationAlways, prompt the user in-app and
                // guide them to settings instead of automatic request.

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Permission request failed");
                return false;
            }
        }
    }
}
