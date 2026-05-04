# PART 1: SD-01 → SD-07
# Copy từng block @startuml...@enduml vào https://www.plantuml.com/plantuml/uml

---
## SD-01 — Đăng ký Owner & Đăng nhập
**Mô tả:** Owner đăng ký trên OwnerPortal (email, password, tên quán, địa chỉ, CCCD) → API tạo User(isVerified=false) + OwnerRegistration(pending) → SignalR báo Admin → Admin duyệt → Owner đăng nhập → JWT → cookie → OwnerDashboard.

```plantuml
@startuml SD01
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
skinparam BoxPadding 12
autonumber "<b>[00]"

title <b><size:20>SD-01: Đăng ký Owner & Đăng nhập</size></b>\n<size:13><i>Owner đăng ký → Admin duyệt → Owner đăng nhập → OwnerDashboard</i></size>

actor "Owner\n(Chủ quán)" as Owner #88CCEE
participant "OwnerPortal\n(Razor Pages)" as OP #DDAA33
participant "AuthController\n(REST API)" as API #44BB99
database "Database\n(SQL Server)" as DB #EE8866
participant "SyncHub\n(SignalR)" as Hub #BBCC33
participant "AdminPortal\n(MVC Web)" as Admin #EE6677

== <size:14><b>PHẦN 1: ĐĂNG KÝ TÀI KHOẢN OWNER</b></size> ==

Owner -> OP : Mở trang **Register**\nNhập: Email, Password,\nTên quán, Địa chỉ, CCCD
activate OP
OP -> API : **POST** admin/auth/register-owner\n{email, password, shopName, shopAddress, cccd}
activate API
API -> API : Normalize email (trim + lowercase)\nHash password (SHA256 + salt)\nEncrypt CCCD (EncryptionService.Protect)
API -> DB : SELECT Users WHERE Email = ?
activate DB
DB --> API : Kết quả kiểm tra
deactivate DB

alt #FFD6D6 Email đã tồn tại
  API --> OP : **409 Conflict** "email_exists"
  OP --> Owner : ❌ "Email này đã được đăng ký"
else #D6FFD6 Email chưa có → Tạo tài khoản
  API -> DB : INSERT **Users** (role="owner", isVerified=**false**)
  activate DB
  DB --> API : userId
  deactivate DB
  API -> DB : INSERT **OwnerRegistrations**\n(status="pending", shopName, cccdEncrypted)
  activate DB
  DB --> API : registrationId
  deactivate DB
  API -> Hub : SendAsync(**"OwnerRegistrationSubmitted"**\n{registrationId, email, shopName})
  activate Hub
  Hub --> Admin : 🔔 Thông báo realtime: có đơn đăng ký mới
  deactivate Hub
  API --> OP : **201 Created**\n{userId, registrationId, status:"pending"}
  deactivate API
  OP --> Owner : ✅ Redirect → **/RegisterSuccess**\n"Vui lòng chờ admin duyệt đơn"
  deactivate OP
end

== <size:14><b>PHẦN 2: ADMIN DUYỆT ĐĂNG KÝ</b></size> ==

Admin -> API : **POST** owner-admin/{id}/**approve**\n{notes, reviewedBy}
activate API
API -> DB : UPDATE OwnerRegistrations\nSET status="**approved**"
activate DB
DB --> API : OK
deactivate DB
API -> DB : UPDATE Users\nSET isVerified=**true**
activate DB
DB --> API : OK
deactivate DB
API --> Admin : ✅ Đã duyệt thành công
deactivate API

== <size:14><b>PHẦN 3: OWNER ĐĂNG NHẬP</b></size> ==

Owner -> OP : Mở trang **Login**\nNhập: Email, Password
activate OP
OP -> API : **POST** admin/auth/**login**\n{email, password}
activate API
API -> API : Normalize email (trim + lowercase)
API -> DB : SELECT Users WHERE Email = ?
activate DB
DB --> API : User record (hoặc null)
deactivate DB

alt #FFD6D6 User không tồn tại / sai password
  API --> OP : **401** "Email hoặc mật khẩu không chính xác"
  OP --> Owner : ❌ Sai thông tin đăng nhập
else #FFF0D6 Đúng nhưng chưa duyệt (isVerified=false)
  API --> OP : **200** {isVerified: **false**}
  OP --> Owner : ❌ "Tài khoản chưa được admin duyệt"
else #D6FFD6 Đăng nhập thành công
  API -> API : **GenerateJwt**(user, permissions)\nAlgorithm: HMAC-SHA256\nExpiry: 180 phút\nClaims: userId, role, permissions
  API --> OP : **200** {userId, email, role,\nisVerified: true, accessToken}
  deactivate API
  OP -> OP : Set Cookie:\n**owner_userid** = userId\n**owner_verified** = "1"
  OP --> Owner : ✅ Redirect → **/OwnerDashboard**
  deactivate OP
end

note over Owner, Admin
  <b>Ghi chú bảo mật:</b>
  • Password: SHA256 + static salt (POC — production nên dùng bcrypt/Argon2)
  • CCCD: Mã hoá bằng EncryptionService.Protect
  • JWT: HMAC-SHA256, hạn 180 phút, chứa claims userId + role + permissions
end note

@enduml
```

### Activity Diagram — SD-01

```plantuml
@startuml SD01_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ActivityFontColor #000000
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam DiamondFontColor #000000
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-01 Activity: Đăng ký Owner & Đăng nhập

start

partition "**ĐĂNG KÝ**" {
  :Owner mở /Register
  Nhập: Email, Password,
  Tên quán, Địa chỉ, CCCD;

  :POST admin/auth/register-owner
  Normalize email + Hash password
  + Encrypt CCCD;

  :SELECT Users WHERE Email = ?;

  if (Email đã tồn tại?) then (Có)
    :409 "email_exists"
    ----
    Hiển thị lỗi: Email đã đăng ký;
    stop
  else (Chưa có)
  endif

  :INSERT Users
  (role=owner, isVerified=false);

  :INSERT OwnerRegistrations
  (status=pending);

  :SignalR: SendAsync
  ("OwnerRegistrationSubmitted");

  :201 Created
  Redirect /RegisterSuccess
  "Chờ admin duyệt";
}

partition "**ADMIN DUYỆT**" {
  :Admin xem danh sách pending;

  if (Admin quyết định?) then (Từ chối)
    :UPDATE status=rejected;
    stop
  else (Duyệt)
  endif

  :POST owner-admin/{id}/approve;

  :UPDATE status=approved
  UPDATE isVerified=true;
}

partition "**ĐĂNG NHẬP**" {
  :Owner mở /Login
  Nhập: Email, Password;

  :POST admin/auth/login
  Normalize email;

  :SELECT Users WHERE Email = ?;

  if (Đúng email & password?) then (Sai)
    :401 Unauthorized;
    stop
  else (Đúng)
  endif

  if (isVerified = true?) then (false)
    :Chưa được admin duyệt;
    stop
  else (true)
  endif

  :GenerateJwt(user, permissions)
  HMAC-SHA256, 180 phút;

  :200 OK {userId, accessToken};

  :Set Cookie
  owner_userid, owner_verified;

  :Redirect /OwnerDashboard;
}

stop

@enduml
```

---
## SD-02 — Geofence & Tự động phát Narration
**Mô tả:** LocationPollingService chạy background mỗi 5-10s lấy GPS → GeofenceEngine tính khoảng cách → nếu vào vùng POI + chưa cooldown → trigger ShowPoiDetail + PlayNarration TTS qua AudioQueueService → MiniPlayer.

```plantuml
@startuml SD02
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-02: Geofence & Tự động phát Narration</size></b>\n<size:13><i>GPS polling → kiểm tra vùng POI → tự động phát TTS</i></size>

actor "Du khách" as User #88CCEE
participant "Mobile App\n(MapPage)" as App #DDAA33
participant "LocationPolling\nService" as GPS #44BB99
participant "GeofenceEngine" as GEO #EE8866
participant "NarrationService" as NAR #BBCC33
participant "AudioQueue\nService" as AQ #EE6677
participant "IAudioService\n(Platform)" as Audio #CC6699
participant "API\n(AnalyticsController)" as API #77AADD

App -> GPS : **StartAsync()**
activate GPS

loop <b>Mỗi 5-10 giây</b>
  GPS -> GPS : GetLocationAsync() → (lat, lng)
  GPS -> GEO : **ProcessLocation**(lat, lng)
  activate GEO
  GEO -> GEO : Tính khoảng cách đến từng POI\n(Haversine formula)

  alt #D6FFD6 Khoảng cách <= POI.Radius + chưa cooldown
    GEO --> App : 🔔 Event: **PoiTriggered**(poi)
    deactivate GEO
    activate App
    App -> App : **ShowPoiDetail**(poi)\n(hiện card chi tiết)
    App -> API : **POST** api/analytics\n{event:"poi_enter", poiId, deviceId}
    activate API
    API --> App : 200 OK
    deactivate API
    App -> NAR : **PlayNarration**(description)
    activate NAR
    NAR -> AQ : Enqueue(audioItem)
    activate AQ
    AQ -> Audio : **PlayAsync**(ttsUrl)
    activate Audio
    Audio --> App : 🔊 Đang phát TTS
    deactivate Audio
    deactivate AQ
    deactivate NAR
    App -> App : **ShowMiniPlayerAsync**()\n(hiện thanh mini player)
    deactivate App
  else #FFD6D6 Ngoài vùng / đang cooldown
    GEO -> GEO : Bỏ qua (tránh spam trigger)
    deactivate GEO
  end

  GPS -> API : **POST** api/analytics\n{event:"poi_heartbeat", lat, lng, deviceId}
  activate API
  API --> GPS : 200 OK
  deactivate API
end

deactivate GPS
@enduml
```

### Activity Diagram — SD-02

```plantuml
@startuml SD02_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-02 Activity: Geofence & Tự động phát Narration

start

:LocationPollingService.StartAsync();

repeat

  :GetLocationAsync()
  → lấy (lat, lng);

  :GeofenceEngine.ProcessLocation()
  Tính khoảng cách đến từng POI
  (Haversine formula);

  if (Trong bán kính POI?) then (Không)
    :Bỏ qua;
  else (Có)
    if (Đang cooldown?) then (Có)
      :Bỏ qua (tránh spam);
    else (Không)
      :PoiTriggered event;
      :ShowPoiDetail(poi);
      :POST api/analytics
      {event: poi_enter};
      :PlayNarration(description);
      :AudioQueue.Enqueue
      → PlayAsync(ttsUrl);
      :ShowMiniPlayerAsync();
    endif
  endif

  :POST api/analytics
  {event: poi_heartbeat, lat, lng};

  :Chờ 5-10 giây;

repeat while (App đang chạy?) is (Có)

stop

@enduml
```

---
## SD-03 — Quét mã QR (Mobile)
**Mô tả:** Du khách mở camera quét QR → decode poiId → app tìm POI → ShowPoiDetail → gọi API lấy content + audio + reviews → track event qr_scan → auto phát TTS.

```plantuml
@startuml SD03
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-03: Quét mã QR (Mobile App)</size></b>\n<size:13><i>Camera → decode QR → hiện chi tiết POI → phát TTS</i></size>

actor "Du khách" as User #88CCEE
participant "ScanPage\n(Camera)" as Scan #DDAA33
participant "MapPage\n(Main Screen)" as Map #44BB99
participant "API\n(PoiController)" as PoiAPI #EE8866
participant "API\n(ContentController)" as ContentAPI #BBCC33
participant "API\n(AudioController)" as AudioAPI #EE6677
participant "API\n(AnalyticsController)" as Analytics #77AADD

User -> Scan : Mở camera quét mã QR
activate Scan
Scan -> Scan : Decode QR → poiId
Scan -> Map : Navigate to MapPage(poiId)
deactivate Scan

activate Map
Map -> PoiAPI : **GET** api/poi/load-all
activate PoiAPI
PoiAPI --> Map : List<PoiModel>
deactivate PoiAPI
Map -> Map : Tìm POI theo poiId\n→ **ShowPoiDetail**(poi)

Map -> ContentAPI : **GET** api/content/by-poi/{poiId}
activate ContentAPI
ContentAPI --> Map : Content (title, description,\naddress, phone...)
deactivate ContentAPI

Map -> AudioAPI : **GET** api/audio/by-poi/{poiId}
activate AudioAPI
AudioAPI --> Map : List<AudioModel>
deactivate AudioAPI

Map -> Analytics : **POST** api/analytics\n{event:"qr_scan", poiId, deviceId}
activate Analytics
Analytics --> Map : 200 OK
deactivate Analytics

Map -> Map : **PlayNarration**(description)\n→ AudioQueue → MiniPlayer
Map --> User : Hiện chi tiết POI + Tự động phát TTS
deactivate Map
@enduml
```

### Activity Diagram — SD-03

```plantuml
@startuml SD03_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-03 Activity: Quét mã QR (Mobile App)

start

:Mở camera quét QR;

:Decode QR → poiId;

:Navigate to MapPage(poiId);

:GET api/poi/load-all;

:Tìm POI theo poiId;

if (POI tồn tại?) then (Không)
  :Hiển thị lỗi;
  stop
else (Có)
endif

:ShowPoiDetail(poi);

:GET api/content/by-poi/{poiId}
→ title, description, address...;

:GET api/audio/by-poi/{poiId}
→ List<AudioModel>;

:POST api/analytics
{event: qr_scan, poiId};

:PlayNarration(description)
→ AudioQueue → MiniPlayer;

:Hiện chi tiết POI + phát TTS;

stop

@enduml
```

---
## SD-04 — Quét QR Web Public (/qr/{id})
**Mô tả:** Du khách quét QR bằng camera (không cần app) → browser mở /qr/{poiId} → PublicQrController redirect /listen/{id} → trang web hiện thông tin + auto-generate TTS nếu chưa cache → phát audio trên web.

```plantuml
@startuml SD04
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-04: Quét QR Web Public (/qr/{id})</size></b>\n<size:13><i>Camera → browser → /qr → /listen → auto-play TTS</i></size>

actor "Du khách" as User #88CCEE
participant "Trình duyệt\n(Mobile Browser)" as Browser #DDAA33
participant "PublicQrController\n(API)" as QR #44BB99
database "Database\n(POI + Content)" as DB #EE8866
participant "TTS Engine\n(gTTS)" as TTS #BBCC33
participant "AnalyticsController\n(API)" as Analytics #EE6677

User -> Browser : Quét QR bằng camera\n→ URL: **/qr/{poiId}?lang=vi**
activate Browser
Browser -> QR : **GET** /qr/{poiId}?lang=vi
activate QR
QR -> DB : SELECT POI WHERE Id = poiId
activate DB
DB --> QR : POI record
deactivate DB

alt #FFD6D6 POI không tồn tại
  QR --> Browser : **404** Not Found
else #D6FFD6 POI hợp lệ
  QR -> QR : Log TraceLog\n(event:"qr_scan", source:"web_public")
  QR --> Browser : **302 Redirect** → /listen/{poiId}?lang=vi
end

Browser -> QR : **GET** /listen/{poiId}?lang=vi
QR -> DB : SELECT POI + Content\nWHERE poiId, lang
activate DB
DB --> QR : POI + Content data
deactivate DB

QR -> QR : Kiểm tra TTS cache:\n/tts-cache/{lang}/poi_{id}.mp3

alt #FFF0D6 Chưa có TTS cache
  QR -> TTS : **Generate** MP3 (text, lang="vi")
  activate TTS
  TTS --> QR : MP3 binary
  deactivate TTS
  QR -> QR : Save → /tts-cache/vi/poi_{id}.mp3
else #D6FFD6 Đã có cache
  QR -> QR : Dùng file MP3 có sẵn
end

QR --> Browser : **200** HTML Listen Page\n(tên, mô tả, ảnh, audio player,\nnút chọn ngôn ngữ)
deactivate QR

Browser -> Browser : Auto-play audio MP3
Browser -> Analytics : **POST** /qr/track\n{event:"listen_start", poiId}
activate Analytics
Analytics --> Browser : 200 OK
deactivate Analytics
deactivate Browser

User -> Browser : Chọn ngôn ngữ khác (EN, JA...)\n→ reload /listen/{poiId}?lang=en
note right : Lặp lại flow tương tự\nvới ngôn ngữ mới
@enduml
```

### Activity Diagram — SD-04

```plantuml
@startuml SD04_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-04 Activity: Quét QR Web Public

start

:Quét QR bằng camera
→ URL /qr/{poiId}?lang=vi;

:GET /qr/{poiId}?lang=vi;

:SELECT POI WHERE Id = poiId;

if (POI tồn tại?) then (Không)
  :404 Not Found;
  stop
else (Có)
endif

:Log TraceLog (qr_scan, web_public);

:302 Redirect → /listen/{poiId};

:GET /listen/{poiId}?lang=vi;

:SELECT POI + Content;

if (TTS cache tồn tại?) then (Có)
  :Dùng file MP3 có sẵn;
else (Chưa có)
  :gTTS Generate MP3;
  :Save → /tts-cache/{lang}/poi_{id}.mp3;
endif

:Trả HTML Listen Page
(info + audio player);

:Auto-play audio;

:POST /qr/track {listen_start};

if (Đổi ngôn ngữ?) then (Có)
  :Reload /listen/{poiId}?lang=...;
  note right: Lặp lại flow
else (Không)
endif

stop

@enduml
```

---
## SD-05 — Admin Quản lý POI (CRUD + Publish/Unpublish)
**Mô tả:** Admin xem danh sách POI → tạo/sửa/xóa trực tiếp → publish/unpublish → mỗi thao tác broadcast SignalR (PoiAdded/Updated/Deleted + RequestFullPoiSync) để Mobile cập nhật realtime.

```plantuml
@startuml SD05
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-05: Admin CRUD POI + Publish/Unpublish</size></b>\n<size:13><i>Admin tạo/sửa/xóa/publish POI → SignalR broadcast</i></size>

actor "Admin" as Admin #88CCEE
participant "AdminPortal\n(PoiAdmin + AdminMap)" as Portal #DDAA33
participant "API\n(PoiController)" as API #44BB99
participant "API\n(AdminPoisController)" as AdminAPI #EE8866
database "Database\n(PointsOfInterest)" as DB #BBCC33
participant "SyncHub\n(SignalR)" as Hub #EE6677

== <size:14><b>XEM DANH SÁCH POI</b></size> ==

Admin -> Portal : Mở **/PoiAdmin**
activate Portal
Portal -> API : **GET** api/poi/load-all
activate API
API --> Portal : List<PoiModel>
deactivate API
Portal --> Admin : 📋 Bảng danh sách POI
deactivate Portal

== <size:14><b>TẠO POI MỚI</b></size> ==

Admin -> Portal : Nhập thông tin → bấm "Tạo POI"
activate Portal
Portal -> API : **POST** api/poi\n{name, lat, lng, radius, imageUrl...}
activate API
API -> DB : INSERT **PointsOfInterest**
activate DB
DB --> API : New POI (id)
deactivate DB
API -> Hub : SendAsync(**"PoiAdded"**, poi)\nSendAsync(**"RequestFullPoiSync"**)
activate Hub
Hub --> Hub : Broadcast tới Mobile + Web
deactivate Hub
API --> Portal : **201 Created**
deactivate API
Portal --> Admin : ✅ POI đã tạo
deactivate Portal

== <size:14><b>PUBLISH / UNPUBLISH</b></size> ==

Admin -> Portal : Toggle Publish trên bản đồ Admin
activate Portal
Portal -> AdminAPI : **POST** admin/pois/{id}/**publish**\n(hoặc /unpublish)
activate AdminAPI
AdminAPI -> DB : UPDATE **IsPublished** = true/false
activate DB
DB --> AdminAPI : OK
deactivate DB
AdminAPI --> Portal : **200 OK**
deactivate AdminAPI
Portal --> Admin : ✅ Đã publish/unpublish
deactivate Portal

== <size:14><b>XÓA POI</b></size> ==

Admin -> Portal : Bấm "Xóa" POI
activate Portal
Portal -> API : **DELETE** api/poi/{id}
activate API
API -> DB : DELETE PointsOfInterest\n+ cleanup Audio, Content
activate DB
DB --> API : OK
deactivate DB
API -> Hub : SendAsync(**"PoiDeleted"**, id)\nSendAsync(**"RequestFullPoiSync"**)
activate Hub
Hub --> Hub : Broadcast
deactivate Hub
API --> Portal : **204 No Content**
deactivate API
Portal --> Admin : ✅ Đã xóa
deactivate Portal
@enduml
```

### Activity Diagram — SD-05

```plantuml
@startuml SD05_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-05 Activity: Admin CRUD POI + Publish/Unpublish

start

:Admin mở /PoiAdmin;

:GET api/poi/load-all
→ hiện bảng danh sách POI;

if (Thao tác?) then (Tạo mới)
  :Nhập: tên, toạ độ, bán kính,
  ảnh, danh mục...;
  :POST api/poi;
  :INSERT PointsOfInterest;
  :SignalR: PoiAdded
  + RequestFullPoiSync;
else if (Publish/Unpublish) then
  :POST admin/pois/{id}/publish
  (hoặc /unpublish);
  :UPDATE IsPublished;
else if (Sửa) then
  :PUT api/poi/{id};
  :UPDATE PointsOfInterest;
  :SignalR: PoiUpdated
  + RequestFullPoiSync;
else (Xóa)
  :DELETE api/poi/{id};
  :DELETE POI + Audio + Content;
  :SignalR: PoiDeleted
  + RequestFullPoiSync;
endif

:Cập nhật danh sách;

stop

@enduml
```

---
## SD-06 — Owner Submit Update/Delete → Admin Duyệt
**Mô tả:** Owner chỉnh sửa POI trên OwnerPortal → tạo PoiRegistration (requestType=update/delete, pending). Admin xem Pending → xem diff → approve (API cập nhật/xóa POI thật) hoặc reject.

```plantuml
@startuml SD06
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-06: Owner Submit Update/Delete → Admin Duyệt</size></b>\n<size:13><i>Owner gửi yêu cầu → Admin xem diff → Approve/Reject → cập nhật DB</i></size>

actor "Owner\n(Chủ quán)" as Owner #88CCEE
participant "OwnerPortal\n(EditPoi.cshtml)" as OP #DDAA33
participant "API\n(PoiRegistrationController)" as API #44BB99
database "Database\n(PoiRegistrations +\nPointsOfInterest)" as DB #EE8866
actor "Admin" as Admin #BBCC33
participant "AdminPortal\n(AdminPoiRegistrations)" as AP #EE6677

== <size:14><b>PHẦN 1: OWNER GỬI YÊU CẦU UPDATE</b></size> ==

Owner -> OP : Chỉnh sửa thông tin POI → bấm "Gửi"
activate OP
OP -> API : **POST** api/poiregistration/submit-update/{poiId}\n{name, category, lat, lng, contentTitle,\ncontentDescription, imageUrl...}
activate API
API -> DB : INSERT **PoiRegistrations**\n(requestType="update", targetPoiId,\nstatus="pending")
activate DB
DB --> API : registrationId
deactivate DB
API --> OP : **200 OK**
deactivate API
OP --> Owner : ✅ "Đã gửi yêu cầu chỉnh sửa\nChờ admin duyệt"
deactivate OP

== <size:14><b>PHẦN 1b: OWNER GỬI YÊU CẦU XÓA</b></size> ==

Owner -> OP : Bấm "Yêu cầu xóa POI"
activate OP
OP -> API : **POST** api/poiregistration/submit-delete/{poiId}
activate API
API -> DB : INSERT **PoiRegistrations**\n(requestType="delete", targetPoiId,\nstatus="pending")
activate DB
DB --> API : registrationId
deactivate DB
API --> OP : **200 OK**
deactivate API
OP --> Owner : ✅ "Đã gửi yêu cầu xóa"
deactivate OP

== <size:14><b>PHẦN 2: ADMIN DUYỆT</b></size> ==

Admin -> AP : Mở **/AdminPoiRegistrations/Pending**
activate AP
AP -> API : **GET** api/poiregistration/pending
activate API
API -> DB : SELECT WHERE status="pending"
activate DB
DB --> API : List<PoiRegistration>
deactivate DB
API --> AP : Danh sách chờ duyệt
deactivate API
AP --> Admin : 📋 Bảng yêu cầu pending

Admin -> AP : Bấm "Xem chi tiết" → xem diff\nso sánh dữ liệu cũ vs mới

alt #D6FFD6 Approve (requestType = "update")
  Admin -> AP : Bấm "Duyệt"
  AP -> API : **POST** api/poiregistration/{id}/**approve**\n{notes, reviewedBy}
  activate API
  API -> DB : UPDATE **PointsOfInterest**\nSET name, category, lat, lng...
  API -> DB : UPSERT **PointContents** (lang=vi)
  API -> DB : UPDATE registration status="**approved**"
  API --> AP : **200** {success: true, poiId}
  deactivate API
  AP --> Admin : ✅ POI đã cập nhật

else #D6FFD6 Approve (requestType = "delete")
  Admin -> AP : Bấm "Duyệt xóa"
  AP -> API : **POST** api/poiregistration/{id}/**approve**
  activate API
  API -> DB : DELETE PointContents WHERE poiId
  API -> DB : DELETE AudioFiles WHERE poiId
  API -> DB : DELETE PointsOfInterest WHERE id
  API -> DB : UPDATE registration status="**approved**"
  API --> AP : **200** {success: true}
  deactivate API
  AP --> Admin : ✅ POI đã xóa

else #FFD6D6 Reject
  Admin -> AP : Bấm "Từ chối"
  AP -> API : **POST** api/poiregistration/{id}/**reject**\n{notes: "Lý do từ chối"}
  activate API
  API -> DB : UPDATE status="**rejected**"
  API --> AP : **200 OK**
  deactivate API
  AP --> Admin : ❌ Đã từ chối
end
deactivate AP
@enduml
```

### Activity Diagram — SD-06

```plantuml
@startuml SD06_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-06 Activity: Owner Update/Delete → Admin Duyệt

start

partition "**OWNER GỬI YÊU CẦU**" {
  if (Loại yêu cầu?) then (Update)
    :Chỉnh sửa thông tin POI;
    :POST submit-update/{poiId};
    :INSERT PoiRegistrations
    (type=update, pending);
  else (Delete)
    :Bấm "Yêu cầu xóa";
    :POST submit-delete/{poiId};
    :INSERT PoiRegistrations
    (type=delete, pending);
  endif

  :Hiển thị: Chờ admin duyệt;
}

partition "**ADMIN DUYỆT**" {
  :Admin mở Pending list;
  :GET poiregistration/pending;
  :Xem chi tiết + diff;

  if (Admin quyết định?) then (Từ chối)
    :POST /{id}/reject;
    :UPDATE status=rejected;
    stop
  else (Duyệt)
  endif

  if (requestType?) then (update)
    :UPDATE PointsOfInterest;
    :UPSERT PointContents;
  else (delete)
    :DELETE Content + Audio + POI;
  endif

  :UPDATE status=approved;
}

stop

@enduml
```

---
## SD-07 — Content CRUD + AI Auto-Translate + Rebuild
**Mô tả:** Admin tạo content tiếng Việt → bấm "Dịch tự động" → AiController gọi LLM dịch sang ngôn ngữ đích → lưu PointContents → SignalR broadcast. Rebuild All = dịch batch tất cả POI.

```plantuml
@startuml SD07
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-07: Content CRUD + AI Auto-Translate + Rebuild</size></b>\n<size:13><i>Tạo content VI → AI dịch 9 ngôn ngữ → SignalR broadcast</i></size>

actor "Admin" as Admin #88CCEE
participant "AdminPortal\n(TranslationAdmin)" as Portal #DDAA33
participant "API\n(ContentController)" as Content #44BB99
participant "API\n(AiController)" as AI #EE8866
participant "LLM\n(OpenAI / Gemini)" as LLM #BBCC33
database "Database\n(PointContents)" as DB #EE6677
participant "SyncHub\n(SignalR)" as Hub #CC6699

== <size:14><b>TẠO / SỬA CONTENT TIẾNG VIỆT</b></size> ==

Admin -> Portal : Nhập: Title, Description, Address,\nSĐT, Giá, Giờ mở cửa...
activate Portal
Portal -> Content : **POST** api/content\n{poiId:5, languageCode:"vi",\ntitle, description, address...}
activate Content
Content -> DB : INSERT **PointContents**
activate DB
DB --> Content : contentId
deactivate DB
Content -> Hub : SendAsync(**"ContentCreated"**, model)
activate Hub
Hub --> Hub : Broadcast
deactivate Hub
Content --> Portal : **201 Created**
deactivate Content
Portal --> Admin : ✅ Content tiếng Việt đã lưu
deactivate Portal

== <size:14><b>DỊCH TỰ ĐỘNG SANG NGÔN NGỮ KHÁC</b></size> ==

Admin -> Portal : Bấm "Dịch tự động" → chọn EN
activate Portal
Portal -> AI : **POST** api/ai/translate-content\n{poiId:5, targetLanguageCode:"en"}
activate AI
AI -> DB : SELECT PointContents\nWHERE poiId=5, lang="vi"
activate DB
DB --> AI : Content tiếng Việt (source)
deactivate DB
AI -> LLM : **Translate** Vietnamese → English\n(title, subtitle, description,\naddress, priceMin, priceMax...)
activate LLM
LLM --> AI : Bản dịch tiếng Anh
deactivate LLM
AI --> Portal : {translatedTitle, translatedDescription...}
deactivate AI

Portal -> Content : **POST** api/content\n{poiId:5, lang:"en", title, desc...}
activate Content
Content -> DB : INSERT/UPDATE **PointContents**
activate DB
DB --> Content : OK
deactivate DB
Content -> Hub : SendAsync(**"ContentCreated"**, model)
Content --> Portal : **201 Created**
deactivate Content
Portal --> Admin : ✅ Đã dịch sang English
deactivate Portal

== <size:14><b>REBUILD ALL TRANSLATIONS (BATCH)</b></size> ==

Admin -> Content : **POST** api/content/rebuild-all-translations?lang=en
activate Content

loop <b>Mỗi POI có content tiếng Việt</b>
  Content -> AI : **POST** ai/translate-content\n{poiId, targetLang:"en"}
  activate AI
  AI -> LLM : Translate VI → EN
  activate LLM
  LLM --> AI : Bản dịch
  deactivate LLM
  AI --> Content : Translated content
  deactivate AI
  Content -> DB : UPSERT **PointContents** (lang="en")
end

Content -> Hub : SendAsync(**"RequestFullPoiSync"**)
Content --> Admin : ✅ Rebuilt {N} bản dịch
deactivate Content
@enduml
```

### Activity Diagram — SD-07

```plantuml
@startuml SD07_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-07 Activity: Content CRUD + AI Auto-Translate

start

partition "**TẠO CONTENT TIẾNG VIỆT**" {
  :Admin nhập Title, Description,
  Address, SĐT, Giá...;
  :POST api/content
  {poiId, lang:vi};
  :INSERT PointContents;
  :SignalR: ContentCreated;
}

partition "**DỊCH TỰ ĐỘNG**" {
  :Admin bấm "Dịch tự động"
  → chọn ngôn ngữ đích;
  :POST api/ai/translate-content
  {poiId, targetLang};
  :SELECT Content lang=vi (source);
  :LLM dịch VI → target language;
  :POST api/content {poiId, lang:target};
  :INSERT/UPDATE PointContents;
  :SignalR: ContentCreated;
}

partition "**REBUILD ALL (BATCH)**" {
  :Admin bấm "Rebuild All";
  :POST rebuild-all-translations?lang=...;

  repeat
    :Lấy POI tiếp theo có content VI;
    :AI translate-content;
    :UPSERT PointContents;
  repeat while (Còn POI?) is (Có)

  :SignalR: RequestFullPoiSync;
}

stop

@enduml
```
