# PRD: Nền tảng Thuyết minh Đa ngôn ngữ Phố Ẩm thực Vĩnh Khánh (Hệ thống hiện tại)

| Trường | Nội dung |
|---|---|
| Tên dự án | VinhKhanh Food Street Platform |
| Phiên bản | 2.0 - System Baseline (Apr 2026) |
| Trạng thái | Draft for implementation alignment |
| Phạm vi hệ thống | Mobile App (.NET MAUI) + Backend API (ASP.NET Core) + Admin Portal (ASP.NET MVC) + Owner Portal (Razor Pages) |
| Địa bàn | Phố Vĩnh Khánh, Quận 4, TP.HCM |
| Ngôn ngữ | vi mặc định + đa ngôn ngữ theo content/audio và luồng localization on-demand |
| Môi trường dữ liệu | SQLite (dev) / SQL Server (prod) qua EF Core |

---

## 1. TL;DR
Hệ thống VinhKhanh là nền tảng thuyết minh địa điểm ẩm thực theo vị trí và QR cho du khách, đồng thời có quy trình vận hành đầy đủ cho admin và chủ quán. Mobile app MAUI tự động phát narration theo geofence, quét QR để nghe nhanh, đồng bộ dữ liệu thời gian thực qua SignalR, hỗ trợ offline map/audio cục bộ. Backend quản lý POI/content/audio/analytics và luồng kiểm duyệt owner. Admin Portal giám sát vận hành, publish/unpublish POI, duyệt yêu cầu từ owner, theo dõi heatmap/engagement. Owner Portal cho phép đăng ký tài khoản, gửi yêu cầu tạo/sửa/xóa POI, quản lý audio và bản dịch theo cơ chế chờ duyệt.

## 2. Goals
### Business Goals
- Chuẩn hóa vận hành nội dung địa điểm trên một nền tảng tập trung (POI, content, audio, analytics).
- Đảm bảo trải nghiệm nghe thuyết minh nhanh tại điểm đến (geofence hoặc QR).
- Kiểm soát chất lượng nội dung qua workflow owner submit -> admin approve trước khi public.
- Tăng khả năng mở rộng đa ngôn ngữ với pipeline AI translation + localization warmup/on-demand.

### User Goals
- Du khách mở app/map và nghe nội dung tự động khi đến gần POI.
- Du khách có thể quét QR công khai hoặc QR trong app để nghe ngay theo ngôn ngữ chọn.
- Chủ quán có thể tự quản lý nội dung, audio, bản dịch qua owner portal.
- Admin có dashboard theo dõi hiệu quả vận hành và hành vi sử dụng thực tế.

### Non-Goals
- Không xử lý thanh toán hoặc đặt món online trong phiên bản hiện tại.
- Không xây social feed hoặc đánh giá cộng đồng từ người dùng cuối.
- Không mở tự do publish nội dung từ owner mà không qua duyệt.

## 3. Personas & User Stories
**Persona 1 - Du khách**
- Là du khách, tôi muốn app tự phát thuyết minh khi đi vào bán kính POI.
- Là du khách, tôi muốn quét QR để nghe nội dung ngay cả khi GPS không ổn định.
- Là du khách, tôi muốn đổi ngôn ngữ nghe và có fallback khi thiếu audio gốc.

**Persona 2 - Admin hệ thống**
- Là admin, tôi muốn quản lý POI tập trung và bật/tắt publish nhanh theo trạng thái.
- Là admin, tôi muốn duyệt yêu cầu owner (create/update/delete/audio/translation) trước khi áp dụng.
- Là admin, tôi muốn theo dõi top POI, heatmap, active users, engagement theo thời gian.

**Persona 3 - Chủ quán (Owner)**
- Là owner, tôi muốn đăng ký tài khoản và chờ admin duyệt để vào hệ thống.
- Là owner, tôi muốn gửi yêu cầu chỉnh sửa/xóa POI hiện có mà không phá dữ liệu đang chạy.
- Là owner, tôi muốn cập nhật audio/bản dịch để tăng trải nghiệm du khách.

## 4. Functional Requirements
- **FR-01 Authentication & Authorization (High):** JWT/cookie-based luồng đăng nhập; role + permission cho admin/owner.
- **FR-02 POI Management (High):** CRUD POI, trạng thái publish, bulk action, owner ownership.
- **FR-03 Geofencing & Narration (High):** Poll GPS, trigger POI theo bán kính, autoplay narration.
- **FR-04 QR Experience (High):** QR mobile scan + web public QR (`/qr/{id}` -> `/listen/{id}`), track analytics.
- **FR-05 Content & Localization (High):** Quản lý content theo ngôn ngữ, fallback vi/en, warmup/on-demand localization.
- **FR-06 Audio Pipeline (High):** Upload audio, TTS generation, đa tầng fallback playback trên mobile.
- **FR-07 Owner Moderation Workflow (High):** Owner submit create/update/delete/audio/translation, admin review/approve/reject.
- **FR-08 Analytics & Observability (Medium):** Track events, heatmap, topPois, engagement, live stats, QR metrics.
- **FR-09 Realtime Sync (Medium):** SignalR hub phát sự kiện POI thay đổi tới client.
- **FR-10 Offline Support (Medium):** map runtime config + offline manifest, lưu cục bộ dữ liệu cần thiết.

## 5. Technical Considerations
- **Backend API:** ASP.NET Core + EF Core, SQLite dev / SQL Server prod, static files cho media.
- **Mobile App:** .NET MAUI + Maps + ZXing QR + native TTS + audio queue/provider chain.
- **Admin Portal:** ASP.NET MVC, gọi API qua `HttpClient` + API key.
- **Owner Portal:** Razor Pages, dựa trên cookie `owner_userid`, gửi request tới API moderation.
- **Realtime:** SignalR hub `/sync` cho đồng bộ POI.
- **AI & Localization:** `api/ai/translate-content`, `api/localizations/*` (prepare-hotset, on-demand, warmup).
- **Media:** Audio/ảnh upload và cache TTS phục vụ đa ngôn ngữ.

## 6. Business Rules
| Rule | Diễn giải |
|---|---|
| BR-01 | POI chỉ hiển thị public khi `IsPublished = true`. |
| BR-02 | Owner không chỉnh trực tiếp dữ liệu production, chỉ gửi request chờ admin duyệt. |
| BR-03 | Khi owner gửi yêu cầu update, POI có thể tạm unpublish cho đến khi duyệt xong. |
| BR-04 | Geofence trigger ưu tiên phát narration theo ngôn ngữ hiện tại, fallback content/audio khi thiếu dữ liệu. |
| BR-05 | QR scan (mobile/web) phải ghi log analytics event (`qr_scan`, `tts_play`, `poi_enter`...). |
| BR-06 | Localization on-demand có kiểm tra nội dung nhạy cảm theo keyword policy trước khi sinh bản địa hóa. |
| BR-07 | Audio playback áp dụng fallback đa tầng để giảm tỉ lệ fail phát tiếng. |
| BR-08 | Admin có quyền publish/unpublish đơn lẻ và hàng loạt để phản ứng nhanh vận hành. |

---

## 7. Data Schema (Logical)
- **`PointsOfInterest`**: thông tin POI (tọa độ, radius, priority, ownerId, isPublished, qrCode, imageUrl...).
- **`PointContents`**: nội dung theo ngôn ngữ (title/subtitle/description/address/price/opening hours/audioUrl...).
- **`AudioFiles`**: metadata audio theo POI + language.
- **`TraceLogs`**: nhật ký hành vi và telemetry.
- **`Users`**: tài khoản và quyền (admin/owner, permissions, verified state).
- **`OwnerRegistrations`**: hồ sơ đăng ký owner chờ duyệt.
- **`PoiRegistrations`**: yêu cầu create/update/delete từ owner chờ admin xử lý.
- **`LocalizationJobLogs`**: log pipeline localizations warmup/on-demand.
- **`AiUsageLogs`**: log sử dụng tác vụ AI.
- **`Tours`**: mô hình tour tuyến (nếu bật trong vận hành).

---

## 8. Sơ đồ Kiến trúc Cấu trúc Hệ thống (System Architecture Diagram)
```mermaid
graph TD
    subgraph Mobile[Ứng dụng Di động - .NET MAUI]
        MAP[Bản đồ & Geofence]
        SCAN[Quét Mã QR]
        NAR[Dịch vụ Thuyết minh]
        AQ[Hàng đợi Âm thanh]
        OFF[Bộ nhớ Ngoại tuyến]
    end

    subgraph Portals[Cổng thông tin Web]
        ADMIN[Admin Portal - Quản trị viên]
        OWNER[Owner Portal - Chủ quán]
    end

    subgraph API[Backend API - ASP.NET Core]
        AUTH[Xác thực & Người dùng]
        POI[Quản lý Điểm đến POI]
        AUD[Âm thanh & Dịch thuật AI]
        ANA[Phân tích Dữ liệu Analytics]
        HUB[Đồng bộ Realtime SignalR]
    end

    subgraph DB[Lớp Dữ liệu]
        SQL[(Cơ sở dữ liệu SQLite/SQL Server)]
        FILES[(Máy chủ File Media & Audio)]
    end

    Mobile --> API
    ADMIN --> API
    OWNER --> API
    API --> SQL
    API --> FILES
    API --> HUB
    HUB --> Mobile
```

---

## 9. Sơ đồ Tuần tự Kỹ thuật (Sequence Diagrams)

### 9.1 Sơ đồ tuần tự - Đăng ký Chủ quán và Đăng nhập
```mermaid
sequenceDiagram
    autonumber
    actor Owner as Chủ quán
    participant OP as Owner Portal
    participant API as Backend API
    participant Admin as Admin Portal
    participant DB as Database

    Owner->>OP: Nhập thông tin đăng ký tài khoản
    OP->>API: POST /RegisterOwner
    API->>DB: Kiểm tra Email xem đã tồn tại chưa
    
    alt Email đã tồn tại
        DB-->>API: Cảnh báo trùng lặp Email
        API-->>OP: Hiện thông báo nhắc nhở lỗi đăng ký
    else Email Hợp lệ
        API->>DB: Lưu User và OwnerRegistration
        DB-->>API: Xác nhận thao tác tạo thành công
        API-->>OP: Báo thành công, chuyển sang chờ duyệt
    end
    
    Note over API, Admin: Quá trình thẩm định<br>của Admin Hệ thống
    Admin->>API: Phê duyệt hồ sơ (Approve Account)
    API->>DB: Đổi trạng thái user thành isVerified = true
    
    Owner->>OP: Tiến hành đăng nhập (Credentials)
    OP->>API: POST /Login xác thực
    API->>DB: Kiểm tra tài khoản và mật khẩu
    DB-->>API: Trả về thông tin User hợp lệ
    API-->>OP: Cấp phát Session/Token thành công
```

### 9.2 Sơ đồ tuần tự - Geofence và Tự động phát thuyết minh
```mermaid
sequenceDiagram
    autonumber
    actor User as Du khách
    participant App as Mobile App
    participant Geo as Geolocation Service
    participant API as Backend API
    participant Audio as Âm thanh

    User->>App: Cấp quyền và bật theo dõi định vị
    
    loop Chu kỳ quét (Mỗi 5 giây)
        App->>Geo: Lấy tọa độ GPS mới nhất
        Geo-->>App: Tọa độ hiện tại (Lat, Lng)
        App->>App: Tính toán trên GeofenceEngine
        
        alt Nằm trong vùng phủ sóng POI
            App->>API: Call API lưu lại vết (event: poi_enter)
            API-->>App: Ghi nhận truy cập vùng
            App->>App: Trích xuất nội dung văn bản từ bộ nhớ tạm
            App->>Audio: Yêu cầu phát âm thanh mô tả POI
            Audio-->>User: Phát bản thuyết minh tự động
        else Ra ngoài bán kính POI
            App-->>App: Tiếp tục giữ tương tác bản đồ
        end
    end
```

### 9.3 Sơ đồ tuần tự - Quét mã QR trên Mobile App
```mermaid
sequenceDiagram
    autonumber
    actor User as Du khách
    participant App as Mobile App
    participant API as Backend API
    participant Audio as TTS Service

    User->>App: Mở tính năng Scan QR Code
    App->>App: Quét và giải mã chuỗi mã hóa
    
    alt Format Không hợp lệ
        App-->>User: Báo lỗi mã không thuộc ứng dụng
    else Format Hợp lệ (POI ID)
        App->>API: Gửi thông báo bắt đầu quét (qr_scan log)
        API->>API: Lưu lịch sử và hệ thống TraceLogs
        API-->>App: Mang về gói thông tin POI đầy đủ
        
        App->>App: Chuyển ngôn ngữ dựa theo Cài đặt máy
        alt Tồn tại audio gốc MP3
            App->>App: Lấy file MP3 chuyển qua bộ Stream
            App-->>User: Phát âm thanh chất lượng cao
        else Chưa thu âm MP3
            App->>Audio: Chuyển text sang tiếng động bằng TTS
            Audio-->>App: Trả về Audio tạm thời (Stream mới)
            App-->>User: Play cấu hình Text-To-Speech
        end
    end
```

### 9.4 Sơ đồ tuần tự - Quản lý Điểm đến (POI) từ Admin Portal
```mermaid
sequenceDiagram
    autonumber
    actor Admin as Quản trị viên
    participant Portal as Admin Portal
    participant API as Backend API
    participant DB as Database
    participant Hub as Tín hiệu SignalR

    Admin->>Portal: Truy cập danh sách POI thực tế
    Portal->>API: Request tải danh mục với Filters
    API->>DB: Select dữ liệu PointsOfInterest
    DB-->>API: Kết quả Result Set
    API-->>Portal: Render Data lên màn hình
    
    Admin->>Portal: Thao tác Bật/Tắt trạng thái (Publish)
    Portal->>API: POST Yêu cầu chuyển trạng thái
    API->>DB: Thực hiện lệnh Update IsPublished
    DB-->>API: Update Rows = 1 (Thành công)
    
    API->>Hub: Bắn event làm mới dữ liệu
    Hub-->>Portal: Socket notify đẩy qua Mobile Client
    API-->>Portal: Gửi response 200 OK update Layout UI
```

### 9.5 Sơ đồ tuần tự - Chủ quán (Owner) gửi yêu cầu quản lý POI
```mermaid
sequenceDiagram
    autonumber
    actor Owner as Chủ quán
    participant OP as Owner Portal
    participant API as Backend API
    participant DB as Database
    actor Admin as Quản trị viên
    
    Owner->>OP: Nhập Form đổi thông tin & Nhấn Lưu
    OP->>API: Gọi API đăng ký cập nhật POI
    API->>DB: Cập nhật Trạng thái Thành Chờ (Pending)
    API->>DB: Tạm ẩn POI đang hiện hành (Unpublish)
    DB-->>API: Lưu thành công xuống đĩa cứng
    API-->>OP: Hiện popup báo thành công chờ Duyệt
    
    Note over API, Admin: Quá trình xem xét<br>yêu cầu tại Dashboard
    Admin->>API: Thao tác Duyệt Chấp thuận (Approve)
    API->>DB: Merge nội dung mới vào POI chính
    API->>DB: Đổi lại trạng thái sang Đã Duyệt và Hiển thị
    DB-->>API: Thành công hoàn toàn
    API-->>Admin: Loading lại bảng dữ liệu mới nhất
```

### 9.6 Sơ đồ tuần tự - Quy trình xử lý và dự phòng Âm thanh
```mermaid
sequenceDiagram
    autonumber
    participant App as Mobile App
    participant T1 as Máy Chủ Media (Tier 1)
    participant T2 as Đám Mây TTS (Tier 2)
    participant T3 as Hệ Điều Hành (Tier 3)

    App->>App: Gửi Event cần phát âm thanh điểm POI
    App->>T1: Lấy file nguồn gốc tĩnh (MP3 File)
    
    alt Lấy được File Mẫu MP3
        T1-->>App: Trả về link CDN âm thanh tĩnh
    else Server không có File mẫu MP3
        App->>T1: Đóng kết nối Tier 1
        App->>T2: Gửi gói tin Text lên mây (Edge TTS)
        
        alt Trả về luồng TTS
            T2-->>App: Phát kết quả Audio Stream mượt
        else Bị rớt mạng, Hết Quota (Lỗi Tier 2)
            App-->>App: Hủy kết nối Cloud, Dùng dự phòng Offline
            App->>T3: Gọi Module Phát Đọc Bản Ngữ OS (Native)
            T3-->>App: Đọc Text ra loa bằng tiếng Android/iOS mặc định
        end
    end
```

### 9.7 Sơ đồ tuần tự - Dịch thuật AI và Bản địa hóa
```mermaid
sequenceDiagram
    autonumber
    participant Client as Mobile App
    participant API as Backend Server
    participant AI as Cỗ Máy AI Dịch
    participant DB as Cache Database

    Client->>API: Chuyển ngôn ngữ, Test POI Audio
    API->>DB: Truy vấn dữ liệu Cache (PointContent)
    
    alt Dữ liệu Ngôn ngữ này có sẵn
        DB-->>API: Load thông tin dịch đầy đủ
        API-->>Client: Hiển thị giao diện tức thì
    else Dữ liệu Cache Chưa được lưu trữ
        API->>DB: Rút văn bản tiếng Anh hoặc tiếng Việt gốc
        DB-->>API: Phản hồi Văn bản Nguồn (Source Text)
        
        API->>AI: Trigger tính mảng phân tích Dịch tự động
        AI-->>API: Phản hồi Văn bản Đích (Translated Text)
        
        API->>API: Pipeline Duyệt vi phạm Chính sách
        alt Vi phạm Keyword Cấm
            API->>DB: Thu gom TraceLog lỗi Dịch Hỏng
            API-->>Client: Lỗi nội dung, hoàn về Tiếng Việt
        else Phù hợp Chuẩn Mực
            API->>DB: Lưu thành Cached File Content mới
            API-->>Client: Render giao diện chữ Đã Dịch
        end
    end
```

### 9.8 Sơ đồ tuần tự - Phân tích Dữ liệu Analytics
```mermaid
sequenceDiagram
    autonumber
    actor Admin as Quản trị viên
    participant Portal as Admin Portal 
    participant API as Controller Báo cáo
    participant DB as Log Database

    Note right of Admin: Client di động đẩy mã<br>đo lường vào màn ẩn liên tục
    
    Admin->>Portal: Nhấp vào menu Analytic & Báo cáo
    Portal->>API: Yêu cầu tải cấu trúc Dashboard Metrics
    
    API->>DB: Cấu hình GroupBy truy vấn CSDL Log Map
    DB-->>API: Phản hồi Raw Array Data Logs
    
    API->>API: Map sang Metric (Tổng User, Heatmap Cao, Click)
    API-->>Portal: Đóng gói JSON Biểu Đồ Thống Kê
    Portal-->>Admin: Xoay tròn Load ảnh và Hiển thị Graph trực quan
```

---

## 10. Activity Diagrams Theo Chức Năng

### 10.1 Activity - Đăng ký Owner và đăng nhập
```mermaid
flowchart TD
    A[Owner nhập form đăng ký] --> B[OwnerPortal gọi admin/auth/register-owner]
    B --> C{Email đã tồn tại?}
    C -- Có --> C1[Trả lỗi email_exists]
    C -- Không --> D[Tạo User role owner isVerified=false]
    D --> E[Tạo OwnerRegistration status pending]
    E --> F[Admin duyệt hồ sơ owner]
    F --> G{Approved?}
    G -- Không --> G1[Owner chưa đăng nhập được]
    G -- Có --> H[Owner login admin/auth/login]
    H --> I{isVerified=true?}
    I -- Không --> I1[Thông báo chưa được duyệt]
    I -- Có --> J[Lưu cookie owner_userid và vào dashboard]
```

### 10.2 Activity - Geofence và tự động phát thuyết minh
```mermaid
flowchart TD
    A[Mở MapPage] --> B[Khởi tạo permissions + load POI]
    B --> C[Bắt đầu vòng lặp GPS 5 giây]
    C --> D[GeofenceEngine process vị trí]
    D --> E{Trong bán kính POI?}
    E -- Không --> C
    E -- Có --> F[Track event poi_enter]
    F --> G[Hiển thị panel POI]
    G --> H[Lấy content theo ngôn ngữ chọn]
    H --> I{Có content mô tả?}
    I -- Có --> J[Play narration]
    I -- Không --> K[Fallback ngôn ngữ vi/en hoặc thông báo]
    J --> C
    K --> C
```

### 10.3 Activity - Quét QR trên mobile app
```mermaid
flowchart TD
    A[Mở ScanPage] --> B[Camera detect QR]
    B --> C[Parse payload deeplink/url/POI:id/số thuần]
    C --> D{poiId hợp lệ?}
    D -- Không --> D1[Hiện cảnh báo QR không hợp lệ]
    D -- Có --> E[Track event qr_scan]
    E --> F[Lấy content tốt nhất theo language]
    F --> G{Có description?}
    G -- Có --> H[NarrationService.SpeakAsync]
    G -- Không --> I[Refresh POI từ API và fallback đọc tên]
    H --> J[Track tts_play]
    I --> J
    J --> K[Bật lại camera detecting]
```

### 10.4 Activity - Quản lý POI từ Admin Portal
```mermaid
flowchart TD
    A[Admin mở danh sách POI overview] --> B[Lọc theo publish - owner - category - content]
    B --> C{Thao tác nào?}
    C -- Bật/Tắt Hiển thị --> D[POST admin/pois/id/publish hoặc unpublish]
    C -- Hàng loạt --> E[POST bulk publish - unpublish - delete]
    C -- Chỉnh sửa --> F[PUT api/poi/id cập nhật thông tin]
    D --> G[Lưu DB và thông báo SignalR]
    E --> G
    F --> G
    G --> H[Tải lại danh sách và hiển thị trạng thái mới]
```

### 10.5 Activity - Owner gửi yêu cầu tạo/sửa/xóa POI
```mermaid
flowchart TD
    A[Owner thao tác Create/Edit/Delete] --> B[Tạo payload registration]
    B --> C[POST submit hoặc submit-update hoặc submit-delete]
    C --> D[Lưu PoiRegistration pending]
    D --> E{Loại request}
    E -- Update --> F[Tạm unpublish POI gốc nếu đang publish]
    E -- Create/Delete --> G[Giữ trạng thái chờ duyệt]
    F --> H[Admin review request]
    G --> H
    H --> I{Approve hay Reject}
    I -- Approve --> J[Apply thay đổi vào POI/content/audio]
    I -- Reject --> K[Giữ dữ liệu cũ + lưu notes]
```

### 10.6 Activity - Audio pipeline đa tầng
```mermaid
flowchart TD
    A[Nhận yêu cầu phát audio] --> B[Tier1: pre-generated audio theo POI+lang]
    B --> C{Tìm thấy file?}
    C -- Có --> Z[Phát audio file]
    C -- Không --> D[Tier1.5 Edge TTS on-demand]
    D --> E{Sinh thành công?}
    E -- Có --> Z
    E -- Không --> F[Tier2 Cloud TTS stream]
    F --> G{Có stream?}
    G -- Có --> Z
    G -- Không --> H[Tier3 Native TTS fallback]
    H --> Z
```

### 10.7 Activity - Localization và AI translation
```mermaid
flowchart TD
    A[Client yêu cầu ngôn ngữ mới] --> B{Đã có PointContent lang đích?}
    B -- Có --> C[Trả cached localization]
    B -- Không --> D[Chọn source en hoặc vi]
    D --> E{Vi phạm keyword policy?}
    E -- Có --> F[Chặn và log blocked]
    E -- Không --> G[Tạo bản localization mới IsTTS=true]
    G --> H[Lưu DB + log completed]
    H --> I[Trả nội dung cho client]
    A --> J[Tác vụ warmup nền cho toàn bộ POI]
```

### 10.8 Activity - Analytics và dashboard
```mermaid
flowchart TD
    A[Mobile/Web phát sinh sự kiện] --> B[POST trace analytics]
    B --> C[Lưu TraceLogs]
    C --> D[API tổng hợp topPois/heatmap/engagement/active-users/timeseries]
    D --> E[AdminPortal gọi các endpoint analytics]
    E --> F[Render dashboard + biểu đồ]
    F --> G[Admin theo dõi và điều chỉnh vận hành POI]
```

---

## 11. API Surface (Nhóm endpoint chính)
- **Auth & User:** `admin/auth/*`, `admin/users/*`, `owneradmin/*`.
- **POI & Content:** `api/poi/*`, `api/content/*`, `admin/pois/*`, `owner/pois/*`.
- **Moderation:** `api/poiregistration/*`, `api/ownerregistration/*`.
- **Audio & Localization:** `api/audio/*`, `api/localizations/*`, `api/ai/*`.
- **Public Experience:** `/qr/{id}`, `/listen/{id}`, `/listen/{id}/generate-tts`.
- **Analytics:** `api/analytics/*`, `api/tracelog*` (qua portal/controller liên quan).
- **Maps/Health/Sync:** `api/maps/*`, `/health`, `/sync` (SignalR).

## 12. NFR & Acceptance Criteria
### Performance
- Narration được kích hoạt trong trải nghiệm thực tế ở mức chấp nhận được sau trigger geofence/QR.
- Dashboard analytics tải được tập dữ liệu lớn ở mức top/limit hợp lý.

### Reliability
- Fallback audio phải hoạt động khi thiếu file pre-generated.
- Public QR listen page không crash khi thiếu dữ liệu content/audio.

### Security
- API admin cần token hoặc API key hợp lệ.
- Owner chỉ thao tác trên POI thuộc quyền sở hữu.
- Dữ liệu nhạy cảm owner registration được mã hóa ở tầng lưu trữ.

### Operability
- Có health endpoint để kiểm tra readiness.
- Có log job cho localization và AI usage phục vụ debug/vận hành.

---

## 13. Open Issues / Improvement Backlog
- Chuẩn hóa cơ chế auth giữa JWT, cookie, API key để giảm phân mảnh.
- Bổ sung policy/permission matrix chính thức theo role.
- Chuẩn hóa event taxonomy analytics (tên event, source, schema).
- Tăng test coverage cho luồng moderation owner và public QR listen.
- Bổ sung SLA kỹ thuật cụ thể (P95 response time, error budget, retry policy).

