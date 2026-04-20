# 🍜 Phố Ẩm Thực Vĩnh Khánh - Hệ Thống Thuyết Minh Đa Ngôn Ngữ

Hệ thống VinhKhanh là nền tảng thuyết minh địa điểm ẩm thực theo vị trí GPS và mã QR dành cho du khách tham quan **Phố Vĩnh Khánh, Quận 4, TP.HCM**. Ứng dụng tự động phát bản thuyết minh âm thanh khi du khách đi vào vùng bán kính của điểm đến, hỗ trợ nhiều ngôn ngữ thông qua AI dịch tự động và pipeline TTS đa tầng. Hệ thống bao gồm đầy đủ **Mobile App (.NET MAUI)**, **Backend API (ASP.NET Core)**, **Admin Portal (ASP.NET MVC)** và **Owner Portal (Razor Pages)**.

## ✨ Tính Năng Nổi Bật (Features)

1. **🗺️ Bản Đồ Số & Định Vị Geofence (Interactive Map + Geofencing)**
   - Hiển thị bản đồ trực quan tích hợp Mapbox cho Mobile App (.NET MAUI Maps).
   - Định vị người dùng theo thời gian thực bằng GPS polling mỗi 5 giây.
   - Tự động phát hiện khi du khách đi vào bán kính POI (GeofenceEngine) và hiển thị panel thông tin quán ăn.
   - Ghi lại sự kiện `poi_enter` vào hệ thống TraceLogs phục vụ phân tích hành vi.

2. **🔊 Thuyết Minh Tự Động Đa Ngôn Ngữ & AI (Multilingual Narration + AI Translation)**
   - Hỗ trợ đa ngôn ngữ thông qua pipeline dịch tự động tích hợp **Google Gemini AI**.
   - Audio pipeline đa tầng dự phòng (3 Tier Fallback): Pre-generated MP3 → Edge TTS on-demand → Native OS TTS.
   - Admin chỉ cần nhập nội dung Tiếng Việt hoặc Tiếng Anh, hệ thống tự localize theo ngôn ngữ du khách chọn.
   - Kiểm duyệt nội dung nhạy cảm qua keyword policy trước khi sinh bản dịch.

3. **📱 Hỗ Trợ Ngoại Tuyến (Offline Support)**
   - Tải gói dữ liệu Map Pack và danh sách POI xuống thiết bị trước chuyến đi.
   - Offline Manifest System đảm bảo app hoạt động bình thường ngay cả khi mất kết nối.
   - Hiển thị thông báo và fallback UI khi mạng yếu hoặc mất kết nối.

4. **📷 Quét Mã QR (QR Scanner - Mobile & Public Web)**
   - Mobile App tích hợp ZXing QR Scanner, hỗ trợ parse deeplink, URL, `POI:{id}` và số thuần.
   - Trang web công khai `/qr/{id}` cho du khách quét bằng trình duyệt mà không cần cài app.
   - Admin CMS tự động sinh và quản lý mã QR chuẩn cho từng điểm đến (POI).
   - Mọi lượt quét đều được ghi lại `qr_scan` analytics event.

5. **⚙️ Quy Trình Kiểm Duyệt Nội Dung (Owner Moderation Workflow)**
   - Chủ quán (Owner) đăng ký tài khoản → Admin duyệt → được vào hệ thống.
   - Owner gửi yêu cầu tạo/sửa/xóa POI, cập nhật audio và bản dịch qua Owner Portal.
   - Mọi thay đổi chờ Admin duyệt trước khi áp dụng, đảm bảo chất lượng nội dung.
   - POI tự động tạm ẩn (unpublish) trong lúc đang chờ duyệt cập nhật.

6. **📊 Phân Tích Dữ Liệu & Đồng Bộ Realtime (Analytics & SignalR Sync)**
   - Dashboard Admin theo dõi: Top POIs, Heatmap, Active Users, Engagement, QR Metrics.
   - Đồng bộ dữ liệu thời gian thực tới các Client qua **SignalR Hub** (`/sync`).
   - Chi tiết sự kiện: `poi_enter`, `qr_scan`, `tts_play`, `audio_complete`...
   - Health endpoint `/health` hỗ trợ giám sát readiness hệ thống.

## 🛠️ Công Nghệ Sử Dụng (Tech Stack)

### Backend API
- **Framework**: ASP.NET Core (.NET 9) - RESTful Web API
- **Database**: SQLite (Phát triển) / SQL Server (Production) thông qua **Entity Framework Core**
- **Realtime**: SignalR Hub (`/sync`) đẩy sự kiện thay đổi POI đến App
- **AI & TTS**: Google Gemini API (dịch thuật), Google TTS API (sinh audio)
- **Auth**: JWT Authentication + Cookie-based Session + API Key Middleware
- **Kiến trúc**: Dependency Injection, Repository Pattern, Middleware Pipeline

### Mobile App
- **Framework**: .NET MAUI (Android & iOS)
- **Bản đồ**: MAUI Maps + Mapbox SDK
- **Quét QR**: ZXing.Net.MAUI
- **TTS**: Native TTS + EdgeTTS + Cloud TTS (3-tier fallback)
- **Lưu trữ ngoại tuyến**: Offline Map Pack + Local Content Cache

### Admin Portal & Owner Portal
- **Admin Portal**: ASP.NET MVC - gọi API qua `HttpClient` + API Key
- **Owner Portal**: Razor Pages - xác thực qua cookie `owner_userid`

## 🚀 Hướng Dẫn Chạy Dự Án (Getting Started)

### Yêu Cầu Hệ Thống
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- SQL Server Express hoặc SQLite (đã tích hợp sẵn cho môi trường dev)
- Visual Studio 2022+ hoặc JetBrains Rider

### Cài Đặt (Setup)

1. **Cấu hình chuỗi kết nối Database:**
   Mở file `VinhKhanh.API/appsettings.json`, điền thông tin kết nối phù hợp:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=.\\SQLEXPRESS,1433;Database=QuanLyVinhKhanh;Trusted_Connection=True;TrustServerCertificate=True;"
     },
     "Jwt": {
       "Issuer": "VinhKhanh.API",
       "Audience": "VinhKhanh.Clients",
       "Key": "your-super-secret-key-here",
       "AccessTokenMinutes": 180
     },
     "Gemini": {
       "ApiKey": "YOUR_GEMINI_API_KEY",
       "Model": "gemini-1.5-flash"
     }
   }
   ```

2. **Khởi chạy Backend API:**
   ```bash
   cd VinhKhanh.API
   dotnet run
   ```

3. **Khởi chạy Admin Portal:**
   ```bash
   cd VinhKhanh.AdminPortal
   dotnet run
   ```

4. **Khởi chạy Owner Portal:**
   ```bash
   cd VinhKhanh.OwnerPortal
   dotnet run
   ```

5. **Truy Cập Hệ Thống:**
   - Backend API Swagger: `http://localhost:5000/swagger`
   - Admin Portal: `http://localhost:5001`
   - Owner Portal: `http://localhost:5002`
   - Public QR Listen: `http://localhost:5000/qr/{poiId}`
   - Health Check: `http://localhost:5000/health`

### 💡 Lưu Ý Cấu Hình API Key
Hệ thống sử dụng API Key Middleware để bảo vệ các endpoint nội bộ. Mặc định dev key là `admin123` (cấu hình trong `appsettings.json` → `"ApiKey"`). **Bắt buộc thay đổi** trước khi triển khai Production.

## 📁 Cấu Trúc Thư Mục Hệ Thống (Folder Structure)

```
VinhKhanhFood/
├── VinhKhanh.API/                  # Backend API chính (ASP.NET Core)
│   ├── Controllers/                # 19 Controller xử lý toàn bộ REST Endpoints
│   │   ├── PoiController.cs        # CRUD Điểm đến (POI) công khai
│   │   ├── AdminPoisController.cs  # Quản lý POI dành cho Admin
│   │   ├── AuthController.cs       # Đăng nhập, JWT, phân quyền
│   │   ├── AudioController.cs      # Upload, sinh TTS, phát audio
│   │   ├── AiController.cs         # Pipeline dịch thuật Gemini AI
│   │   ├── AnalyticsController.cs  # Dashboard, heatmap, thống kê
│   │   ├── LocalizationController.cs # Bản địa hóa nội dung on-demand
│   │   ├── PublicQrController.cs   # Trang web QR công khai /qr/{id}
│   │   ├── PoiRegistrationController.cs # Workflow duyệt Owner POI
│   │   └── ...                     # Và các Controller khác
│   ├── Hubs/
│   │   └── SyncHub.cs              # SignalR Hub đồng bộ realtime
│   ├── Services/
│   │   ├── QrCodeService.cs        # Sinh và quản lý mã QR
│   │   ├── EncryptionService.cs    # Mã hóa dữ liệu nhạy cảm
│   │   └── PoiCleanupService.cs    # Dọn dẹp dữ liệu nền (Background)
│   ├── Data/                       # DbContext & Migration EF Core
│   ├── Program.cs                  # Entry Point và cấu hình Middleware
│   └── appsettings.json            # Biến cấu hình hệ thống
│
├── VinhKhanh.AdminPortal/          # Portal quản trị (ASP.NET MVC)
│   ├── Controllers/                # Controller Admin gọi API nội bộ
│   └── Views/                      # Giao diện Razor quản trị
│
├── VinhKhanh.OwnerPortal/          # Portal chủ quán (Razor Pages)
│   └── Pages/                      # Pages đăng ký, gửi yêu cầu POI
│
├── VinhKhanh.Models/               # POCO Models dùng chung
├── VinhKhanh.Data/                 # EF Core Data Layer dùng chung
├── VinhKhanh.Services/             # Business Logic Services dùng chung
├── VinhKhanh.Shared/               # Utilities, Helpers dùng chung
├── PRD_VinhKhanh_Project_2026.md   # Tài liệu PRD đầy đủ hệ thống
└── VinhKhanh.slnx                  # Solution file
```

## 📌 Các Endpoint API Chính

| Nhóm | Endpoint | Mô tả |
|---|---|---|
| Auth | `POST /admin/auth/login` | Đăng nhập lấy JWT Token |
| POI | `GET /api/poi` | Danh sách điểm đến công khai |
| Admin POI | `POST /admin/pois/{id}/publish` | Bật/Tắt hiển thị POI |
| Owner | `POST /api/poiregistration/submit-update/{id}` | Gửi yêu cầu cập nhật POI |
| Audio | `POST /api/audio/upload` | Upload file âm thanh |
| AI | `POST /api/ai/translate-content` | Dịch nội dung bằng Gemini AI |
| QR | `GET /qr/{poiId}` | Trang web nghe thuyết minh QR công khai |
| Analytics | `GET /api/analytics/top-pois` | Top điểm đến được ghé thăm nhiều nhất |
| Sync | `/sync` (WebSocket) | SignalR Hub đồng bộ dữ liệu realtime |
| Health | `GET /health` | Kiểm tra trạng thái hệ thống |
