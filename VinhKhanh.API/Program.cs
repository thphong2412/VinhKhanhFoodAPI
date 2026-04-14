using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;
using VinhKhanh.API.Services;
using VinhKhanh.Shared;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình CORS
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddSignalR();

// ✅ Register Services
builder.Services.AddScoped<IQrCodeService, QrCodeService>();

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

app.UseMiddleware<VinhKhanh.API.ApiKeyMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.MapHub<VinhKhanh.API.Hubs.SyncHub>("/sync");

// --- PHẦN KHỞI TẠO DB + SEED DATA ---
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        // Seed sample data if empty
        if (!db.PointsOfInterest.Any())
        {
            var poi1 = new PoiModel
            {
                Name = "Chùa Vinh Nghiêm",
                Category = "Tôn giáo",
                Latitude = 10.7769,
                Longitude = 106.7009,
                Radius = 100,
                Priority = 1,
                CooldownSeconds = 3600,
                OwnerId = 1,
                IsPublished = true,
                IsSaved = false
            };

            var poi2 = new PoiModel
            {
                Name = "Nhà Thờ Đức Bà",
                Category = "Tôn giáo",
                Latitude = 10.7827,
                Longitude = 106.6995,
                Radius = 100,
                Priority = 1,
                CooldownSeconds = 3600,
                OwnerId = 1,
                IsPublished = true,
                IsSaved = false
            };

            var poi3 = new PoiModel
            {
                Name = "Bảo Tàng TPHCM",
                Category = "Bảo tàng",
                Latitude = 10.7898,
                Longitude = 106.6974,
                Radius = 150,
                Priority = 2,
                CooldownSeconds = 3600,
                OwnerId = 2,
                IsPublished = true,
                IsSaved = false
            };

            db.PointsOfInterest.AddRange(poi1, poi2, poi3);
            db.SaveChanges();

            // Seed Content (Vietnamese)
            var content1_vi = new ContentModel
            {
                PoiId = poi1.Id,
                LanguageCode = "vi",
                Title = "Chùa Vinh Nghiêm",
                Subtitle = "Một trong những ngôi chùa nổi tiếng ở TPHCM",
                Description = "Chùa Vinh Nghiêm là một điểm tham quan nổi tiếng, được xây dựng từ thế kỷ 19. Đây là nơi thờ phụng Đức Phật Thích Ca.",
                OpeningHours = "06:00 - 17:00",
                PhoneNumber = "028 3930 1001",
                Address = "40 Nam Kỳ Khởi Nghĩa, Quận 1, TPHCM",
                Rating = 4.5,
                AudioUrl = "",
                IsTTS = false,
                ShareUrl = ""
            };

            var content2_vi = new ContentModel
            {
                PoiId = poi2.Id,
                LanguageCode = "vi",
                Title = "Nhà Thờ Đức Bà",
                Subtitle = "Biểu tượng tôn giáo của TPHCM",
                Description = "Nhà thờ Đức Bà Saigon là một nhà thờ Công giáo La Mã nổi tiếng được xây dựng trong giai đoạn thuộc địa Pháp.",
                OpeningHours = "08:00 - 16:30",
                PhoneNumber = "028 3829 4855",
                Address = "1 Công Trường Cách Mạng, Quận 1, TPHCM",
                Rating = 4.7,
                AudioUrl = "",
                IsTTS = false,
                ShareUrl = ""
            };

            var content3_vi = new ContentModel
            {
                PoiId = poi3.Id,
                LanguageCode = "vi",
                Title = "Bảo Tàng TPHCM",
                Subtitle = "Trung tâm bảo tồn lịch sử",
                Description = "Bảo tàng Thành phố Hồ Chí Minh trưng bày những tư liệu về lịch sử, văn hóa của TPHCM.",
                OpeningHours = "08:00 - 17:00",
                PhoneNumber = "028 3829 8148",
                Address = "65 Lý Tự Trọng, Quận 1, TPHCM",
                Rating = 4.3,
                AudioUrl = "",
                IsTTS = false,
                ShareUrl = ""
            };

            db.PointContents.AddRange(content1_vi, content2_vi, content3_vi);
            db.SaveChanges();

            System.Console.WriteLine("✅ Database initialized with sample data");
        }
        else
        {
            System.Console.WriteLine("✅ Database already has data");
        }
    }
    catch (Exception ex)
    {
        System.Console.WriteLine($"❌ DB Error: {ex.Message}");
    }
}

app.Run();