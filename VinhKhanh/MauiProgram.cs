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

        // --- 2. Đăng ký các Dịch vụ (Services) ---
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<ApiService>();
        builder.Services.AddSingleton<NarrationService>();
        // Geofence engine used for POI proximity detection (POC, foreground)
        builder.Services.AddSingleton<IGeofenceEngine, GeofenceEngine>();
        builder.Services.AddSingleton<SeedDataService>();
        builder.Services.AddSingleton<DatabaseService>(); // Đã đăng ký SQLite ở đây

        // --- 3. Đăng ký các Trang (Pages) - CỰC KỲ QUAN TRỌNG ---
        // Phải đăng ký thì Constructor Injection mới chạy được
        builder.Services.AddTransient<MapPage>();
        builder.Services.AddTransient<ScanPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}