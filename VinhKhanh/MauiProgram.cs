using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using CommunityToolkit.Maui;
using ZXing.Net.Maui.Controls;
using VinhKhanh.Services;
using VinhKhanh.Data;
using VinhKhanh.PageModels;
using VinhKhanh.Pages; // <--- QUAN TRỌNG: Thêm cái này để nhận diện thư mục Pages
using Syncfusion.Maui.Toolkit.Hosting;
#if ANDROID
using Microsoft.Maui.Maps.Handlers;
using Android.Gms.Maps;
using Android.Gms.Maps.Model;
using Java.Lang;
#endif

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
                // Fluent icon font used by AppStyles.xaml (FontFamily="FluentUI")
                fonts.AddFont("FluentSystemIcons-Regular.ttf", "FluentUI");
            });

#if ANDROID
        MapHandler.Mapper.AppendToMapping("HideNativeMyLocationButton", (handler, view) =>
        {
            try
            {
                handler.PlatformView?.GetMapAsync(new InlineMapReadyCallback(googleMap =>
                {
                    try
                    {
                        if (googleMap?.UiSettings != null)
                        {
                            googleMap.UiSettings.MyLocationButtonEnabled = false;
                        }
                    }
                    catch { }
                }));
            }
            catch { }
        });
#endif

        // --- 1. Đăng ký các Repository ---
        // Core POI repository
        builder.Services.AddSingleton<PoiRepository>();
        // Legacy repositories (vẫn đang được dùng bởi các PageModel quản trị nội bộ)
        builder.Services.AddSingleton<TaskRepository>();
        builder.Services.AddSingleton<TagRepository>();
        builder.Services.AddSingleton<CategoryRepository>();
        builder.Services.AddSingleton<ProjectRepository>();

        // --- 2. Đăng ký các Dịch vụ (Services) ---
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<ApiService>(provider => new ApiService(provider.GetRequiredService<ILogger<ApiService>>()));
        builder.Services.AddSingleton<NarrationService>();
        builder.Services.AddSingleton<SignalRSyncService>();  // ✅ SignalR service
        builder.Services.AddSingleton<RealtimeSyncManager>();  // ✅ Real-time sync manager with ApiService support

        // ✅ Performance Optimization Services
        builder.Services.AddSingleton<IOptimizedPoiService, OptimizedPoiService>();
        builder.Services.AddSingleton<ISyncBatchService, SyncBatchService>();

        // ✅ 4-Tier Audio Provider System
        builder.Services.AddSingleton<IPreGeneratedAudioProvider, PreGeneratedAudioProvider>();
        builder.Services.AddSingleton<IEdgeTtsProvider>(provider =>
            new EdgeTtsProvider(
                provider.GetRequiredService<HttpClient>(),
                provider.GetRequiredService<ILogger<EdgeTtsProvider>>()));
        builder.Services.AddSingleton<ICloudTtsProvider>(provider =>
            new CloudTtsProvider(
                provider.GetRequiredService<HttpClient>(),
                provider.GetRequiredService<ILogger<CloudTtsProvider>>()));
        builder.Services.AddSingleton<IAudioProviderFactory, AudioProviderFactory>();

        // ✅ Translation Caching & Hotset Service
        builder.Services.AddSingleton<ITranslationCacheService, TranslationCacheService>();
        builder.Services.AddSingleton<IOfflineStorageService, OfflineStorageService>();

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
        builder.Services.AddSingleton<ModalErrorHandler>();
        builder.Services.AddSingleton<IErrorHandler>(sp => sp.GetRequiredService<ModalErrorHandler>());
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
        builder.Services.AddTransient<ProjectListPage>();
        builder.Services.AddTransient<ProjectDetailPage>();
        builder.Services.AddTransient<TaskDetailPage>();
        builder.Services.AddTransient<ManageMetaPage>();
        builder.Services.AddTransient<MapPage>();
        builder.Services.AddTransient<ScanPage>();

        // --- 4. Đăng ký các PageModel dùng DI ---
        builder.Services.AddTransient<ProjectListPageModel>();
        builder.Services.AddTransient<ProjectDetailPageModel>();
        builder.Services.AddTransient<TaskDetailPageModel>();
        builder.Services.AddTransient<ManageMetaPageModel>();

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

#if ANDROID
internal sealed class InlineMapReadyCallback : Java.Lang.Object, Android.Gms.Maps.IOnMapReadyCallback
{
    private readonly Action<Android.Gms.Maps.GoogleMap> _onReady;

    public InlineMapReadyCallback(Action<Android.Gms.Maps.GoogleMap> onReady)
    {
        _onReady = onReady;
    }

    public void OnMapReady(Android.Gms.Maps.GoogleMap googleMap)
    {
        _onReady?.Invoke(googleMap);
    }
}
#endif
