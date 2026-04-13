using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;
using VinhKhanh.Shared;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình CORS
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddSignalR();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (builder.Environment.IsDevelopment())
{
    var sqliteFile = System.IO.Path.Combine(builder.Environment.ContentRootPath, "vinhkhanh_dev.db");
    builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={sqliteFile}"));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
}

builder.Services.AddControllers();

// Swagger & Auth (Giữ nguyên cấu hình cũ của ông)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.MapHub<VinhKhanh.API.Hubs.SyncHub>("/sync");

// --- PHẦN KHỞI TẠO DB & SEED DATA ---
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Database.EnsureCreated();
        System.Console.WriteLine("✅ Database initialized");

        if (!db.PointsOfInterest.Any())
        {
            System.Console.WriteLine("Seeding POI data...");

            var pois = new List<(string name, string category, double lat, double lng)>
            {
                ("Ốc Oanh 534", "Food", 10.7584, 106.7058),
                ("Ốc Vũ", "Food", 10.7578, 106.7050),
                ("Trạm Xe Buýt", "BusStop", 10.7570, 106.7045),
                ("Công Viên Vĩnh Khánh", "Attraction", 10.7592, 106.7065),
                ("Nhà Truyền Thống Vĩnh Khánh", "Attraction", 10.7572, 106.7068),
                ("Nhà Hàng Làng Xưa", "Restaurant", 10.7580, 106.7055),
                ("Nhà Hàng Bếp Quê", "Restaurant", 10.7582, 106.7060),
                ("Quán Ăn Sài Gòn Ngon", "Restaurant", 10.7575, 106.7052),
                ("Nhà Hàng Hương Xưa", "Restaurant", 10.7576, 106.7062),
                ("Quán Cơm Bình Dân Kim", "Restaurant", 10.7579, 106.7048),
                ("Nhà Hàng Hải Sản Phố", "Restaurant", 10.7581, 106.7049),
            };

            foreach (var item in pois)
            {
                db.PointsOfInterest.Add(new PoiModel
                {
                    Name = item.name,
                    Category = item.category,
                    Latitude = item.lat,
                    Longitude = item.lng,
                    Radius = 50,
                    Priority = 0,
                    CooldownSeconds = 60,
                    ImageUrl = "https://via.placeholder.com/150",
                    WebsiteUrl = "https://vinhkhanh.vn", // Fix lỗi NOT NULL
                    QrCode = "QR_CODE_DEFAULT",           // Fix lỗi NOT NULL
                    IsPublished = true,
                    IsSaved = false
                });
            }

            db.SaveChanges();
            System.Console.WriteLine($"✅ Seeded {pois.Count} POI(s)");
        }
    }
    catch (Exception ex)
    {
        System.Console.WriteLine($"❌ DB Error: {ex.Message}");
    }
}

app.Run();