using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;

var builder = WebApplication.CreateBuilder(args);

// Lấy chuỗi kết nối từ file appsettings.json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Đăng ký kết nối SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddControllers();

// Cấu hình Swagger để kiểm tra API
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// THÊM DÒNG NÀY ĐỂ MỞ CỬA CHO MÁY ẢO
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5291); // Nghe cổng http 5291
    options.ListenAnyIP(7174, listenOptions => listenOptions.UseHttps()); // Nghe cổng https 7174
});

var app = builder.Build();

// Kích hoạt Swagger trong môi trường Development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();