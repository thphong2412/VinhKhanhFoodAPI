using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Cryptography;
using VinhKhanh.API.Data;
using VinhKhanh.API.Models;
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
builder.Services.AddScoped<IPoiCleanupService, PoiCleanupService>();

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
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "VinhKhanh.API";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "VinhKhanh.Clients";
var jwtKey = builder.Configuration["Jwt:Key"] ?? "dev-super-secret-key-please-change";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminApi", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Admin", "SuperAdmin", "admin", "super_admin");
    });
});

// Swagger & Auth (Giữ nguyên cấu hình cũ của ông)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseCors();

app.UseAuthentication();

app.UseMiddleware<VinhKhanh.API.ApiKeyMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.MapHub<VinhKhanh.API.Hubs.SyncHub>("/sync");
app.UseStaticFiles();

// --- PHẦN KHỞI TẠO DB + SEED DATA ---
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        HashSet<string> GetSqliteColumns(string tableName)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                conn.Open();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info('{tableName.Replace("'", "''")}');";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
            }

            return result;
        }

        void EnsureSqliteColumns(string tableName, params (string Name, string SqlType)[] columns)
        {
            var existing = GetSqliteColumns(tableName);
            foreach (var column in columns)
            {
                if (existing.Contains(column.Name)) continue;

                db.Database.ExecuteSqlRaw($"ALTER TABLE {tableName} ADD COLUMN {column.Name} {column.SqlType}");
            }
        }

        // Đồng bộ schema bảng PoiRegistrations cho SQLite dev (tránh lỗi 400 ở Admin Pending khi thêm cột mới)
        if (db.Database.IsSqlite())
        {
            EnsureSqliteColumns("PoiRegistrations",
                ("ContentTitle", "TEXT"),
                ("ContentSubtitle", "TEXT"),
                ("ContentDescription", "TEXT"),
                ("ContentPriceMin", "TEXT"),
                ("ContentPriceMax", "TEXT"),
                ("ContentRating", "REAL"),
                ("ContentOpenTime", "TEXT"),
                ("ContentCloseTime", "TEXT"),
                ("ContentPhoneNumber", "TEXT"),
                ("ContentAddress", "TEXT"),
                ("RequestType", "TEXT"),
                ("TargetPoiId", "INTEGER")
            );

            // Đồng bộ bảng Users cho trường hợp DB dev cũ thiếu cột mới
            EnsureSqliteColumns("Users",
                ("PermissionsJson", "TEXT"),
                ("CreatedAt", "TEXT"),
                ("IsVerified", "INTEGER NOT NULL DEFAULT 0")
            );

            EnsureSqliteColumns("OwnerRegistrations",
                ("Notes", "TEXT")
            );

            EnsureSqliteColumns("PoiReviews",
                ("PoiId", "INTEGER"),
                ("Rating", "INTEGER"),
                ("Comment", "TEXT"),
                ("LanguageCode", "TEXT"),
                ("DeviceId", "TEXT"),
                ("IsHidden", "INTEGER NOT NULL DEFAULT 0"),
                ("CreatedAtUtc", "TEXT")
            );
        }

        var forceReset = builder.Configuration.GetValue<bool>("DevSeed:ForceReset");
        var shouldSeed = forceReset || !db.PointsOfInterest.Any();

        if (!shouldSeed)
        {
            System.Console.WriteLine("Skip dev reset/seed (DevSeed:ForceReset=false và đã có dữ liệu). Giữ nguyên audio/POI hiện có.");
            app.Run();
            return;
        }

        System.Console.WriteLine("Resetting POI/Content data and reseeding 10 nearby POIs around 10.7731577,106.582758...");

        db.PointContents.RemoveRange(db.PointContents);
        db.PointsOfInterest.RemoveRange(db.PointsOfInterest);

        // Seed owner accounts để đồng bộ với POI ownership trong admin web
        var oldOwnerRegs = db.OwnerRegistrations.ToList();
        if (oldOwnerRegs.Any()) db.OwnerRegistrations.RemoveRange(oldOwnerRegs);

        var oldOwnerUsers = db.Users.Where(u => u.Role == "owner").ToList();
        if (oldOwnerUsers.Any()) db.Users.RemoveRange(oldOwnerUsers);

        db.SaveChanges();

        static string HashSeedPassword(string password)
        {
            var salt = "static-salt";
            var bytes = Encoding.UTF8.GetBytes(salt + password);
            return Convert.ToBase64String(SHA256.HashData(bytes));
        }

        var ownerUsers = new List<User>();
        for (var i = 1; i <= 10; i++)
        {
            ownerUsers.Add(new User
            {
                Email = $"owner{i}@vinhkhanh.local",
                PasswordHash = HashSeedPassword("Owner@123"),
                Role = "owner",
                PermissionsJson = "owner.poi.read,owner.poi.create,owner.poi.update,owner.analytics.read",
                IsVerified = true
            });
        }
        db.Users.AddRange(ownerUsers);
        db.SaveChanges();

        var ownerRegs = ownerUsers.Select((u, idx) => new OwnerRegistration
        {
            UserId = u.Id,
            ShopName = $"Quán mẫu Owner {idx + 1}",
            ShopAddress = $"Vĩnh Khánh, Quận 4, TP.HCM (Owner {idx + 1})",
            CccdEncrypted = EncryptionService.Protect($"0792{idx + 1:00000000}"),
            Status = "approved",
            Notes = "Auto seeded",
            ReviewedBy = 1,
            ReviewedAt = DateTime.UtcNow,
            SubmittedAt = DateTime.UtcNow
        }).ToList();
        db.OwnerRegistrations.AddRange(ownerRegs);
        db.SaveChanges();

        var pois = new[]
        {
            new PoiModel { Name = "🍜 Phở Góc Bình Minh", Category = "Ẩm thực", Latitude = 10.7735200, Longitude = 106.5831200, Radius = 60, Priority = 1, CooldownSeconds = 60, ImageUrl = "https://images.unsplash.com/photo-1585032226651-759b368d7246?w=400&h=300&fit=crop", OwnerId = ownerUsers[0].Id, IsPublished = true },
            new PoiModel { Name = "☕ Cà Phê Mộc Quán", Category = "Đồ uống", Latitude = 10.7729400, Longitude = 106.5822100, Radius = 60, Priority = 2, CooldownSeconds = 60, ImageUrl = "https://images.unsplash.com/photo-1495474472287-4d71bcdd2085?w=400&h=300&fit=crop", OwnerId = ownerUsers[1].Id, IsPublished = true },
            new PoiModel { Name = "🥖 Bánh Mì Nướng Giòn", Category = "Ăn vặt", Latitude = 10.7738800, Longitude = 106.5829900, Radius = 60, Priority = 3, CooldownSeconds = 60, ImageUrl = "https://images.unsplash.com/photo-1484723091739-30a097e8f929?w=400&h=300&fit=crop", OwnerId = ownerUsers[2].Id, IsPublished = true },
            new PoiModel { Name = "🍲 Lẩu Đêm Phố Mới", Category = "Ẩm thực", Latitude = 10.7724200, Longitude = 106.5835400, Radius = 60, Priority = 4, CooldownSeconds = 60, ImageUrl = "https://images.unsplash.com/photo-1541544741938-0af808871cc0?w=400&h=300&fit=crop", OwnerId = ownerUsers[3].Id, IsPublished = true },
            new PoiModel { Name = "🍹 Trà Chanh Chill Spot", Category = "Đồ uống", Latitude = 10.7731400, Longitude = 106.5818600, Radius = 60, Priority = 5, CooldownSeconds = 60, ImageUrl = "https://images.unsplash.com/photo-1470337458703-46ad1756a187?w=400&h=300&fit=crop", OwnerId = ownerUsers[4].Id, IsPublished = true },
            new PoiModel { Name = "🦐 Ốc Đêm 106", Category = "Hải sản", Latitude = 10.7740800, Longitude = 106.5823500, Radius = 60, Priority = 6, CooldownSeconds = 60, ImageUrl = "https://images.unsplash.com/photo-1615141982883-c7ad0e69fd62?w=400&h=300&fit=crop", OwnerId = ownerUsers[5].Id, IsPublished = true },
            new PoiModel { Name = "🍚 Cơm Tấm Nhà Gỗ", Category = "Ẩm thực", Latitude = 10.7727600, Longitude = 106.5840400, Radius = 60, Priority = 7, CooldownSeconds = 60, ImageUrl = "https://images.unsplash.com/photo-1546069901-ba9599a7e63c?w=400&h=300&fit=crop", OwnerId = ownerUsers[6].Id, IsPublished = true },
            new PoiModel { Name = "🍢 Xiên Nướng Vườn Nhỏ", Category = "Ăn vặt", Latitude = 10.7734600, Longitude = 106.5809800, Radius = 60, Priority = 8, CooldownSeconds = 60, ImageUrl = "https://images.unsplash.com/photo-1529563021893-cc83c992d75d?w=400&h=300&fit=crop", OwnerId = ownerUsers[7].Id, IsPublished = true },
            new PoiModel { Name = "🥤 Nước Ép Tươi 79", Category = "Đồ uống", Latitude = 10.7729800, Longitude = 106.5843200, Radius = 60, Priority = 9, CooldownSeconds = 60, ImageUrl = "https://images.unsplash.com/photo-1497534446932-c925b458314e?w=400&h=300&fit=crop", OwnerId = ownerUsers[8].Id, IsPublished = true },
            new PoiModel { Name = "🍛 Quán Nhà Lá", Category = "Ẩm thực", Latitude = 10.7742600, Longitude = 106.5837600, Radius = 60, Priority = 10, CooldownSeconds = 60, ImageUrl = "https://images.unsplash.com/photo-1569718212165-3a8278d5f624?w=400&h=300&fit=crop", OwnerId = ownerUsers[9].Id, IsPublished = true }
        };

        db.PointsOfInterest.AddRange(pois);
        db.SaveChanges();

        var publicBaseUrl = (builder.Configuration["QrPublicBaseUrl"] ?? builder.Configuration["PublicBaseUrl"] ?? "http://localhost:5291").Trim().TrimEnd('/');
        var defaultLang = (builder.Configuration["DefaultLanguage"] ?? "vi").Trim().ToLowerInvariant();
        foreach (var poi in pois)
        {
            poi.QrCode = $"{publicBaseUrl}/qr/{poi.Id}?lang={Uri.EscapeDataString(defaultLang)}";
        }
        db.SaveChanges();

        var contents = new[]
        {
            new ContentModel { PoiId = pois[0].Id, LanguageCode = "vi", Title = "Phở Góc Bình Minh", Subtitle = "Phở bò truyền thống", Description = "Quán phở nước dùng ngọt thanh, phục vụ nhanh cho khách đi làm buổi sáng.", PriceMin = "45k", PriceMax = "95k", Address = "534 Đường Vĩnh Khánh, Phường 8, Quận 4, TP. Hồ Chí Minh", OpenTime = "06:00", CloseTime = "22:00", Rating = 4.6 },
            new ContentModel { PoiId = pois[1].Id, LanguageCode = "vi", Title = "Cà Phê Mộc Quán", Subtitle = "Cà phê máy & pha phin", Description = "Không gian cà phê yên tĩnh, có máy lạnh và ổ cắm phù hợp làm việc ngắn.", PriceMin = "29k", PriceMax = "79k", Address = "487 Đường Vĩnh Khánh, Phường 8, Quận 4, TP. Hồ Chí Minh", OpenTime = "07:00", CloseTime = "23:00", Rating = 4.4 },
            new ContentModel { PoiId = pois[2].Id, LanguageCode = "vi", Title = "Bánh Mì Nướng Giòn", Subtitle = "Bánh mì nóng giòn", Description = "Bánh mì nóng giòn, nhân đầy đặn, có cả lựa chọn không cay.", PriceMin = "20k", PriceMax = "45k", Address = "502 Đường Vĩnh Khánh, Phường 8, Quận 4, TP. Hồ Chí Minh", OpenTime = "06:00", CloseTime = "21:30", Rating = 4.3 },
            new ContentModel { PoiId = pois[3].Id, LanguageCode = "vi", Title = "Lẩu Đêm Phố Mới", Subtitle = "Lẩu nhóm bạn", Description = "Quán lẩu mở tối, không gian thoáng và phù hợp nhóm bạn tụ họp.", PriceMin = "139k", PriceMax = "329k", Address = "450 Đường Vĩnh Khánh, Phường 8, Quận 4, TP. Hồ Chí Minh", OpenTime = "16:30", CloseTime = "23:30", Rating = 4.5 },
            new ContentModel { PoiId = pois[4].Id, LanguageCode = "vi", Title = "Trà Chanh Chill Spot", Subtitle = "Đồ uống mát", Description = "Điểm hẹn đồ uống mát, menu đa dạng và có khu ngồi ngoài trời.", PriceMin = "25k", PriceMax = "69k", Address = "465 Đường Vĩnh Khánh, Phường 8, Quận 4, TP. Hồ Chí Minh", OpenTime = "09:00", CloseTime = "23:00", Rating = 4.2 },
            new ContentModel { PoiId = pois[5].Id, LanguageCode = "vi", Title = "Ốc Đêm 106", Subtitle = "Hải sản đêm", Description = "Quán ốc đậm vị với nhiều món sốt, đông khách vào khung giờ tối.", PriceMin = "60k", PriceMax = "220k", Address = "520 Đường Vĩnh Khánh, Phường 8, Quận 4, TP. Hồ Chí Minh", OpenTime = "16:00", CloseTime = "23:45", Rating = 4.6 },
            new ContentModel { PoiId = pois[6].Id, LanguageCode = "vi", Title = "Cơm Tấm Nhà Gỗ", Subtitle = "Cơm tấm sườn", Description = "Cơm tấm sườn nướng thơm, phần ăn đầy đủ và lên món nhanh.", PriceMin = "40k", PriceMax = "89k", Address = "478 Đường Vĩnh Khánh, Phường 8, Quận 4, TP. Hồ Chí Minh", OpenTime = "07:00", CloseTime = "21:00", Rating = 4.4 },
            new ContentModel { PoiId = pois[7].Id, LanguageCode = "vi", Title = "Xiên Nướng Vườn Nhỏ", Subtitle = "Ăn vặt tối", Description = "Xiên nướng nóng hổi, giá mềm, thích hợp ăn nhẹ buổi chiều tối.", PriceMin = "15k", PriceMax = "60k", Address = "430 Đường Vĩnh Khánh, Phường 8, Quận 4, TP. Hồ Chí Minh", OpenTime = "15:00", CloseTime = "23:00", Rating = 4.1 },
            new ContentModel { PoiId = pois[8].Id, LanguageCode = "vi", Title = "Nước Ép Tươi 79", Subtitle = "Sinh tố & nước ép", Description = "Nước ép trái cây tươi và sinh tố theo mùa, vị thanh mát dễ uống.", PriceMin = "25k", PriceMax = "65k", Address = "540 Đường Vĩnh Khánh, Phường 8, Quận 4, TP. Hồ Chí Minh", OpenTime = "08:00", CloseTime = "22:00", Rating = 4.3 },
            new ContentModel { PoiId = pois[9].Id, LanguageCode = "vi", Title = "Quán Nhà Lá", Subtitle = "Món Việt gia đình", Description = "Quán ăn gia đình với món Việt quen thuộc, phù hợp cả trưa và tối.", PriceMin = "49k", PriceMax = "179k", Address = "555 Đường Vĩnh Khánh, Phường 8, Quận 4, TP. Hồ Chí Minh", OpenTime = "10:00", CloseTime = "22:30", Rating = 4.5 }
        };

        foreach (var content in contents)
        {
            content.NormalizeCompositeFields();
        }

        db.PointContents.AddRange(contents);
        db.SaveChanges();
        System.Console.WriteLine("✅ Reset xong và seeded 10 POIs quanh 10.7731577,106.582758");
    }
    catch (Exception ex)
    {
        System.Console.WriteLine($"❌ DB Error: {ex.Message}");
    }
}

app.Run();