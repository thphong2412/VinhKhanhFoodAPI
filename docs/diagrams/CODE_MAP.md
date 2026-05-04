# CODE MAP — Vị trí phương thức chính của 21 Sequence Diagrams

> Mỗi SD liệt kê **phương thức quan trọng nhất**, file nào, dòng bao nhiêu, và giải thích ngắn gọn.
> Dùng để trả lời thầy khi hỏi: *"Code phương thức này nằm ở đâu? Giải thích cho tôi."*

---

## SD-01 — Đăng ký Owner & Đăng nhập

### 1. `RegisterOwner()` — API xử lý đăng ký owner
- **File:** `VinhKhanh.API/Controllers/AuthController.cs` → **dòng 32**
- **Route:** `POST admin/auth/register-owner`
- **Giải thích:**
  - Nhận request gồm `Email, Password, ShopName, ShopAddress, Cccd`.
  - Normalize email (trim + lowercase) để tránh trùng do viết hoa/khoảng trắng.
  - Hash password bằng SHA256 + static salt (`ComputeHash`).
  - Mã hoá CCCD bằng `EncryptionService.Protect` (AES symmetric).
  - Kiểm tra email đã tồn tại → trả `409 Conflict`.
  - INSERT vào bảng `Users` (role=owner, isVerified=false) + `OwnerRegistrations` (status=pending).
  - Gọi SignalR `SendAsync("OwnerRegistrationSubmitted")` để admin nhận realtime.
  - Trả `201 Created {userId, registrationId}`.

### 2. `Login()` — API xử lý đăng nhập
- **File:** `VinhKhanh.API/Controllers/AuthController.cs` → **dòng 99**
- **Route:** `POST admin/auth/login`
- **Giải thích:**
  - Nhận `{Email, Password}`.
  - Normalize email, hash password, so sánh với DB.
  - Kiểm tra `isVerified == true` (đã được admin duyệt).
  - Nếu OK → Generate JWT (HMAC-SHA256, 180 phút, chứa userId + role + permissions).
  - Trả `200 {userId, email, role, isVerified, accessToken}`.

### 3. `OnPostAsync()` — Owner Portal gửi form đăng ký
- **File:** `VinhKhanh.OwnerPortal/Pages/Register.cshtml.cs` → **dòng 29**
- **Giải thích:** Thu thập form → gọi API `register-owner` → redirect `/RegisterSuccess`.

### 4. `OnPostAsync()` — Owner Portal đăng nhập
- **File:** `VinhKhanh.OwnerPortal/Pages/Login.cshtml.cs` → **dòng 26**
- **Giải thích:** Thu thập form → gọi API `login` → set cookie → redirect `/OwnerDashboard`.

---

## SD-02 — Geofence & Tự động phát Narration

### 1. `ProcessLocation()` — Engine xử lý vị trí GPS
- **File:** `VinhKhanh/Services/GeofenceEngine.cs` → **dòng 36**
- **Giải thích:**
  - Nhận (lat, lng) từ LocationPollingService.
  - Duyệt tất cả POI, tính khoảng cách Haversine.
  - Nếu user nằm trong bán kính POI + chưa cooldown → fire event `PoiTriggered`.
  - Có debounce (stability check) để tránh GPS jitter.

### 2. `ExecuteAsync()` — Background loop GPS
- **File:** `VinhKhanh/Services/LocationPollingService.cs` → **dòng 66**
- **Giải thích:**
  - Loop mỗi 5-10 giây: `GetLocationAsync()` → lấy GPS.
  - Gọi `_geofenceEngine.ProcessLocation(lat, lng)`.
  - Gọi `TrackAnonymousRouteAsync()` → POST api/analytics {poi_heartbeat}.

### 3. `PoiTriggered` event handler
- **File:** `VinhKhanh/Services/GeofenceEngine.cs` → **dòng 28** (event declaration), **dòng 152** (fire)
- **Giải thích:** Khi POI triggered → `MapPage.Lifecycle.cs` subscribe event này → gọi `ShowPoiDetail(poi)` + `PlayNarration()`.

---

## SD-03 — Quét mã QR (Mobile App)

### 1. `OnBarcodeDetected()` — Camera decode QR
- **File:** `VinhKhanh/Pages/ScanPage.xaml.cs` → **dòng 107**
- **Giải thích:**
  - Camera ZXing detect barcode → decode ra payload (chứa poiId).
  - Kiểm tra `_isSpeaking` tránh xử lý trùng.
  - Navigate đến MapPage với poiId → MapPage tìm POI → `ShowPoiDetail(poi)`.

### 2. `ShowPoiDetail()` — Hiện chi tiết POI
- **File:** `VinhKhanh/Pages/MapPage.xaml.cs` → **dòng 853**
- **Giải thích:**
  - Nhận `PoiModel poi` → load content local trước.
  - Gọi API: `GET content/by-poi/{poiId}`, `GET audio/by-poi/{poiId}`, `GET poi-reviews/{poiId}`.
  - Track event `poi_detail_open`.
  - Auto phát TTS `PlayNarration(description)`.

---

## SD-04 — Quét QR Web Public (/qr/{id})

### 1. `ScanAndRedirect()` — QR redirect
- **File:** `VinhKhanh.API/Controllers/PublicQrController.cs` → **dòng 78**
- **Route:** `GET /qr/{poiId}`
- **Giải thích:**
  - Nhận poiId từ URL → lookup POI trong DB.
  - Log TraceLog (qr_scan, web_public).
  - 302 Redirect → `/listen/{poiId}?lang=vi`.

### 2. `Listen()` — Trang nghe web
- **File:** `VinhKhanh.API/Controllers/PublicQrController.cs` → **dòng 121**
- **Route:** `GET /listen/{poiId}`
- **Giải thích:**
  - Load POI + Content từ DB.
  - Kiểm tra TTS cache (`/tts-cache/{lang}/poi_{id}.mp3`).
  - Nếu chưa có → gTTS generate MP3 → save cache.
  - Trả HTML Listen Page (info + audio player auto-play).
  - JS gọi `POST /qr/track {listen_start}` để track analytics.

---

## SD-05 — Admin CRUD POI + Publish/Unpublish

### 1. `PostPoi()` — Tạo POI mới
- **File:** `VinhKhanh.API/Controllers/PoiController.cs` → **dòng 323**
- **Route:** `POST api/poi`
- **Giải thích:** Validate + INSERT `PointsOfInterest` + auto generate QR Code + SignalR broadcast `PoiAdded`.

### 2. `UpdatePoi()` — Sửa POI
- **File:** `VinhKhanh.API/Controllers/PoiController.cs` → **dòng 277**
- **Route:** `PUT api/poi/{id}`
- **Giải thích:** UPDATE các trường + SignalR broadcast `PoiUpdated` + `RequestFullPoiSync`.

### 3. `DeletePoi()` — Xóa POI
- **File:** `VinhKhanh.API/Controllers/PoiController.cs` → **dòng 387**
- **Route:** `DELETE api/poi/{id}`
- **Giải thích:** DELETE POI + cascade Audio + Content + SignalR broadcast `PoiDeleted`.

### 4. `LoadAll()` — Lấy danh sách POI
- **File:** `VinhKhanh.API/Controllers/PoiController.cs` → **dòng 30**
- **Route:** `GET api/poi/load-all`
- **Giải thích:** SELECT tất cả POI (có filter `IsPublished` nếu không có header `X-Include-Unpublished`), join Content để lấy tên theo ngôn ngữ.

---

## SD-06 — Owner Submit Update/Delete → Admin Duyệt

### 1. `SubmitUpdate()` — Owner gửi yêu cầu sửa
- **File:** `VinhKhanh.API/Controllers/PoiRegistrationController.cs` → **dòng 579**
- **Route:** `POST api/poiregistration/submit-update/{poiId}`
- **Giải thích:** Tạo `PoiRegistration` (type=update, pending), gắn poiId gốc.

### 2. `SubmitDelete()` — Owner gửi yêu cầu xóa
- **File:** `VinhKhanh.API/Controllers/PoiRegistrationController.cs` → **dòng 601**
- **Route:** `POST api/poiregistration/submit-delete/{poiId}`
- **Giải thích:** Tạo `PoiRegistration` (type=delete, pending).

### 3. `ApprovePoi()` — Admin duyệt
- **File:** `VinhKhanh.API/Controllers/PoiRegistrationController.cs` → **dòng 177**
- **Route:** `POST api/poiregistration/{id}/approve`
- **Giải thích:**
  - Nếu type=create → INSERT PointsOfInterest + PointContents.
  - Nếu type=update → UPDATE POI thật + UPSERT Content.
  - Nếu type=delete → DELETE POI + Content + Audio.
  - UPDATE status=approved.

### 4. `RejectPoi()` — Admin từ chối
- **File:** `VinhKhanh.API/Controllers/PoiRegistrationController.cs` → **dòng 632**
- **Route:** `POST api/poiregistration/{id}/reject`

---

## SD-07 — Content CRUD + AI Auto-Translate + Rebuild

### 1. `Create()` — Tạo content
- **File:** `VinhKhanh.API/Controllers/ContentController.cs` → **dòng 48**
- **Route:** `POST api/content`
- **Giải thích:** INSERT PointContents (poiId, lang, title, description, address, phone...) + SignalR broadcast.

### 2. `TranslateContent()` — AI dịch tự động
- **File:** `VinhKhanh.API/Controllers/AiController.cs` → **dòng 31**
- **Route:** `POST api/ai/translate-content`
- **Giải thích:**
  - Nhận `{poiId, targetLanguageCode}`.
  - SELECT content tiếng Việt (source).
  - Gọi LLM API (OpenAI/Groq) với prompt dịch.
  - UPSERT PointContents cho ngôn ngữ đích.
  - SignalR broadcast ContentCreated.

### 3. `RebuildAllTranslations()` — Batch dịch tất cả
- **File:** `VinhKhanh.API/Controllers/ContentController.cs` → **dòng 74**
- **Route:** `POST api/content/rebuild-all-translations`
- **Giải thích:** Loop qua tất cả POI có content VI → gọi AI translate cho từng ngôn ngữ → UPSERT → SignalR `RequestFullPoiSync`.

---

## SD-08 — Analytics Dashboard + User đang Online

### 1. `PostTrace()` — Nhận và lưu trace event
- **File:** `VinhKhanh.API/Controllers/AnalyticsController.cs` → **dòng 28**
- **Route:** `POST api/analytics`
- **Giải thích:**
  - Nhận `{event, poiId, deviceId, extraJson}`.
  - Tạo fingerprint = MD5(event + poiId + deviceId).
  - Kiểm tra MemoryCache → trùng (< 3 phút) → bỏ qua.
  - Mới → SET cache TTL=3min → INSERT TraceLog → SignalR `TraceLogged`.

### 2. `GetSummary()` — Tính user đang online + KPI
- **File:** `VinhKhanh.API/Controllers/AnalyticsController.cs` → **dòng 1112**
- **Route:** `GET api/analytics/summary`
- **Giải thích:**
  - SELECT TraceLogs WHERE `TimestampUtc >= (now - 90s)`.
  - Lọc event online: `qr_scan, listen_start, poi_heartbeat, web_session_active`.
  - Lọc source mobile: `mobile_app, mobile_scan, app_audio_queue`.
  - `Distinct(deviceId)` → đếm = **OnlineUsers**.
  - Trả DTO gồm: onlineUsers, totalVisitors, totalEvents, avgDuration...

### 3. `GetQrScanCounts()` — Đếm QR scan theo POI
- **File:** `VinhKhanh.API/Controllers/AnalyticsController.cs` → **dòng 821**
- **Route:** `GET api/analytics/qr-scan-counts`
- **Giải thích:** COUNT TraceLogs WHERE event="qr_scan" GROUP BY PoiId → trả `QrScanCountDto[]`.

### 4. `GetAppListenMetrics()` — Thống kê TTS + Audio
- **File:** `VinhKhanh.API/Controllers/AnalyticsController.cs` → **dòng 883**
- **Route:** `GET api/analytics/app-listen-metrics`

---

## SD-09 — Sinh & Cache TTS Audio

### 1. `GenerateTts()` — Sinh TTS từ text
- **File:** `VinhKhanh.API/Controllers/AudioController.cs` → **dòng 49**
- **Route:** `POST api/audio/tts`
- **Giải thích:**
  - Nhận `{text, lang, poiId}`.
  - Kiểm tra cache `/tts-cache/{lang}/poi_{id}.mp3`.
  - Chưa có → gTTS sinh MP3 (Google TTS via HTTP) → save file + UPSERT AudioFiles DB.
  - SignalR `AudioUploaded`.
  - Trả URL file MP3.

### 2. `GenerateAllLanguageTts()` — Batch sinh TTS 10 ngôn ngữ
- **File:** `VinhKhanh.API/Controllers/AudioController.cs` → **dòng 124**
- **Route:** `POST api/audio/tts/generate-all/{poiId}`
- **Giải thích:** Loop 10 ngôn ngữ → lấy content description → gTTS → save cache.

---

## SD-10 — Rebuild & Warmup Localization

### 1. `PrepareHotset()` — Dịch sẵn batch POI
- **File:** `VinhKhanh.API/Controllers/LocalizationController.cs` → **dòng 40**
- **Route:** `POST api/localizations/prepare-hotset`
- **Giải thích:** Chọn ngôn ngữ + list POI → loop AI translate → UPSERT PointContents.

### 2. `Warmup()` — Nạp cache bản dịch
- **File:** `VinhKhanh.API/Controllers/LocalizationController.cs` → **dòng 148**
- **Route:** `POST api/localizations/warmup`
- **Giải thích:** SELECT tất cả content cho 1 lang → load vào MemoryCache.

### 3. `OnDemand()` — Dịch 1 text cụ thể
- **File:** `VinhKhanh.API/Controllers/LocalizationController.cs` → **dòng 96**
- **Route:** `POST api/localizations/on-demand`
- **Giải thích:** Kiểm cache → có: trả ngay. Không: AI translate → SET cache → trả.

---

## SD-11 — Anti-Spam Trace Analytics

### 1. `PostTrace()` — (cùng SD-08, phần anti-spam)
- **File:** `VinhKhanh.API/Controllers/AnalyticsController.cs` → **dòng 28**
- **Giải thích chi tiết anti-spam:**
  - Tạo fingerprint = `MD5(event + poiId + deviceId)`.
  - `_cache.TryGetValue(fingerprint)` → HIT = trả `{ignored: true, reason: "exact_duplicate"}`.
  - MISS → `_cache.Set(fingerprint, true, TimeSpan.FromMinutes(3))` → INSERT TraceLog → SignalR.
  - Đây là cơ chế chống duplicate request trong 3 phút.

---

## SD-12 — Đánh giá POI từ Du khách (Reviews)

### 1. `Create()` — Du khách gửi review
- **File:** `VinhKhanh.API/Controllers/PoiReviewsController.cs` → **dòng 84**
- **Route:** `POST api/poi-reviews`
- **Giải thích:** INSERT PoiReviews (poiId, rating, comment, IsHidden=false).

### 2. `GetByPoi()` — Lấy reviews (app)
- **File:** `VinhKhanh.API/Controllers/PoiReviewsController.cs` → **dòng 50**
- **Route:** `GET api/poi-reviews/{poiId}`
- **Giải thích:** SELECT WHERE IsHidden=false → chỉ hiện review chưa bị ẩn.

### 3. `ToggleHidden()` — Admin ẩn/hiện review
- **File:** `VinhKhanh.API/Controllers/PoiReviewsController.cs` → **dòng 104**
- **Route:** `POST api/poi-reviews/{reviewId}/toggle-hidden`

---

## SD-13 — Admin Quản lý User & Duyệt Owner

### 1. `Approve()` — Duyệt owner
- **File:** `VinhKhanh.API/Controllers/OwnerAdminController.cs` → **dòng 94**
- **Route:** `POST admin/registrations/{id}/approve`
- **Giải thích:** UPDATE `OwnerRegistrations.status = approved` + UPDATE `Users.isVerified = true`.

### 2. `Reject()` — Từ chối owner
- **File:** `VinhKhanh.API/Controllers/OwnerAdminController.cs` → **dòng 119**
- **Route:** `POST admin/registrations/{id}/reject`

### 3. `ToggleVerified()` — Toggle trạng thái user
- **File:** `VinhKhanh.API/Controllers/AdminUsersController.cs` → **dòng 139**
- **Route:** `POST admin/users/{id}/toggle-verified`
- **Giải thích:** UPDATE `isVerified = !isVerified` → khoá/mở khóa user.

### 4. `List()` — Lấy danh sách users
- **File:** `VinhKhanh.API/Controllers/AdminUsersController.cs` → **dòng 23**
- **Route:** `GET admin/users`

---

## SD-14 — Nghe Audio: TTS + MP3 picker → Mini Player

### 1. `OnNgheNgayClicked()` — Bấm "Nghe ngay" (TTS)
- **File:** `VinhKhanh/Pages/MapPage.NgheAudio.cs` → **dòng 27**
- **Giải thích:**
  - Lấy content description theo ngôn ngữ hiện tại.
  - Gọi `ShowMiniPlayerArmedAsync()` → popup mở nhưng **chưa phát**.
  - User bấm Play → `AudioQueue.Enqueue(ttsItem)` → API sinh TTS → `PlayAsync()`.

### 2. `OnAudioListClicked()` — Bấm "Audio" (MP3 list)
- **File:** `VinhKhanh/Pages/MapPage.NgheAudio.cs` → **dòng 55**
- **Giải thích:**
  - Gọi `GET api/audio/by-poi/{poiId}`.
  - Lọc MP3 files theo ngôn ngữ.
  - Hiện `AudioListPopup` → user chọn file → Enqueue → PlayAsync.

### 3. `GetByPoi()` — API trả danh sách audio
- **File:** `VinhKhanh.API/Controllers/AudioController.cs` → **dòng 233**
- **Route:** `GET api/audio/by-poi/{poiId}`

---

## SD-15 — Owner Upload Ảnh & Submit Tạo POI

### 1. `UploadImage()` — Upload ảnh
- **File:** `VinhKhanh.API/Controllers/PoiRegistrationController.cs` → **dòng 30**
- **Route:** `POST api/poiregistration/upload-image`
- **Giải thích:** Validate image (type, size) → save file → trả imageUrl.

### 2. `SubmitPoi()` — Submit POI mới
- **File:** `VinhKhanh.API/Controllers/PoiRegistrationController.cs` → **dòng 64**
- **Route:** `POST api/poiregistration/submit`
- **Giải thích:** Validate data → INSERT PoiRegistrations (type=create, status=pending) → chờ admin duyệt.

---

## SD-16 — App khởi động → Load bản đồ POI

### 1. `OnAppearing()` → `InitializeOnAppearingAsync()`
- **File:** `VinhKhanh/Pages/MapPage.Lifecycle.cs` → **dòng 110** (OnAppearing), **dòng 122** (Initialize)
- **Giải thích:**
  - Kết nối SignalR (`ConnectForDeviceAsync`).
  - Load POI từ SQLite local (`GetPoisAsync()`).
  - Nếu DB rỗng → `GET api/poi/load-all` → save vào SQLite.
  - `AddPoisToMap()` → render pins lên bản đồ.
  - Background: `RenderHighlightsAsync`, `DisplayAllPois`, `GeofenceEngine.UpdatePois`.
  - `EnsureTrackingStartedAsync` → khởi động GPS polling.
  - `CenterMapOnUserFirstAsync` → center map vào vị trí user.

---

## SD-17 — Bấm pin → Xem chi tiết POI + Đổi ngôn ngữ

### 1. `ShowPoiDetail()` — Hiện chi tiết POI
- **File:** `VinhKhanh/Pages/MapPage.xaml.cs` → **dòng 853**
- **Giải thích:**
  - Load content local trước (cache-first).
  - Hiện PoiDetailPanel ngay lập tức.
  - Background: GET content + audio + reviews từ API → hydrate UI đầy đủ.
  - Track event `poi_detail_open`.

### 2. Pin click handler
- **File:** `VinhKhanh/Pages/MapPage.xaml.cs` → **dòng 1175**
- **Giải thích:** Khi bấm pin → `_selectedPoi = poi` → `ShowPoiDetail(poi, true)`.

---

## SD-18 — Admin Duyệt POI Submission

### 1. `ApprovePoi()` — (cùng SD-06)
- **File:** `VinhKhanh.API/Controllers/PoiRegistrationController.cs` → **dòng 177**
- **Giải thích:**
  - **create:** INSERT PointsOfInterest (tạo POI thật) + INSERT PointContents + generate QR.
  - **update:** UPDATE POI thật + UPSERT Content.
  - **delete:** DELETE Content + Audio + POI.
  - SET status=approved.

### 2. `GetPendingRegistrations()` — Lấy danh sách pending
- **File:** `VinhKhanh.API/Controllers/PoiRegistrationController.cs` → **dòng 113**
- **Route:** `GET api/poiregistration/pending`

---

## SD-19 — Realtime Sync SignalR

### 1. `SyncHub` — Server hub
- **File:** `VinhKhanh.API/Hubs/SyncHub.cs` → **dòng 5**
- **Giải thích:** Hub nhận kết nối → broadcast events: `PoiAdded`, `PoiUpdated`, `PoiDeleted`, `ContentCreated`, `AudioUploaded`, `TraceLogged`, `RequestFullPoiSync`.

### 2. `SignalRSyncService` — Mobile client hub
- **File:** `VinhKhanh/Services/SignalRSyncService.cs` → **dòng 15**
- **Giải thích:** Kết nối SignalR từ mobile → subscribe events → forward tới `RealtimeSyncManager`.

### 3. `RealtimeSyncManager` — Xử lý sync local DB
- **File:** `VinhKhanh/Services/RealtimeSyncManager.cs` → **dòng 17**
- **Giải thích:** Nhận event từ SignalRSyncService → Insert/Update/Delete POI trong SQLite → fire events cho MapPage.

### 4. MapPage handlers
- **File:** `VinhKhanh/Pages/MapPage.Realtime.cs` → **dòng 31** (`HandleRealtimePoiChanged`), **dòng 43** (`HandleRealtimeContentChanged`), **dòng 117** (`HandleRealtimeFullSyncRequested`)
- **Giải thích:** Lắng nghe events → schedule UI refresh (pins, highlights, selected POI detail) với cooldown tránh spam.

---

## SD-20 — GPS Tracking → Admin Route Map

### 1. `TrackAnonymousRouteAsync()` — Mobile gửi GPS
- **File:** `VinhKhanh/Services/LocationPollingService.cs` → **dòng 178**
- **Giải thích:** Mỗi lần GPS update → POST api/analytics `{event: poi_heartbeat, lat, lng, deviceId}` → INSERT TraceLog.

### 2. `GetAnonymousRoutes()` — API trả route data
- **File:** `VinhKhanh.API/Controllers/AnalyticsController.cs` → **dòng 1252**
- **Route:** `GET api/analytics/routes`
- **Giải thích:**
  - SELECT TraceLogs có lat/lng trong N giờ gần nhất.
  - Filter events GPS: `poi_enter, poi_click, poi_detail_open, qr_scan, tts_play, audio_play`.
  - GROUP BY deviceId → sort by timestamp → trả polyline per user.
  - Admin Leaflet render: POI markers + polylines từng du khách (ẩn danh).

---

## SD-21 — Lưu / Chia sẻ / Dẫn đường POI

### 1. `OnSaveClicked()` — Lưu POI
- **File:** `VinhKhanh/Pages/MapPage.PoiActions.cs` → **dòng 896**
- **Giải thích:** Toggle `IsSaved = !IsSaved` → `_dbService.SavePoiAsync(target)` (SQLite local) → cập nhật icon nút.

### 2. `OnShareClicked()` — Chia sẻ POI
- **File:** `VinhKhanh/Pages/MapPage.PoiActions.cs` → **dòng 879**
- **Giải thích:** Tạo text chia sẻ (tên + mô tả + link) → `Share.RequestAsync()` → popup chia sẻ hệ thống (Zalo, Messenger...).

### 3. `OnGetDirectionsClicked()` — Dẫn đường
- **File:** `VinhKhanh/Pages/MapPage.PoiActions.cs` → **dòng 761**
- **Giải thích:**
  - Lấy toạ độ POI (lat, lng).
  - Track event `navigation_start`.
  - Android: mở Google Maps `google.navigation:q={lat},{lng}`.
  - iOS: mở Apple Maps `maps://?daddr={lat},{lng}`.
  - Fallback: mở Web Google Maps.
  - Gán `_pendingNavigationPoiId` → khi GPS detect tới nơi → track `navigation_arrived`.
