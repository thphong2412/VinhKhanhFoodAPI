# PART 3: SD-14 → SD-21 (Sửa tên + Mới hoàn toàn)
# Copy từng block @startuml...@enduml vào https://www.plantuml.com/plantuml/uml

---
## SD-14 (NEW) — Nghe Audio: TTS "Nghe ngay" + MP3 picker → Mini Player
**Mô tả:** Du khách bấm "Nghe ngay" → load TTS description → armed mode (popup mở, chưa phát) → bấm Play → AudioQueue → MiniPlayer. Hoặc bấm "Audio" → API load danh sách MP3 → chọn file → Enqueue → Play. Mini Player cho phép pause/resume/stop.

```plantuml
@startuml SD14
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-14: Nghe Audio — TTS + MP3 picker → Mini Player</size></b>\n<size:13><i>"Nghe ngay" TTS armed → Play | "Audio" MP3 list → chọn file → Play</i></size>

actor "Du khách" as User #88CCEE
participant "Mobile App\n(MapPage.NgheAudio)" as App #DDAA33
participant "Mobile App\n(MapPage.MiniPlayer)" as MP #44BB99
participant "API\n(AudioController)" as AudioAPI #EE8866
participant "API\n(ContentController)" as ContentAPI #BBCC33
participant "AudioQueue\nService" as AQ #EE6677
participant "IAudioService\n(Platform)" as Audio #CC6699

== <size:14><b>LUỒNG 1: "NGHE NGAY" (TTS)</b></size> ==

User -> App : Bấm nút **"Nghe ngay"**
activate App
App -> App : Lấy _selectedPoi
App -> ContentAPI : GetContentForLanguageAsync\n(poiId, _currentLanguage)
activate ContentAPI
ContentAPI --> App : Content (description text)
deactivate ContentAPI

alt #FFD6D6 Không có description
  App --> User : ❌ "Không có thuyết minh\ncho ngôn ngữ này"
else #D6FFD6 Có text
  App -> App : HideAudioListPopup()
  App -> MP : **ShowMiniPlayerArmedAsync**(title, isTts:true)\n→ Popup mở, trạng thái "armed"\n→ Nút Play hiện, chưa phát
  activate MP
  MP --> User : 🔇 MiniPlayer hiện (armed, chưa phát)

  User -> MP : Bấm nút **▶ Play**
  MP -> MP : OnMiniPlayerPlayPauseClicked()\n→ Phát hiện _isArmed = true
  MP -> App : Gọi callback **PlayNarration**(text)
  App -> AQ : Enqueue(audioItem)\n{key: "tts:{poiId}:{lang}",\nisTts: true, text}
  activate AQ
  AQ -> AudioAPI : **POST** api/audio/tts\n{text, lang, poiId}
  activate AudioAPI
  AudioAPI --> AQ : {url: "/tts-cache/{lang}/poi_{id}.mp3"}
  deactivate AudioAPI
  AQ -> Audio : **PlayAsync**(mp3Url)
  activate Audio
  Audio --> MP : 🔊 Đang phát TTS
  deactivate Audio
  deactivate AQ
  MP -> MP : _isArmed = false\n→ Cập nhật UI: slider + thời gian
  MP --> User : 🔊 MiniPlayer đang phát
  deactivate MP
end
deactivate App

== <size:14><b>LUỒNG 2: "AUDIO" (MP3 LIST)</b></size> ==

User -> App : Bấm nút **"Audio"**
activate App
App -> AudioAPI : **GET** api/audio/by-poi/{poiId}
activate AudioAPI
AudioAPI --> App : List<AudioModel>
deactivate AudioAPI

App -> App : SelectAudioListByLanguage()\nLọc file MP3 (IsTts=false)\ntheo ngôn ngữ ưu tiên + fallback

alt #FFD6D6 Không có file MP3
  App --> User : ❌ "Không có file audio\ncho ngôn ngữ này"
else #D6FFD6 Có file → Hiện popup danh sách
  App -> App : AudioListItems.Clear()\n+ Add items (DisplayName, LanguageLabel)
  App -> App : Hiện **AudioListPopup**
  App --> User : 📋 Popup danh sách file MP3

  User -> App : Bấm **▶ Play** trên file #3
  App -> App : OnAudioListItemPlayClicked()
  App -> App : HideAudioListPopup()
  App -> AQ : **Enqueue**(audioItem)\n{key: "mp3:{poiId}:{lang}:{audioId}",\nisTts: false, filePath: playUrl}
  activate AQ
  AQ -> Audio : **PlayAsync**(mp3Url)
  activate Audio
  Audio --> App : 🔊 Đang phát MP3
  deactivate Audio
  deactivate AQ
  App -> MP : **ShowMiniPlayerAsync**(title, isTts:false)
  activate MP
  MP --> User : 🔊 MiniPlayer đang phát MP3
  deactivate MP
end
deactivate App

== <size:14><b>MINI PLAYER CONTROLS</b></size> ==

User -> MP : Bấm **⏸ Pause**
activate MP
MP -> Audio : PauseAsync()
MP --> User : ⏸ Tạm dừng

User -> MP : Bấm **▶ Resume**
MP -> Audio : ResumeAsync()
MP --> User : 🔊 Tiếp tục phát

User -> MP : Bấm **✕ Đóng**
MP -> Audio : StopAsync()
MP -> MP : HideMiniPlayer()
MP --> User : MiniPlayer đã đóng
deactivate MP

@enduml
```

### Activity Diagram — SD-14

```plantuml
@startuml SD14_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-14 Activity: Nghe Audio (TTS + MP3)

start

if (Nút nào?) then ("Nghe ngay" TTS)
  :Lấy content description
  theo ngôn ngữ hiện tại;

  if (Có description?) then (Không)
    :Hiển thị lỗi;
    stop
  else (Có)
  endif

  :ShowMiniPlayerArmed
  (popup mở, chưa phát);

  :User bấm Play;
  :AudioQueue.Enqueue(ttsItem);
  :POST api/audio/tts → URL;
  :PlayAsync(mp3Url);
  :MiniPlayer đang phát;

else ("Audio" MP3 list)
  :GET api/audio/by-poi/{poiId};
  :Lọc MP3 theo ngôn ngữ;

  if (Có file MP3?) then (Không)
    :Hiển thị lỗi;
    stop
  else (Có)
  endif

  :Hiện AudioListPopup;
  :User chọn file → Play;
  :AudioQueue.Enqueue(mp3Item);
  :PlayAsync(mp3Url);
  :MiniPlayer đang phát;
endif

:Pause / Resume / Stop;

stop

@enduml
```

---
## SD-15 — Owner Upload Ảnh & Submit Tạo POI Mới (→ chờ duyệt)
**Mô tả:** Owner mở CreatePoi trên OwnerPortal → điền thông tin + upload ảnh → API upload file → tạo PoiRegistration (type=create, pending) → Owner chờ Admin duyệt (xem SD-18).

```plantuml
@startuml SD15
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-15: Owner Upload Ảnh & Submit Tạo POI</size></b>\n<size:13><i>Owner tạo POI + upload ảnh → PoiRegistration pending → chờ duyệt</i></size>

actor "Owner\n(Chủ quán)" as Owner #88CCEE
participant "OwnerPortal\n(CreatePoi.cshtml)" as OP #DDAA33
participant "API\n(PoiRegistration\nController)" as RegAPI #44BB99
participant "API\n(File Upload)" as Upload #EE8866
participant "File System\n(/uploads/)" as FS #BBCC33
database "Database\n(PoiRegistrations)" as DB #EE6677

Owner -> OP : Mở **/CreatePoi**\nĐiền: Tên, Danh mục, Toạ độ,\nBán kính, Mô tả, Giá, SĐT...
activate OP

opt <b>Có ảnh đại diện</b>
  Owner -> OP : Chọn file ảnh
  OP -> Upload : **POST** api/poiregistration/upload-image\n(multipart/form-data)
  activate Upload
  Upload -> Upload : Validate: image/*, < 10MB
  Upload -> FS : Save → /uploads/poi-images/{guid}.jpg
  activate FS
  FS --> Upload : filePath
  deactivate FS
  Upload --> OP : **200** {imageUrl: "/uploads/poi-images/{guid}.jpg"}
  deactivate Upload
end

Owner -> OP : Bấm **"Gửi đăng ký"**
OP -> RegAPI : **POST** api/poiregistration/submit\n{name, category, lat, lng, radius,\nimageUrl, contentTitle, contentDescription,\ncontentPriceMin, contentPhoneNumber,\ncontentAddress, ownerId...}
activate RegAPI
RegAPI -> RegAPI : Validate: tên không rỗng,\ntoạ độ hợp lệ
RegAPI -> DB : INSERT **PoiRegistrations**\n(requestType="create",\nstatus="pending", ownerId)
activate DB
DB --> RegAPI : registrationId
deactivate DB
RegAPI --> OP : **200** {success: true, id: registrationId}
deactivate RegAPI
OP --> Owner : ✅ "Đã gửi yêu cầu tạo POI"\n"Vui lòng chờ admin duyệt"
deactivate OP

note over Owner : Owner theo dõi trạng thái\ntrên trang /MyPois\n\nAdmin duyệt tại SD-18

@enduml
```

### Activity Diagram — SD-15

```plantuml
@startuml SD15_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-15 Activity: Owner Upload Ảnh & Submit Tạo POI

start

:Owner mở /CreatePoi;

:Điền: Tên, Danh mục, Toạ độ,
Bán kính, Mô tả, Giá, SĐT...;

if (Có ảnh?) then (Có)
  :POST upload-image
  (multipart/form-data);
  :Validate image, save file;
  :Nhận imageUrl;
else (Không)
endif

:POST poiregistration/submit;

:Validate dữ liệu;

:INSERT PoiRegistrations
(type=create, pending);

:Hiển thị: Chờ admin duyệt;

stop

@enduml
```

---
## SD-16 (NEW) — App khởi động → Load bản đồ POI
**Mô tả:** Du khách mở app → MapPage.OnAppearing() → load POI từ SQLite local → render pin lên bản đồ → kết nối SignalR → sync từ API nếu DB rỗng → khởi động GPS → hiện highlights.

```plantuml
@startuml SD16
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-16: App khởi động → Load bản đồ POI</size></b>\n<size:13><i>OnAppearing → SQLite → SignalR → API sync → render map → GPS</i></size>

actor "Du khách" as User #88CCEE
participant "Mobile App\n(MapPage.Lifecycle)" as App #DDAA33
participant "DatabaseService\n(SQLite local)" as Local #44BB99
participant "RealtimeSync\nManager" as Sync #EE8866
participant "SignalRSync\nService" as SR #BBCC33
participant "API\n(PoiController)" as API #EE6677
participant "LocationPolling\nService" as GPS #CC6699

User -> App : Mở ứng dụng
activate App
App -> App : **OnAppearing()**\n→ InitializeOnAppearingAsync()

== <size:14><b>BƯỚC 1: KẾT NỐI SIGNALR</b></size> ==

App -> Sync : EnsureRealtimeSyncSubscriptions()
activate Sync
Sync --> App : Subscribed events:\nPoiDataChanged, ContentDataChanged,\nAudioDataChanged, FullSyncRequested
deactivate Sync

App -> SR : **ConnectForDeviceAsync()**\n→ WebSocket tới /sync-hub
activate SR
SR --> App : ✅ Connected
deactivate SR

== <size:14><b>BƯỚC 2: LOAD DỮ LIỆU LOCAL</b></size> ==

App -> Local : **GetPoisAsync()**
activate Local
Local --> App : List<PoiModel> (từ SQLite)
deactivate Local

alt #FFF0D6 Không có dữ liệu local (DB rỗng)
  App -> API : **GET** api/poi/load-all?lang=vi\n(RunSingleFullSyncAndApplyUiAsync)
  activate API
  API --> App : List<PoiModel> từ server
  deactivate API
  App -> Local : **SavePoisAsync**(list)
  activate Local
  Local --> App : OK
  deactivate Local
else #D6FFD6 Có dữ liệu local
  App -> App : Dùng dữ liệu local ngay\n(không chờ API → tránh ANR)
end

== <size:14><b>BƯỚC 3: RENDER BẢN ĐỒ</b></size> ==

App -> App : **AddPoisToMap()**\n(tạo Pin cho mỗi POI published)
App -> App : vinhKhanhMap.IsVisible = true
App --> User : 🗺️ Bản đồ hiện các pin POI

== <size:14><b>BƯỚC 4: DEFERRED WORK (BACKGROUND)</b></size> ==

App -> App : RunDeferredStartupWorkAsync()
App -> App : **RenderHighlightsAsync**(top 6 POI)
App -> App : **DisplayAllPois**()\n(localized pin labels)
App -> App : GeofenceEngine.**UpdatePois**(_pois)

== <size:14><b>BƯỚC 5: KHỞI ĐỘNG GPS</b></size> ==

App -> GPS : **EnsureTrackingStartedAsync()**
activate GPS
GPS -> GPS : StartAsync()\n→ bắt đầu polling GPS mỗi 5-10s
deactivate GPS

== <size:14><b>BƯỚC 6: CENTER BẢN ĐỒ</b></size> ==

App -> App : **CenterMapOnUserFirstAsync()**\n→ GetLastKnownLocation\n→ MoveToRegion(userLocation)
App --> User : 📍 Bản đồ center vào vị trí user
deactivate App

@enduml
```

### Activity Diagram — SD-16

```plantuml
@startuml SD16_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-16 Activity: App khởi động → Load bản đồ

start

:OnAppearing();

:Kết nối SignalR
(ConnectForDeviceAsync);

:GetPoisAsync() từ SQLite;

if (Có dữ liệu local?) then (Không)
  :GET api/poi/load-all;
  :SavePoisAsync vào SQLite;
else (Có)
  :Dùng dữ liệu local;
endif

:AddPoisToMap()
(render pins);

:Hiện bản đồ;

fork
  :RenderHighlightsAsync
  (top 6 POI);
fork again
  :DisplayAllPois
  (localized labels);
fork again
  :GeofenceEngine.UpdatePois;
end fork

:EnsureTrackingStartedAsync
(GPS polling);

:CenterMapOnUserFirstAsync;

stop

@enduml
```

---
## SD-17 (NEW) — Bấm pin → Xem chi tiết POI + Đổi ngôn ngữ
**Mô tả:** Du khách bấm pin → ShowPoiDetail → load content local trước → hydrate từ API (content, audio, reviews) ở background → render card. Đổi ngôn ngữ → reload content cho cùng POI.

```plantuml
@startuml SD17
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-17: Bấm pin → Xem chi tiết POI + Đổi ngôn ngữ</size></b>\n<size:13><i>Tap pin → local content → API hydrate → render card → switch lang</i></size>

actor "Du khách" as User #88CCEE
participant "Mobile App\n(MapPage)" as App #DDAA33
participant "DatabaseService\n(SQLite)" as Local #44BB99
participant "API\n(PoiController)" as PoiAPI #EE8866
participant "API\n(ContentController)" as ContentAPI #BBCC33
participant "API\n(AudioController)" as AudioAPI #EE6677
participant "API\n(PoiReviews)" as ReviewAPI #CC6699

User -> App : Bấm pin POI trên bản đồ
activate App
App -> App : OnPinClicked → **SelectPoi**(poi)

== <size:14><b>HIỆN NHANH TỪ LOCAL (INSTANT)</b></size> ==

App -> Local : **GetContentByPoiIdAsync**(poiId, lang)
activate Local
Local --> App : Content local (nếu có cache)
deactivate Local
App -> App : LblPoiName.Text = title\nHiện **PoiDetailPanel**
App --> User : 📋 Card chi tiết (nhanh, từ cache)

== <size:14><b>HYDRATE TỪ API (BACKGROUND)</b></size> ==

App -> PoiAPI : **GET** api/poi/load-all (hydrate full data)
activate PoiAPI
PoiAPI --> App : Full POI data
deactivate PoiAPI

App -> ContentAPI : **GET** api/content/by-poi/{poiId}
activate ContentAPI
ContentAPI --> App : List<Content> (tất cả ngôn ngữ)
deactivate ContentAPI
App -> Local : Cache content vào SQLite

App -> AudioAPI : **GET** api/audio/by-poi/{poiId}
activate AudioAPI
AudioAPI --> App : List<AudioModel>
deactivate AudioAPI

App -> ReviewAPI : **GET** api/poi-reviews/{poiId}
activate ReviewAPI
ReviewAPI --> App : List<Review>
deactivate ReviewAPI

App -> App : Cập nhật UI đầy đủ:\n• Mô tả, địa chỉ, SĐT\n• Giờ mở/đóng, giá\n• Category, Radius, Priority\n• Rating, số review\n• Nút: Nghe ngay, Audio,\n  Lưu, Chia sẻ, Dẫn đường
App --> User : 📱 Card đầy đủ thông tin

== <size:14><b>ĐỔI NGÔN NGỮ</b></size> ==

User -> App : Chọn ngôn ngữ mới (EN, JA...)
App -> App : _currentLanguage = "en"
App -> Local : **GetContentByPoiIdAsync**(poiId, "en")
activate Local

alt #D6FFD6 Có bản dịch sẵn trong cache
  Local --> App : Content tiếng Anh
  deactivate Local
else #FFF0D6 Chưa có → gọi API
  Local --> App : null
  deactivate Local
  App -> ContentAPI : **GET** api/content/by-poi/{poiId}
  activate ContentAPI
  ContentAPI --> App : Content (lọc lang="en")
  deactivate ContentAPI
end

App -> App : Reload UI với ngôn ngữ mới\n(title, description, address...)
App --> User : 🌐 Nội dung đã chuyển sang English
deactivate App

@enduml
```

### Activity Diagram — SD-17

```plantuml
@startuml SD17_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-17 Activity: Bấm pin → Chi tiết POI + Đổi ngôn ngữ

start

:Bấm pin trên bản đồ;
:SelectPoi(poi);

partition "**HIỆN NHANH (LOCAL)**" {
  :GetContentByPoiIdAsync
  (poiId, currentLang);
  :Hiện PoiDetailPanel
  (từ cache local);
}

partition "**HYDRATE (API)**" {
  :GET api/content/by-poi/{poiId};
  :GET api/audio/by-poi/{poiId};
  :GET api/poi-reviews/{poiId};
  :Cache content vào SQLite;
  :Cập nhật UI đầy đủ;
}

if (Đổi ngôn ngữ?) then (Có)
  :_currentLanguage = newLang;
  if (Có bản dịch local?) then (Có)
    :Dùng cache;
  else (Không)
    :GET content từ API;
  endif
  :Reload UI ngôn ngữ mới;
else (Không)
endif

stop

@enduml
```

---
## SD-18 (NEW) — Admin Duyệt POI Submission (Create/Update/Delete)
**Mô tả:** Admin vào AdminPoiRegistrations/Pending → xem yêu cầu từ Owner → xem diff → Approve/Reject. Approve create → tạo POI thật + Content. Approve update → sửa POI. Approve delete → xóa POI.

```plantuml
@startuml SD18
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-18: Admin Duyệt POI Submission</size></b>\n<size:13><i>Admin xem pending → Approve create/update/delete hoặc Reject</i></size>

actor "Admin" as Admin #88CCEE
participant "AdminPortal\n(AdminPoiRegistrations)" as Portal #DDAA33
participant "API\n(PoiRegistration\nController)" as API #44BB99
database "Database\n(PoiRegistrations +\nPOI + Content + Audio)" as DB #EE8866

Admin -> Portal : Mở **/AdminPoiRegistrations/Pending**
activate Portal
Portal -> API : **GET** api/poiregistration/pending
activate API
API -> DB : SELECT WHERE status="pending"
activate DB
DB --> API : List<PoiRegistration>
deactivate DB
API --> Portal : Danh sách chờ duyệt
deactivate API
Portal --> Admin : 📋 Bảng: tên, loại\n(create/update/delete), owner, ngày gửi

Admin -> Portal : Bấm "Xem chi tiết" #7
Portal -> API : **GET** api/poiregistration/7
activate API
API --> Portal : Registration details + diff
deactivate API

alt #D6FFD6 Approve → requestType = "create"
  Admin -> Portal : Bấm **"Duyệt"**
  Portal -> API : **POST** api/poiregistration/7/**approve**\n{notes, reviewedBy}
  activate API
  API -> DB : INSERT **PointsOfInterest**\n(name, lat, lng, radius, imageUrl,\nisPublished=true, ownerId)
  activate DB
  DB --> API : New POI id
  deactivate DB
  API -> DB : INSERT **PointContents**\n(poiId, lang:"vi", title, description...)
  activate DB
  DB --> API : OK
  deactivate DB
  API -> DB : UPDATE PoiRegistrations\nstatus="**approved**", approvedPoiId
  API --> Portal : **200** {success: true, poiId}
  deactivate API
  Portal --> Admin : ✅ POI đã được tạo thành công

else #FFF0D6 Approve → requestType = "update"
  Admin -> Portal : Bấm **"Duyệt"**
  Portal -> API : **POST** api/poiregistration/7/**approve**
  activate API
  API -> DB : UPDATE **PointsOfInterest**\nSET name, category, lat, lng,\nradius, imageUrl...
  API -> DB : UPSERT **PointContents** (lang=vi)
  API -> DB : UPDATE status="**approved**"
  API --> Portal : **200** {success: true, poiId}
  deactivate API
  Portal --> Admin : ✅ POI đã cập nhật

else #FFD6D6 Approve → requestType = "delete"
  Admin -> Portal : Bấm **"Duyệt xóa"**
  Portal -> API : **POST** api/poiregistration/7/**approve**
  activate API
  API -> DB : DELETE **PointContents** WHERE poiId
  API -> DB : DELETE **AudioFiles** WHERE poiId
  API -> DB : DELETE **PointsOfInterest** WHERE id
  API -> DB : UPDATE status="**approved**"
  API --> Portal : **200** {success: true}
  deactivate API
  Portal --> Admin : ✅ POI đã bị xóa

else #FFD6D6 Reject
  Admin -> Portal : Bấm **"Từ chối"**
  Portal -> API : **POST** api/poiregistration/7/**reject**\n{notes: "Lý do từ chối"}
  activate API
  API -> DB : UPDATE status="**rejected**"
  API --> Portal : **200 OK**
  deactivate API
  Portal --> Admin : ❌ Đã từ chối
end
deactivate Portal

@enduml
```

### Activity Diagram — SD-18

```plantuml
@startuml SD18_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-18 Activity: Admin Duyệt POI Submission

start

:Admin mở Pending list;
:GET poiregistration/pending;
:Xem chi tiết + diff;

if (Admin quyết định?) then (Từ chối)
  :POST /{id}/reject;
  :UPDATE status=rejected;
  stop
else (Duyệt)
endif

if (requestType?) then (create)
  :INSERT PointsOfInterest;
  :INSERT PointContents;
else if (update) then
  :UPDATE PointsOfInterest;
  :UPSERT PointContents;
else (delete)
  :DELETE Content + Audio + POI;
endif

:UPDATE status=approved;

stop

@enduml
```

---
## SD-19 (NEW) — Realtime Sync SignalR
**Mô tả:** API Controllers broadcast events qua SyncHub → Mobile SignalRSyncService nhận → RealtimeSyncManager cập nhật SQLite → MapPage re-render. Admin JS nhận TraceLogged → reload KPI.

```plantuml
@startuml SD19
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-19: Realtime Sync SignalR</size></b>\n<size:13><i>API broadcast → SyncHub → Mobile update DB + UI → Admin reload KPI</i></size>

participant "API Controllers\n(Poi / Content /\nAudio / Analytics)" as API #88CCEE
participant "SyncHub\n(SignalR Server)" as Hub #DDAA33
participant "SignalRSync\nService (Mobile)" as SR #44BB99
participant "RealtimeSync\nManager" as RSM #EE8866
participant "DatabaseService\n(SQLite)" as Local #BBCC33
participant "MapPage\n(Mobile UI)" as App #EE6677
participant "Admin JS\n(Browser)" as AdminJS #CC6699

== <size:14><b>API THAY ĐỔI → BROADCAST</b></size> ==

API -> Hub : SendAsync(**"PoiAdded"**, poi)
API -> Hub : SendAsync(**"PoiUpdated"**, poi)
API -> Hub : SendAsync(**"PoiDeleted"**, poiId)
API -> Hub : SendAsync(**"ContentCreated"**, content)
API -> Hub : SendAsync(**"ContentUpdated"**, content)
API -> Hub : SendAsync(**"AudioUploaded"**, audio)
API -> Hub : SendAsync(**"AudioDeleted"**, audioId)
API -> Hub : SendAsync(**"TraceLogged"**, trace)
API -> Hub : SendAsync(**"RequestFullPoiSync"**)

== <size:14><b>MOBILE APP NHẬN POI EVENTS</b></size> ==

Hub --> SR : Event: **"PoiAdded"** / **"PoiUpdated"**
activate SR
SR -> RSM : OnPoiAdded(poi) / OnPoiUpdated(poi)
activate RSM
RSM -> Local : InsertOrUpdate POI vào SQLite
activate Local
Local --> RSM : OK
deactivate Local
RSM --> App : Event: **PoiDataChanged**(poi)
deactivate RSM
deactivate SR

activate App
App -> App : ScheduleRealtimeMapRefreshAsync()\n(debounce 1050ms + cooldown 3.6s)
App -> Local : **GetPoisAsync()** (reload toàn bộ)
activate Local
Local --> App : Updated list
deactivate Local
App -> App : **AddPoisToMap()** (re-render pins)
App -> App : **RenderHighlightsAsync()** (update cards)
deactivate App

Hub --> SR : Event: **"PoiDeleted"**
activate SR
SR -> RSM : OnPoiDeleted(poiId)
activate RSM
RSM -> Local : Delete POI khỏi SQLite
RSM --> App : PoiDataChanged(null)
deactivate RSM
deactivate SR

== <size:14><b>CONTENT / AUDIO EVENTS</b></size> ==

Hub --> SR : Event: **"ContentUpdated"**
activate SR
SR -> RSM : OnContentUpdated(content)
activate RSM
RSM -> Local : Update content SQLite
RSM --> App : **ContentDataChanged**(content)
deactivate RSM
deactivate SR
activate App
App -> App : Nếu đang xem POI này\n→ **ShowPoiDetail()** refresh card
deactivate App

Hub --> SR : Event: **"AudioUploaded"**
activate SR
SR -> RSM : OnAudioUploaded(audio)
RSM --> App : **AudioDataChanged**(audio)
deactivate SR

== <size:14><b>ADMIN WEB NHẬN EVENT</b></size> ==

Hub --> AdminJS : Event: **"TraceLogged"**
activate AdminJS
AdminJS -> AdminJS : JS: fetch GET api/analytics/summary\n→ Cập nhật KPI cards:\nOnline users, Total events,\nBảng realtime
deactivate AdminJS

@enduml
```

### Activity Diagram — SD-19

```plantuml
@startuml SD19_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-19 Activity: Realtime Sync SignalR

start

:API thay đổi dữ liệu
(POI / Content / Audio);

:SyncHub broadcast event;

fork
  partition "**MOBILE**" {
    :SignalRSyncService nhận event;
    :RealtimeSyncManager
    cập nhật SQLite;

    if (Event loại?) then (POI)
      :Insert/Update/Delete POI;
      :Re-render pins + highlights;
    else if (Content) then
      :Update content local;
      :Refresh ShowPoiDetail
      (nếu đang xem);
    else (Audio)
      :AudioDataChanged event;
    endif
  }
fork again
  partition "**ADMIN WEB**" {
    :JS nhận TraceLogged;
    :Fetch GET summary;
    :Cập nhật KPI cards;
  }
end fork

stop

@enduml
```

---
## SD-20 (NEW) — GPS Tracking → Admin Route Map
**Mô tả:** LocationPollingService gửi poi_heartbeat mỗi lần GPS → TraceLogs lưu lat/lng. Admin vào AdminRouteMap → API group by deviceId → polylines → Leaflet vẽ tuyến di chuyển.

```plantuml
@startuml SD20
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-20: GPS Tracking → Admin Route Map</size></b>\n<size:13><i>GPS polling → TraceLogs → Admin Leaflet map polylines</i></size>

actor "Du khách" as User #88CCEE
participant "Mobile App\n(LocationPolling\nService)" as GPS #DDAA33
participant "API\n(AnalyticsController)" as API #44BB99
database "TraceLogs\n(Database)" as DB #EE8866
actor "Admin" as Admin #BBCC33
participant "AdminPortal\n(AdminRouteMap)" as Portal #EE6677
participant "Leaflet.js\n(Bản đồ)" as Map #CC6699

== <size:14><b>MOBILE GỬI GPS LIÊN TỤC</b></size> ==

loop <b>Mỗi 5-10 giây</b>
  GPS -> GPS : GetLocationAsync() → (lat, lng)
  GPS -> API : **POST** api/analytics\n{event:"poi_heartbeat",\ndeviceId:"abc123",\nextraJson: {lat, lng,\nsource:"mobile_app"}}
  activate API
  API -> DB : INSERT **TraceLog**\n(DeviceId, TimestampUtc,\nExtraJson chứa lat/lng)
  activate DB
  DB --> API : OK
  deactivate DB
  API --> GPS : 200 OK
  deactivate API
end

== <size:14><b>ADMIN XEM ROUTE MAP</b></size> ==

Admin -> Portal : Mở **/AdminRouteMap?hours=24**
activate Portal

Portal -> API : **GET** admin/pois/overview
activate API
API --> Portal : List<POI> (markers trên bản đồ)
deactivate API

Portal -> API : **GET** api/analytics/**routes**\n?hours=24&topUsers=120\n&maxPointsPerUser=260
activate API
API -> DB : SELECT TraceLogs\nWHERE TimestampUtc >= (now - 24h)\nAND ExtraJson LIKE '%lat%'
activate DB
DB --> API : Raw trace logs
deactivate DB

API -> API : Group by **DeviceId**\nFilter events có lat/lng hợp lệ:\npoi_enter, poi_click, qr_scan,\ntts_play, audio_play, poi_heartbeat
API -> API : Sort by timestamp\nLimit maxPointsPerUser per device

API --> Portal : List<**AnonymousUserRouteDto**>\n[{deviceId, points:\n  [{lat, lng, time}...]}]
deactivate API

Portal -> Map : Render:\n• POI markers (đỏ)\n• **Polyline** per device (màu random)\n• Tooltip: deviceId, thời gian
activate Map
Map --> Admin : 🗺️ Bản đồ tuyến di chuyển\ncủa từng du khách (ẩn danh)
deactivate Map
deactivate Portal

@enduml
```

### Activity Diagram — SD-20

```plantuml
@startuml SD20_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-20 Activity: GPS Tracking → Admin Route Map

start

partition "**MOBILE GỬI GPS**" {
  repeat
    :GetLocationAsync → (lat, lng);
    :POST api/analytics
    {event: poi_heartbeat, lat, lng};
    :INSERT TraceLog;
    :Chờ 5-10 giây;
  repeat while (App chạy?) is (Có)
}

partition "**ADMIN XEM ROUTE MAP**" {
  :Admin mở /AdminRouteMap?hours=24;
  :GET admin/pois/overview
  → markers POI;
  :GET api/analytics/routes
  ?hours=24&topUsers=120;
  :Group TraceLogs by deviceId;
  :Filter events có lat/lng;
  :Sort by timestamp;
  :Leaflet render:
  POI markers + polylines;
}

stop

@enduml
```

---
## SD-21 (NEW) — Lưu / Chia sẻ / Dẫn đường POI
**Mô tả:** Du khách bấm Lưu → toggle IsSaved trong SQLite local. Chia sẻ → Share API hệ thống. Dẫn đường → mở Google Maps/Apple Maps/Web fallback.

```plantuml
@startuml SD21
!theme cerulean
skinparam defaultFontSize 15
skinparam maxMessageSize 260
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam sequenceArrowThickness 2
skinparam roundcorner 10
skinparam ParticipantPadding 18
autonumber "<b>[00]"

title <b><size:20>SD-21: Lưu / Chia sẻ / Dẫn đường POI</size></b>\n<size:13><i>Save local → Share API → Google Maps / Apple Maps</i></size>

actor "Du khách" as User #88CCEE
participant "Mobile App\n(MapPage.PoiActions)" as App #DDAA33
participant "DatabaseService\n(SQLite local)" as Local #44BB99
participant "MAUI\nShare API" as Share #EE8866
participant "MAUI\nLauncher" as Launcher #BBCC33
participant "Google Maps /\nApple Maps" as ExtMap #EE6677

== <size:14><b>LƯU POI (LOCAL)</b></size> ==

User -> App : Bấm nút ❤️ **"Lưu"**
activate App
App -> App : poi.**IsSaved** = !IsSaved\n(toggle trạng thái)
App -> Local : **UpdatePoiAsync**(poi)\n(cập nhật SQLite)
activate Local
Local --> App : OK
deactivate Local
App -> App : Cập nhật icon nút Lưu\n(filled ❤️ hoặc outline ♡)
App -> App : BtnShowSaved.IsVisible =\n_pois.Any(p => p.IsSaved)
App --> User : ✅ Đã lưu / Đã hủy lưu
deactivate App

note over User : POI đã lưu hiển thị trong\ndanh sách "Đã lưu" (bộ lọc local)

== <size:14><b>CHIA SẺ</b></size> ==

User -> App : Bấm nút **"Chia sẻ"**
activate App
App -> App : Tạo text chia sẻ:\n"{Tên POI} - {Mô tả ngắn}\nLink: https://..."
App -> Share : **Share.RequestAsync**(\nnew ShareTextRequest {\nTitle, Text, Uri})
activate Share
Share --> User : 📤 Popup chia sẻ hệ thống\n(Zalo, Messenger, Copy link...)
deactivate Share
deactivate App

== <size:14><b>DẪN ĐƯỜNG</b></size> ==

User -> App : Bấm nút **"Dẫn đường"**
activate App
App -> App : Lấy toạ độ POI (lat, lng)
App -> App : TrackPoiEventAsync("navigation_start")

alt #D6FFD6 Android
  App -> Launcher : **OpenAsync**(\n"google.navigation:q={lat},{lng}")
  activate Launcher
  Launcher -> ExtMap : Mở **Google Maps** navigation
  deactivate Launcher
else #D6F0FF iOS
  App -> Launcher : **OpenAsync**(\n"maps://?daddr={lat},{lng}")
  activate Launcher
  Launcher -> ExtMap : Mở **Apple Maps**
  deactivate Launcher
else #FFF0D6 Fallback
  App -> Launcher : **OpenAsync**(\n"https://google.com/maps/dir/\n?destination={lat},{lng}")
  activate Launcher
  Launcher -> ExtMap : Mở Web Google Maps
  deactivate Launcher
end

ExtMap --> User : 🗺️ Hiện đường đi tới POI
deactivate App

@enduml
```

### Activity Diagram — SD-21

```plantuml
@startuml SD21_Activity
skinparam defaultFontSize 14
skinparam shadowing false
skinparam ActivityBorderColor #333333
skinparam ActivityBackgroundColor #FFFFFF
skinparam ArrowColor #333333
skinparam DiamondBorderColor #333333
skinparam DiamondBackgroundColor #FFFFFF
skinparam PartitionBorderColor #666666
skinparam PartitionBackgroundColor #F8F8F8

title SD-21 Activity: Lưu / Chia sẻ / Dẫn đường POI

start

if (Thao tác?) then (Lưu)
  :Toggle IsSaved = !IsSaved;
  :UpdatePoiAsync (SQLite);
  :Cập nhật icon nút Lưu;

else if (Chia sẻ) then
  :Tạo text chia sẻ
  (tên + mô tả + link);
  :Share.RequestAsync();
  :Popup chia sẻ hệ thống;

else (Dẫn đường)
  :Lấy toạ độ POI (lat, lng);
  :Track event navigation_start;

  if (Platform?) then (Android)
    :Mở Google Maps
    navigation:q={lat},{lng};
  else if (iOS) then
    :Mở Apple Maps
    maps://?daddr={lat},{lng};
  else (Fallback)
    :Mở Web Google Maps;
  endif
endif

stop

@enduml
```
