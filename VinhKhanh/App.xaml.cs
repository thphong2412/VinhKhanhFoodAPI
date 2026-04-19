using Microsoft.Maui.Controls;
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
                var signalRService = IPlatformApplication.Current?.Services?.GetRequiredService<SignalRSyncService>();
                if (signalRService != null)
                {
                    // Do not block UI thread on startup with network retries.
                    _ = Task.Run(async () =>
                    {
                        try { await signalRService.ConnectForDeviceAsync(); } catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SignalR connection error: {ex.Message}");
            }
        }

        protected override async void OnResume()
        {
            base.OnResume();
            // ✅ Reconnect to SignalR when app resumes
            try
            {
                var signalRService = IPlatformApplication.Current?.Services?.GetRequiredService<SignalRSyncService>();
                if (signalRService != null && !signalRService.IsConnected)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await signalRService.ConnectForDeviceAsync(); } catch { }
                    });
                }
            }
            catch { }
        }
    }
}