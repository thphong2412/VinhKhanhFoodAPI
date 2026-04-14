using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using CommunityToolkit.Maui;
using ZXing.Net.Maui.Controls;
using VinhKhanh.Services;
using VinhKhanh.Data;
using VinhKhanh.Pages; // <--- QUAN TRỌNG: Thêm cái này để nhận diện thư mục Pages
using Syncfusion.Maui.Toolkit.Hosting;

namespace VinhKhanh;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureSyncfusionToolkit()
            .UseMauiCommunityToolkit()
            .UseBarcodeReader()
            .UseMauiMaps()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // --- 1. Đăng ký các Repository ---
        builder.Services.AddSingleton<ProjectRepository>();
        builder.Services.AddSingleton<TaskRepository>();
        builder.Services.AddSingleton<TagRepository>();
        builder.Services.AddSingleton<CategoryRepository>();
        builder.Services.AddSingleton<PoiRepository>();

        // --- 2. Đăng ký các Dịch vụ (Services) ---
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<ApiService>(provider => new ApiService(provider.GetRequiredService<ILogger<ApiService>>()));
        builder.Services.AddSingleton<NarrationService>();
        builder.Services.AddSingleton<SignalRSyncService>();  // ✅ SignalR service
        builder.Services.AddSingleton<RealtimeSyncManager>();  // ✅ Real-time sync manager
#if ANDROID
        builder.Services.AddSingleton<IAudioGenerator, VinhKhanh.Platforms.Android.TtsFileGenerator>();
#endif
#if ANDROID
        // Use non-generic registration to avoid ambiguous extension method overloads
        builder.Services.AddSingleton(typeof(IAudioService), typeof(VinhKhanh.Platforms.Android.AndroidAudioService));
#elif IOS
        builder.Services.AddSingleton(typeof(IAudioService), typeof(VinhKhanh.Platforms.iOS.iOSAudioService));
#else
        // Fallback no-op implementation
        builder.Services.AddSingleton(typeof(IAudioService), typeof(VinhKhanh.Services.NoOpAudioService));
#endif
        builder.Services.AddSingleton<AudioQueueService>();
        builder.Services.AddSingleton<PermissionService>();
        // Geofence engine used for POI proximity detection (POC, foreground)
        builder.Services.AddSingleton<IGeofenceEngine, GeofenceEngine>();
        // Location polling service (POC). Runs while app is active; for true background tracking we use Android foreground service.
        // Register LocationPollingService with DatabaseService dependency
        builder.Services.AddSingleton<LocationPollingService>(provider =>
            new LocationPollingService(
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LocationPollingService>>(),
                provider.GetRequiredService<IGeofenceEngine>(),
                provider.GetRequiredService<PoiRepository>(),
                provider.GetRequiredService<DatabaseService>()));
        builder.Services.AddSingleton<SeedDataService>();
        builder.Services.AddSingleton<DatabaseService>(); // Đã đăng ký SQLite ở đây

        // --- 3. Đăng ký các Trang (Pages) - CỰC KỲ QUAN TRỌNG ---
        // Phải đăng ký thì Constructor Injection mới chạy được
        builder.Services.AddTransient<MapPage>();
        builder.Services.AddTransient<ScanPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var mauiApp = builder.Build();

        // Initialize Android Geofence manager
#if ANDROID
        try { VinhKhanh.Platforms.Android.AndroidGeofenceManager.Initialize(Android.App.Application.Context); } catch { }
#endif

        // NOTE: Do NOT start LocationPollingService automatically here.
        // Starting the polling service will start a foreground service on Android which
        // requires runtime location permissions. Start the service only after the
        // user grants permissions (MapPage.OnStartTrackingClicked calls StartAsync).

        return mauiApp;
    }
}
// test app