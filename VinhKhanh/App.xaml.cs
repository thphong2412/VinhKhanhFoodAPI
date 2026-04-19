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
                    await signalRService.ConnectForDeviceAsync();
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
                    await signalRService.ConnectForDeviceAsync();
                }
            }
            catch { }
        }
    }
}