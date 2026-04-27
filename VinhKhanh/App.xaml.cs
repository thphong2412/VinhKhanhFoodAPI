using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;

namespace VinhKhanh
{
    public partial class App : Application
    {
        private readonly SignalRSyncService _signalRService;

        public App()
        {
            InitializeComponent();
            MainPage = new AppShell();
        }

        protected override async void OnStart()
        {
            base.OnStart();
            // ✅ Connect to SignalR when app starts
            try
            {
                var services = IPlatformApplication.Current?.Services;
                var signalRService = services?.GetRequiredService<SignalRSyncService>();
                if (signalRService != null)
                {
                    // Do not block UI thread on startup with network retries.
                    _ = Task.Run(async () =>
                    {
                        try { await signalRService.ConnectForDeviceAsync(); } catch { }
                    });
                }

                var locationPollingService = services?.GetRequiredService<Services.LocationPollingService>();
                if (locationPollingService != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await locationPollingService.StartAsync(); } catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Startup service error: {ex.Message}");
            }
        }

        protected override async void OnResume()
        {
            base.OnResume();
            // ✅ Reconnect to SignalR when app resumes
            try
            {
                var services = IPlatformApplication.Current?.Services;
                var signalRService = services?.GetRequiredService<SignalRSyncService>();
                if (signalRService != null && !signalRService.IsConnected)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await signalRService.ConnectForDeviceAsync(); } catch { }
                    });
                }

                var locationPollingService = services?.GetRequiredService<Services.LocationPollingService>();
                if (locationPollingService != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await locationPollingService.StartAsync(); } catch { }
                    });
                }
            }
            catch { }
        }
    }
}
