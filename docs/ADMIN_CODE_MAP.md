# Admin Web — Bản đồ code chức năng

Tài liệu này liệt kê **mỗi chức năng** trong Admin Web tương ứng với **file & dòng code nào**, để khi thầy hỏi *"chỗ này code ở đâu?"* bạn có thể trả lời ngay.

> Quy ước: tìm comment `[FEATURE: ...]` trong source code — mỗi feature đều được đánh dấu bằng marker này.

---

## Cấu trúc 4 project

| Project | Vai trò |
|---|---|
| `VinhKhanh.API` | REST API + SignalR Hub. Mọi dữ liệu đi qua đây. |
| `VinhKhanh.AdminPortal` | Web admin (MVC). Gọi API. |
| `VinhKhanh.OwnerPortal` | Web owner (Razor Pages). Gọi API. |
| `VinhKhanh` | App MAUI (Android/iOS/Windows). Gọi API. |

Sidebar admin (`Views/Shared/_Layout.cshtml`) gồm 8 menu:

1. Quản lý POI · 2. Bản đồ · 3. Quản lý tài khoản Owner · 4. Chờ duyệt POI của Owner
5. Lịch sử sử dụng · 6. Analytics · 7. Tuyến di chuyển · 8. Quản lý tour

---

## 1. Quản lý POI (`/PoiAdmin`)

| Chức năng | Controller (AdminPortal) | Endpoint API | View |
|---|---|---|---|
| Danh sách POI | `PoiAdminController.Index` | `GET admin/pois/overview` | `Views/PoiAdmin/Index.cshtml` |
| Chi tiết 1 POI | `PoiAdminController.Details` | `GET api/poi/{id}` + `GET api/poi-reviews/{id}` | `Views/PoiAdmin/Details.cshtml` |
| Sửa POI | `PoiAdminController.Edit` (GET/POST) | `PUT api/poi/{id}` | `Views/PoiAdmin/Edit.cshtml` |
| Tạo POI | `PoiAdminController.Create` (GET/POST) | `POST api/poi` | `Views/PoiAdmin/Create.cshtml` |
| Xoá POI | `PoiAdminController.Delete` | `DELETE api/poi/{id}` | modal trong Details.cshtml |
| Duyệt POI (đánh HOT/Approve) | `PoiAdminController.ApprovePoi` | `POST admin/pois/{id}/approve` | nút trong Index/Details |
| **Ẩn/Hiện đánh giá xúc phạm** | `PoiAdminController.ToggleReviewHidden` | `POST api/poi-reviews/{reviewId}/toggle-hidden` | block "Đánh giá từ người dùng" trong `Details.cshtml` |

**Logic ẩn đánh giá**:
- Backend: `VinhKhanh.API/Controllers/PoiReviewsController.cs > ToggleHidden`
- Khi `IsHidden = true`, endpoint `GetByPoi` không trả review đó về app → app không thấy.

---

## 2. Bản đồ admin (`/AdminMap`)

- Controller: `AdminMapController` (Razor view, leaflet/google maps).
- Hiển thị tất cả POI + heatmap GPS user.
- API: `GET api/poi`, `GET api/analytics/heatmap`.

---

## 3. Quản lý tài khoản Owner (`/OwnerAdmin`)

| Chức năng | Controller | Endpoint API |
|---|---|---|
| Danh sách owner | `OwnerAdminController.Index` | `GET admin/owners` |
| Duyệt / từ chối owner | `OwnerAdminController.Approve / Reject` | `POST admin/owners/{id}/approve` |
| Chi tiết 1 owner + POI họ tạo | `OwnerAdminController.Details` | `GET admin/owners/{id}` |

---

## 4. Chờ duyệt POI của Owner (`/AdminPoiRegistrations/Pending`)

- Controller: `AdminPoiRegistrationsController`.
- Endpoint: `GET admin/poi-registrations/pending`, `POST admin/poi-registrations/{id}/approve|reject`.
- Khi approve → POI được tạo thật trong bảng `PointsOfInterest`.

---

## 5. Lịch sử sử dụng (`/TraceLogAdmin`) — log realtime mọi thao tác trên app

| Chức năng | File:Line | Endpoint API |
|---|---|---|
| Trang index + bộ lọc | `TraceLogAdminController.Index` | `GET api/analytics/logs` |
| Realtime push log mới | SignalR `vk:trace-logged` | Hub `SyncHub` (`VinhKhanh.API/Hubs/SyncHub.cs`) |
| View hiển thị tên POI | `Views/TraceLogAdmin/Index.cshtml` cột "Tên POI" | DTO `TraceLogRowDto` |

---

## 6. ⭐ Analytics (`/AnalyticsAdmin`)

Đây là trang **dày đặc nhất**. Mỗi card / mỗi bảng = một feature riêng:

| Card / Bảng | Endpoint API gốc | View block | Comment marker |
|---|---|---|---|
| **Tổng lượt nghe trong app** (TTS + Audio) | `GET api/analytics/app-listen-metrics` | đầu file Index.cshtml | `[FEATURE: app-listen-metrics]` |
| **QR Scan tổng** | sum của `GET api/analytics/qr-scan-counts` | đầu file | `[FEATURE: qr-scan-counts]` |
| **User đang online** ⭐ | `GET api/analytics/summary` → `OnlineUsers` | id `kpiOnlineUsers` | `[FEATURE: User đang online (realtime)]` |
| **Khách hôm nay** | `GET api/analytics/summary` → `VisitorsToday` | id `kpiVisitorsToday` | `[FEATURE: Khách hôm nay]` |
| QR scan theo POI | `GET api/analytics/qr-scan-counts` | section "QR scan theo POI" | |
| Top POI lượt nghe | `GET api/analytics/topPois` | bảng "Top POI" | |
| Engagement TTS/MP3/Detail | `GET api/analytics/engagement` | bảng "Engagement" | |
| Users hoạt động gần đây | `GET api/analytics/active-users` | bảng "Users hoạt động" | |
| Heatmap GPS | `GET api/analytics/heatmap` | iframe Google Maps | |
| Biểu đồ giờ/ngày | `GET api/analytics/timeseries` | Chart.js | |
| Top địa điểm GPS hôm nay | `GET api/analytics/top-visited-today` | bảng "Top hôm nay" | |
| Thời gian nghe TB/POI | `GET api/analytics/avg-duration?poiId=X` | gọi từ JS khi mở row | `[FEATURE: avg-duration]` |

### ⭐ "User đang online" — code ở đâu? (câu hỏi thầy thường gặp)

| Bước | File | Dòng |
|---|---|---|
| Hiển thị card | `VinhKhanh.AdminPortal/Views/AnalyticsAdmin/Index.cshtml` | ~85-93 (`id="kpiOnlineUsers"`) |
| Controller MVC nạp dữ liệu | `VinhKhanh.AdminPortal/Controllers/AnalyticsAdminController.cs` → `Index()` | ~49-65 |
| Endpoint REST | `VinhKhanh.API/Controllers/AnalyticsController.cs` → `GetSummary()` | ~1077-1180 |
| Helper chuẩn hoá deviceId | cùng file → `GetOnlineDeviceKey()` | cuối file |
| Tự refresh 20s + SignalR | `Views/AnalyticsAdmin/Index.cshtml` cuối file `<script>` | section Scripts |

**Cách tính**:
1. Lấy `TraceLogs` trong **90 giây gần nhất**.
2. Lọc các event "online" (qr_scan, listen_start, poi_heartbeat, app_open, location_update…).
3. Distinct deviceId chuẩn hoá → đếm số lượng = số user online.

---

## 7. Tuyến di chuyển user (`/AdminRouteMap`)

- Controller: `AdminRouteMapController`.
- Endpoint: `GET api/analytics/anonymous-routes`.
- Vẽ polyline GPS từng device theo thời gian.

---

## 8. Quản lý tour (`/TourAdmin`)

- Controller: `TourAdminController`.
- Endpoint: `GET api/tour`, `POST api/tour`, …
- View: `Views/TourAdmin/*`.

---

## Cách tìm code khi thầy hỏi

### Cách 1 — search comment marker (nhanh nhất)
Trong VS dùng `Ctrl+Shift+F`:
```
[FEATURE:
```
Mỗi chức năng đều có 1 block `// [FEATURE: tên_chức_năng]` ngay trước handler / view block.

### Cách 2 — tra bảng trên
Đọc bảng phần liên quan, mở file & dòng tương ứng.

### Cách 3 — đi từ URL
Mở DevTools (F12) → Network → bấm thử chức năng → xem URL request:
- `/PoiAdmin/Details/12` → controller `PoiAdminController.Details`
- `api/analytics/summary` → `AnalyticsController.GetSummary`

---

## Mobile app — code chính (đề phòng thầy hỏi cả app)

| Feature | File |
|---|---|
| Map chính + danh sách POI | `VinhKhanh/Pages/MapPage.xaml` + `MapPage.*.cs` (split partial) |
| Mở POI detail card | `MapPage.PoiActions.cs > ShowPoiDetail` |
| Phát Audio MP3 | `MapPage.PoiActions.cs > ShowAudioListForCurrentLanguageAsync` |
| Phát TTS | `MapPage.PoiActions.cs > OnStartNarrationClicked` |
| Mini player popup (seek/pause) | `MapPage.MiniPlayer.cs` |
| Lưu POI (Saved) | `MapPage.PoiActions.cs > OnSaveClicked` |
| Quét QR | `MapPage.UiInteractions.cs > OnScanQrClicked` |
| Đăng đánh giá | `MapPage.PoiActions.cs > OnSubmitReviewClicked` |
| Realtime sync POI từ admin | `Services/RealtimeSyncManager.cs` |
| GPS polling background | `Services/LocationPollingService.cs` |
