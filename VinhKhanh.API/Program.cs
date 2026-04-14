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
        // NOTE: Không seed POI/Content mẫu nữa. Nguồn dữ liệu POI phải đến từ Admin web.
        System.Console.WriteLine("✅ Database ensured (no seed).");
    }
    catch (Exception ex)
    {
        System.Console.WriteLine($"❌ DB Error: {ex.Message}");
    }
}

app.Run();