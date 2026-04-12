using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình CORS - Cho phép Admin Portal gọi API
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (builder.Environment.IsDevelopment())
{
    // In development use a lightweight file-based Sqlite DB to avoid requiring
    // a local SQL Server instance. This makes it easier to run API + Admin
    // for testing on dev machines.
    var sqliteFile = System.IO.Path.Combine(builder.Environment.ContentRootPath, "vinhkhanh_dev.db");
    builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={sqliteFile}"));
    // Cannot log on builder.Logging directly; log after build if needed
}
else
{
    // Production: use configured SQL Server
    builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
}

builder.Services.AddControllers();

builder.Services.AddAuthorization(options => {
    options.AddPolicy("AdminApi", policy => policy.RequireAssertion(ctx =>
        ctx.Resource is Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext afc &&
        (afc.HttpContext.Request.Headers.TryGetValue("X-API-Key", out var k) && k == builder.Configuration.GetValue<string>("ApiKey"))
    ));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Add API key header support in Swagger UI so you can call POST endpoints from the UI
    c.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "API Key needed to access the endpoints. X-API-Key: dev-key",
        Name = "X-API-Key",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "ApiKeyScheme"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            }, new string[] { }
        }
    });
    // No custom operation filter required here; leave Swagger security definition as-is.
});

builder.WebHost.ConfigureKestrel(options => {
    // Bind to localhost for development to ensure HTTPS dev certificate matches
    options.ListenLocalhost(5291);
    options.ListenLocalhost(7174, listenOptions => listenOptions.UseHttps());
});

var app = builder.Build();

// 2. Kích hoạt CORS (Phải đặt trước MapControllers)
app.UseCors();

app.UseMiddleware<VinhKhanh.API.ApiKeyMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

// Ensure database exists in Development for easier local testing
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VinhKhanh.API.Data.AppDbContext>();
        db.Database.EnsureCreated();

        // Seed some sample POIs for development if database is empty
        try
        {
            if (!db.PointsOfInterest.Any())
            {
                var p1 = new VinhKhanh.Shared.PoiModel
                {
                    Name = "Ốc Oanh 534",
                    Category = "Food",
                    Latitude = 10.7584,
                    Longitude = 106.7058,
                    Radius = 50,
                    Priority = 0,
                    CooldownSeconds = 60,
                    ImageUrl = "",
                    IsSaved = false
                };

                db.PointsOfInterest.Add(p1);
                db.SaveChanges();

                var c1 = new VinhKhanh.Shared.ContentModel
                {
                    PoiId = p1.Id,
                    LanguageCode = "vi",
                    Title = "Ốc Oanh 534",
                    Subtitle = "Ẩm thực",
                    Description = "Quán ốc nổi tiếng khu Vĩnh Khánh",
                    AudioUrl = "",
                    IsTTS = false,
                    PriceRange = "50k-200k",
                    Rating = 4.4,
                    OpeningHours = "10:00 - 22:00",
                    PhoneNumber = "0123456789",
                    Address = "Số 534 Vĩnh Khánh",
                    ShareUrl = ""
                };

                db.PointContents.Add(c1);
                db.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "Seeding sample data failed");
        }
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Failed to ensure database created");
    }
}

app.Run();
