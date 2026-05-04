# PART 2: SD-08 → SD-13
# Copy từng block @startuml...@enduml vào https://www.plantuml.com/plantuml/uml

---
## SD-08 — Analytics Dashboard (+ User đang Online)
**Mô tả:** Mobile gửi trace events → anti-spam MemoryCache → lưu TraceLogs → SignalR broadcast "TraceLogged". Admin Dashboard gọi GET summary (đếm user online = distinct deviceId trong 90s), topPois, heatmap, timeseries → hiện KPI cards + biểu đồ. JS auto-reload mỗi 20s.

```plantuml
@startuml SD08
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-08: Analytics Dashboard + User đang Online</size></b>\n<size:13><i>Trace events → anti-spam → DB → SignalR → Admin KPI dashboard</i></size>

actor "Du khách" as User #88CCEE
participant "Mobile App" as App #DDAA33
participant "API\n(AnalyticsController)" as API #44BB99
participant "MemoryCache\n(Anti-Spam)" as Cache #EE8866
database "TraceLogs\n(Database)" as DB #BBCC33
participant "SyncHub\n(SignalR)" as Hub #EE6677
actor "Admin" as Admin #CC6699
participant "AdminPortal\n(AnalyticsAdmin)" as Portal #77AADD

== <size:14><b>PHẦN 1: MOBILE GỬI TRACE EVENT</b></size> ==

User -> App : Tương tác (geofence, QR,\nnghe audio, xem POI...)
App -> API : **POST** api/analytics\n{event, poiId, deviceId, extraJson}
activate API
API -> API : Tạo fingerprint =\nMD5(event + poiId + deviceId)
API -> Cache : GET cache[fingerprint]
activate Cache

alt #FFD6D6 Cache HIT (duplicate < 3 phút)
  Cache --> API : Đã có → trùng
  deactivate Cache
  API --> App : **200** {ignored: true,\nreason: "exact_duplicate"}
else #D6FFD6 Cache MISS → Event mới
  Cache --> API : Không có
  deactivate Cache
  API -> Cache : SET cache[fingerprint]\nTTL = 3 phút
  API -> DB : INSERT **TraceLog**\n{PoiId, DeviceId, TimestampUtc,\nExtraJson, SessionId}
  activate DB
  DB --> API : OK
  deactivate DB
  API -> Hub : SendAsync(**"TraceLogged"**, {\nPoiId, PoiName, DeviceId, Event})
  activate Hub
  Hub --> Portal : 🔔 Event realtime
  deactivate Hub
  API --> App : **200** {ignored: false}
end
deactivate API

== <size:14><b>PHẦN 2: ADMIN XEM DASHBOARD</b></size> ==

Admin -> Portal : Mở **/AnalyticsAdmin**
activate Portal

Portal -> API : **GET** api/analytics/**summary**
activate API
note right of API
  <b>Tính User đang Online:</b>
  1. SELECT TraceLogs WHERE
     TimestampUtc >= (now - 90s)
  2. Lọc event: qr_scan, listen_start,
     poi_heartbeat, web_session_active
  3. Lọc source: mobile_app,
     mobile_scan, app_audio_queue
  4. Distinct(deviceId) → đếm
end note
API -> DB : SELECT TraceLogs\nWHERE TimestampUtc >= (now - 90s)
activate DB
DB --> API : Recent logs
deactivate DB
API -> API : Distinct deviceId\n→ **OnlineUsers** count
API --> Portal : {onlineUsers, totalVisitors,\ntotalEvents, avgDuration...}
deactivate API

Portal -> API : **GET** api/analytics/**topPois**?top=10
activate API
API --> Portal : Top 10 POI phổ biến nhất
deactivate API

Portal -> API : **GET** api/analytics/**heatmap**
activate API
API --> Portal : Heatmap data (lat, lng, intensity)
deactivate API

Portal -> API : **GET** api/analytics/**qr-scan-counts**?top=50
activate API
API -> DB : COUNT TraceLogs\nWHERE event="qr_scan"\nGROUP BY PoiId
activate DB
DB --> API : QrScanCountDto[]
deactivate DB
API --> Portal : QR Scan counts per POI
deactivate API

Portal -> API : **GET** api/analytics/**timeseries**
activate API
API --> Portal : Biểu đồ theo giờ/ngày
deactivate API

Portal -> API : **GET** api/analytics/**app-listen-metrics**
activate API
API --> Portal : {app_tts_play, app_audio_play}
deactivate API

Portal --> Admin : 📊 Dashboard:\n• KPI cards (Online, Visitors, Events, **QR Scan**)\n• Bảng Top POI + **QR Scan per POI**\n• Heatmap\n• Biểu đồ thời gian\n• App Listen Metrics (TTS + Audio)
deactivate Portal

note over Portal : <b>JS auto-reload mỗi 20 giây</b>\n→ gọi lại GET summary\n→ cập nhật KPI cards realtime

@enduml
```

### Activity Diagram — SD-08

```plantuml
@startuml SD08_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-08 Activity: Analytics Dashboard + User Online

start

partition "**MOBILE GỬI TRACE**" {
  :Client gửi POST api/analytics
  {event, poiId, deviceId};
  :Tạo fingerprint =
  MD5(event + poiId + deviceId);

  if (Cache HIT?) then (Trùng)
    :Bỏ qua (ignored: true);
  else (Mới)
    :SET cache[fingerprint] TTL=3min;
    :INSERT TraceLog;
    :SignalR: TraceLogged;
  endif
}

partition "**ADMIN DASHBOARD**" {
  :Admin mở /AnalyticsAdmin;
  :GET api/analytics/summary;
  :Tính Online Users:
  TraceLogs 90s gần nhất
  → distinct(deviceId);
  :GET topPois, heatmap, timeseries;
  :GET qr-scan-counts?top=50
  → QR Scan per POI;
  :GET app-listen-metrics
  → TTS + Audio counts;
  :Hiện KPI cards + biểu đồ
  (Online, Visitors, Events,
  QR Scan, App Listen);
  :JS auto-reload mỗi 20s;
}

stop

@enduml
```

---
## SD-09 — Sinh & Cache TTS Audio
**Mô tả:** Khi cần phát TTS → kiểm tra cache /tts-cache/{lang}/poi_{id}.mp3. Chưa có → gọi gTTS sinh MP3 → lưu cache + DB → trả URL. Admin có thể Generate All Languages TTS cho 1 POI (batch 10 ngôn ngữ).

```plantuml
@startuml SD09
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-09: Sinh & Cache TTS Audio</size></b>\n<size:13><i>Generate TTS → cache file → UPSERT DB → SignalR</i></size>

actor "Caller\n(Admin/App/QR)" as Caller #88CCEE
participant "API\n(AudioController)" as API #DDAA33
participant "TTS Engine\n(gTTS)" as TTS #44BB99
participant "File System\n(/tts-cache/)" as FS #EE8866
database "Database\n(AudioFiles)" as DB #BBCC33
participant "SyncHub\n(SignalR)" as Hub #EE6677

== <size:14><b>SINH TTS CHO 1 NGÔN NGỮ</b></size> ==

Caller -> API : **POST** api/audio/tts\n{text, lang:"vi", poiId:5}
activate API
API -> API : Validate: text không rỗng,\ntext.Length <= MaxTtsTextLength
API -> FS : Kiểm tra file\n/tts-cache/vi/poi_5.mp3 tồn tại?
activate FS

alt #D6FFD6 Đã có cache
  FS --> API : File exists ✅
  deactivate FS
  API --> Caller : **200** {url: "/tts-cache/vi/poi_5.mp3",\ncached: true}
else #FFF0D6 Chưa có → Generate mới
  FS --> API : Not found
  deactivate FS
  API -> TTS : **Generate** MP3\n(text, language="vi")
  activate TTS
  TTS --> API : MP3 binary
  deactivate TTS
  API -> FS : Save → /tts-cache/vi/poi_5.mp3
  activate FS
  FS --> API : OK
  deactivate FS
  API -> DB : UPSERT **AudioFiles**\n{poiId:5, lang:"vi", url, isTts:true}
  activate DB
  DB --> API : audioId
  deactivate DB
  API -> Hub : SendAsync(**"AudioUploaded"**, audio)
  activate Hub
  Hub --> Hub : Broadcast
  deactivate Hub
  API --> Caller : **200** {url: "/tts-cache/vi/poi_5.mp3",\ncached: false}
end
deactivate API

== <size:14><b>BATCH: SINH TTS TẤT CẢ NGÔN NGỮ</b></size> ==

Caller -> API : **POST** api/audio/tts/generate-all/{poiId}
activate API
API -> DB : SELECT **PointContents**\nWHERE poiId (tất cả ngôn ngữ)
activate DB
DB --> API : Contents (vi, en, fr, ja, ko, zh...)
deactivate DB

loop <b>Mỗi ngôn ngữ có content</b>
  API -> TTS : Generate MP3 (text, lang)
  activate TTS
  TTS --> API : MP3 binary
  deactivate TTS
  API -> FS : Save → /tts-cache/{lang}/poi_{id}.mp3
  API -> DB : UPSERT **AudioFiles**
end

API -> Hub : SendAsync(**"AudioUploaded"**,\n{poiId, isBulk: true})
API --> Caller : **200** {generated: N, skipped: M}
deactivate API

@enduml
```

### Activity Diagram — SD-09

```plantuml
@startuml SD09_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-09 Activity: Sinh & Cache TTS Audio

start

:POST api/audio/tts
{text, lang, poiId};

:Validate text;

if (File /tts-cache/{lang}/poi_{id}.mp3
tồn tại?) then (Có)
  :Trả URL cached;
else (Chưa có)
  :gTTS Generate MP3;
  :Save → /tts-cache/;
  :UPSERT AudioFiles;
  :SignalR: AudioUploaded;
  :Trả URL mới;
endif

stop

@enduml
```

---
## SD-10 — Rebuild & Warmup Localization
**Mô tả:** prepare-hotset dịch sẵn cụm POI sang ngôn ngữ chỉ định. Warmup tải tất cả bản dịch vào MemoryCache. On-demand dịch 1 text cụ thể. Tất cả dùng AiController → LLM.

```plantuml
@startuml SD10
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-10: Rebuild & Warmup Localization</size></b>\n<size:13><i>Prepare hotset → Warmup cache → On-demand translate</i></size>

actor "Admin" as Admin #88CCEE
participant "AdminPortal" as Portal #DDAA33
participant "API\n(LocalizationController)" as Loc #44BB99
participant "API\n(AiController)" as AI #EE8866
participant "LLM\n(OpenAI / Gemini)" as LLM #BBCC33
database "Database\n(PointContents)" as DB #EE6677
participant "In-Memory\nCache" as Cache #CC6699

== <size:14><b>PREPARE HOTSET</b></size> ==

Admin -> Loc : **POST** api/localization/prepare-hotset\n{targetLang:"ja", poiIds:[1,2,5,8]}
activate Loc

loop <b>Mỗi POI trong danh sách</b>
  Loc -> DB : SELECT Content WHERE poiId, lang="vi"
  activate DB
  DB --> Loc : Source content
  deactivate DB
  Loc -> AI : **POST** ai/translate-content\n{poiId, targetLang:"ja"}
  activate AI
  AI -> LLM : Translate VI → JA
  activate LLM
  LLM --> AI : Bản dịch tiếng Nhật
  deactivate LLM
  AI --> Loc : Translated content
  deactivate AI
  Loc -> DB : UPSERT **PointContents** (lang="ja")
end

Loc --> Admin : **200** {prepared: 4}
deactivate Loc

== <size:14><b>WARMUP (TẢI VÀO CACHE)</b></size> ==

Admin -> Loc : **POST** api/localization/warmup\n{lang:"ja"}
activate Loc
Loc -> DB : SELECT tất cả PointContents\nWHERE lang="ja"
activate DB
DB --> Loc : List contents
deactivate DB
Loc -> Cache : Load toàn bộ vào MemoryCache\n(key: "loc:ja:{poiId}")
activate Cache
Cache --> Loc : Cached ✅
deactivate Cache
Loc --> Admin : **200** {warmedUp: N, lang:"ja"}
deactivate Loc

== <size:14><b>ON-DEMAND (DỊCH 1 TEXT)</b></size> ==

Admin -> Loc : **POST** api/localization/on-demand\n{text:"Bánh mì", targetLang:"en"}
activate Loc
Loc -> Cache : GET cache["Bánh mì:en"]
activate Cache

alt #D6FFD6 Cache HIT
  Cache --> Loc : "Bread"
  deactivate Cache
  Loc --> Admin : {translated: "Bread", cached: true}
else #FFF0D6 Cache MISS
  Cache --> Loc : null
  deactivate Cache
  Loc -> AI : translate-content
  activate AI
  AI -> LLM : Translate "Bánh mì" → EN
  activate LLM
  LLM --> AI : "Bread"
  deactivate LLM
  AI --> Loc : "Bread"
  deactivate AI
  Loc -> Cache : SET cache["Bánh mì:en"] = "Bread"
  Loc --> Admin : {translated: "Bread", cached: false}
end
deactivate Loc

@enduml
```

### Activity Diagram — SD-10

```plantuml
@startuml SD10_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-10 Activity: Rebuild & Warmup Localization

start

partition "**PREPARE HOTSET**" {
  :Chọn ngôn ngữ + danh sách POI;
  repeat
    :SELECT content VI cho POI;
    :AI translate VI → target;
    :UPSERT PointContents;
  repeat while (Còn POI?) is (Có)
}

partition "**WARMUP**" {
  :POST warmup {lang};
  :SELECT tất cả content cho lang;
  :Load vào MemoryCache;
}

partition "**ON-DEMAND**" {
  :POST on-demand {text, targetLang};
  if (Cache HIT?) then (Có)
    :Trả bản dịch từ cache;
  else (Không)
    :AI translate;
    :SET cache;
    :Trả bản dịch mới;
  endif
}

stop

@enduml
```

---
## SD-11 — Anti-Spam Trace Analytics
**Mô tả:** Mỗi event từ mobile/web qua bộ lọc anti-spam: tạo fingerprint MD5(event+poiId+deviceId), kiểm tra MemoryCache (TTL 3 phút). Trùng → bỏ. Mới → lưu TraceLog + broadcast SignalR "TraceLogged".

```plantuml
@startuml SD11
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-11: Anti-Spam Trace Analytics</size></b>\n<size:13><i>Fingerprint → MemoryCache check → lưu hoặc bỏ qua</i></size>

participant "Client\n(Mobile / Web)" as Client #88CCEE
participant "API\n(AnalyticsController)" as API #DDAA33
participant "MemoryCache\n(Fingerprint Store)" as Cache #44BB99
database "TraceLogs\n(Database)" as DB #EE8866
participant "SyncHub\n(SignalR)" as Hub #EE6677

Client -> API : **POST** api/analytics\n{event:"poi_enter", poiId:5,\ndeviceId:"abc123", extraJson}
activate API

API -> API : Tạo **fingerprint** =\nMD5(event + poiId + deviceId)
API -> Cache : GET cache[fingerprint]
activate Cache

alt #FFD6D6 Duplicate (đã gửi < 3 phút trước)
  Cache --> API : EXISTS ⚠️
  deactivate Cache
  API --> Client : **200 OK**\n{ignored: true,\nreason: "exact_duplicate"}

else #D6FFD6 Event mới
  Cache --> API : NOT FOUND
  deactivate Cache
  API -> Cache : SET cache[fingerprint]\n**TTL = 3 phút**
  activate Cache
  Cache --> API : Stored
  deactivate Cache

  API -> API : Lookup PoiName (nếu poiId > 0)
  API -> DB : INSERT **TraceLog**\n{PoiId, DeviceId, TimestampUtc,\nExtraJson, SessionId}
  activate DB
  DB --> API : OK
  deactivate DB

  API -> Hub : SendAsync(**"TraceLogged"**, {\nPoiId, PoiName, DeviceId,\nTimestampUtc, Extra})
  activate Hub
  Hub --> Hub : Broadcast → Admin Dashboard
  deactivate Hub

  API --> Client : **200 OK**\n{ignored: false, reason: "new_event"}
end
deactivate API

@enduml
```

### Activity Diagram — SD-11

```plantuml
@startuml SD11_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-11 Activity: Anti-Spam Trace Analytics

start

:POST api/analytics
{event, poiId, deviceId};

:Tạo fingerprint =
MD5(event + poiId + deviceId);

:GET cache[fingerprint];

if (Cache HIT?) then (Trùng < 3 phút)
  :Trả {ignored: true,
  reason: exact_duplicate};
  stop
else (Mới)
endif

:SET cache[fingerprint]
TTL = 3 phút;

:Lookup PoiName;

:INSERT TraceLog;

:SignalR: TraceLogged;

:Trả {ignored: false};

stop

@enduml
```

---
## SD-12 — Đánh giá POI từ Du khách (Reviews)
**Mô tả:** Du khách gửi review (rating + comment) từ mobile → API lưu PoiReviews (IsHidden=false). App chỉ hiện review visible. Admin có thể ẩn/hiện/xóa review qua AdminPortal.

```plantuml
@startuml SD12
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-12: Đánh giá POI từ Du khách (Reviews)</size></b>\n<size:13><i>Mobile gửi review → API lưu → Admin quản lý ẩn/xóa</i></size>

actor "Du khách" as User #88CCEE
participant "Mobile App\n(MapPage)" as App #DDAA33
participant "API\n(PoiReviewsController)" as API #44BB99
database "Database\n(PoiReviews)" as DB #EE8866
actor "Admin" as Admin #BBCC33
participant "AdminPortal\n(PoiAdmin/Details)" as Portal #EE6677

== <size:14><b>DU KHÁCH GỬI ĐÁNH GIÁ</b></size> ==

User -> App : Mở chi tiết POI\n→ chấm sao + viết bình luận
App -> API : **POST** api/poi-reviews\n{poiId:5, rating:4,\ncomment:"Rất ngon!", reviewerName:"Khách"}
activate API
API -> DB : INSERT **PoiReviews**\n(IsHidden = false)
activate DB
DB --> API : reviewId
deactivate DB
API --> App : **201 Created**
deactivate API
App --> User : ✅ Đã gửi đánh giá

== <size:14><b>HIỂN THỊ REVIEWS TRÊN APP</b></size> ==

App -> API : **GET** api/poi-reviews/{poiId}
activate API
API -> DB : SELECT WHERE poiId=5\nAND **IsHidden = false**
activate DB
DB --> API : List<Review> (chỉ visible)
deactivate DB
API --> App : Reviews
deactivate API
App --> User : ⭐ Danh sách đánh giá

== <size:14><b>ADMIN QUẢN LÝ REVIEWS</b></size> ==

Admin -> Portal : Mở **/PoiAdmin/Details/{poiId}**
activate Portal
Portal -> API : **GET** api/poi-reviews/{poiId}/**admin**\n(X-API-Key: admin123)
activate API
API -> DB : SELECT ALL\n(bao gồm hidden)
activate DB
DB --> API : Tất cả reviews
deactivate DB
API --> Portal : Full review list
deactivate API
Portal --> Admin : 📋 Reviews + nút Ẩn/Xóa

Admin -> Portal : Bấm "Ẩn" review #7
Portal -> API : **POST** api/poi-reviews/7/**toggle-hidden**
activate API
API -> DB : UPDATE **IsHidden** = !IsHidden
activate DB
DB --> API : OK
deactivate DB
API --> Portal : **200 OK**
deactivate API

Admin -> Portal : Bấm "Xóa" review #8
Portal -> API : **DELETE** api/poi-reviews/8
activate API
API -> DB : DELETE WHERE Id = 8
activate DB
DB --> API : OK
deactivate DB
API --> Portal : **200 OK**
deactivate API
Portal --> Admin : ✅ Đã xử lý
deactivate Portal

@enduml
```

### Activity Diagram — SD-12

```plantuml
@startuml SD12_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-12 Activity: Đánh giá POI (Reviews)

start

partition "**DU KHÁCH GỬI**" {
  :Chấm sao + viết bình luận;
  :POST api/poi-reviews
  {poiId, rating, comment};
  :INSERT PoiReviews (IsHidden=false);
}

partition "**HIỂN THỊ**" {
  :GET api/poi-reviews/{poiId};
  :SELECT WHERE IsHidden=false;
  :Hiện danh sách reviews;
}

partition "**ADMIN QUẢN LÝ**" {
  :GET poi-reviews/{poiId}/admin;
  :SELECT ALL (gồm hidden);

  if (Thao tác?) then (Ẩn/Hiện)
    :POST toggle-hidden;
    :UPDATE IsHidden = !IsHidden;
  else (Xóa)
    :DELETE api/poi-reviews/{id};
  endif
}

stop

@enduml
```

---
## SD-13 — Admin Quản lý User & Duyệt Owner
**Mô tả:** Admin xem danh sách owner đăng ký (pending) → duyệt (isVerified=true) hoặc từ chối (rejected). Admin cũng xem/toggle verified/cập nhật thông tin user.

```plantuml
@startuml SD13
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-13: Admin Quản lý User & Duyệt Owner</size></b>\n<size:13><i>Xem pending → Approve/Reject → Toggle verified</i></size>

actor "Admin" as Admin #88CCEE
participant "AdminPortal\n(AdminRegistrations\n+ AdminOwners)" as Portal #DDAA33
participant "API\n(OwnerRegistration\nController)" as RegAPI #44BB99
participant "API\n(AdminUsers\nController)" as UserAPI #EE8866
database "Database\n(Users +\nOwnerRegistrations)" as DB #BBCC33

== <size:14><b>XEM ĐĂNG KÝ CHỜ DUYỆT</b></size> ==

Admin -> Portal : Mở **/AdminRegistrations**
activate Portal
Portal -> RegAPI : **GET** api/owner-admin/pending
activate RegAPI
RegAPI -> DB : SELECT OwnerRegistrations\nWHERE status="pending"
activate DB
DB --> RegAPI : List<OwnerRegistration>
deactivate DB
RegAPI --> Portal : Danh sách chờ duyệt
deactivate RegAPI
Portal --> Admin : 📋 Bảng: email, shopName,\nshopAddress, submittedAt

== <size:14><b>DUYỆT OWNER</b></size> ==

Admin -> Portal : Bấm "Duyệt" owner #3
Portal -> RegAPI : **POST** api/owner-admin/3/**approve**\n{notes:"OK", reviewedBy:"admin"}
activate RegAPI
RegAPI -> DB : UPDATE OwnerRegistrations\nSET status="**approved**"
activate DB
DB --> RegAPI : OK
deactivate DB
RegAPI -> DB : UPDATE Users\nSET isVerified=**true**
activate DB
DB --> RegAPI : OK
deactivate DB
RegAPI --> Portal : **200 OK**
deactivate RegAPI
Portal --> Admin : ✅ Đã duyệt

== <size:14><b>TỪ CHỐI OWNER</b></size> ==

Admin -> Portal : Bấm "Từ chối" owner #4
Portal -> RegAPI : **POST** api/owner-admin/4/**reject**\n{notes:"Thiếu giấy tờ"}
activate RegAPI
RegAPI -> DB : UPDATE status="**rejected**"
activate DB
DB --> RegAPI : OK
deactivate DB
RegAPI --> Portal : **200 OK**
deactivate RegAPI
Portal --> Admin : ❌ Đã từ chối

== <size:14><b>QUẢN LÝ USERS</b></size> ==

Admin -> Portal : Mở **/AdminOwners**
activate Portal
Portal -> UserAPI : **GET** admin/users
activate UserAPI
UserAPI -> DB : SELECT Users\nLEFT JOIN OwnerRegistrations
activate DB
DB --> UserAPI : List<User + Registration>
deactivate DB
UserAPI --> Portal : Danh sách users
deactivate UserAPI
Portal --> Admin : 📋 Bảng users

Admin -> Portal : Toggle verified user #5
Portal -> UserAPI : **POST** admin/users/5/**toggle-verified**
activate UserAPI
UserAPI -> DB : UPDATE isVerified = !isVerified
activate DB
DB --> UserAPI : OK
deactivate DB
UserAPI --> Portal : **200 OK**
deactivate UserAPI
deactivate Portal

@enduml
```

### Activity Diagram — SD-13

```plantuml
@startuml SD13_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-13 Activity: Admin Quản lý User & Duyệt Owner

start

partition "**XEM PENDING**" {
  :Admin mở /AdminRegistrations;
  :GET api/owner-admin/pending;
  :Hiện bảng: email, shopName,
  shopAddress, submittedAt;
}

partition "**DUYỆT / TỪ CHỐI**" {
  if (Admin quyết định?) then (Từ chối)
    :POST /{id}/reject {notes};
    :UPDATE status=rejected;
    stop
  else (Duyệt)
  endif

  :POST /{id}/approve;
  :UPDATE status=approved;
  :UPDATE isVerified=true;
}

partition "**QUẢN LÝ USERS**" {
  :Admin mở /AdminOwners;
  :GET admin/users;
  :Hiện bảng users;

  :Toggle verified user;
  :POST admin/users/{id}/toggle-verified;
  :UPDATE isVerified = !isVerified;
}

stop

@enduml
```
