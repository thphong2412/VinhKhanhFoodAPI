# Real-time Sync - Visual Flow Diagrams

## 1. System Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    VinhKhanh Real-time System                   │
└─────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────┐
│                   ADMIN PORTAL (Web)                             │
│  ├─ POI Management (Create, Edit, Delete)                      │
│  ├─ Audio Upload (VI, EN, JA, KO)                              │
│  └─ Content Descriptions                                        │
└─────────────────────────┬──────────────────────────────────────┘
                          │
                    HTTP + REST
                          │
                          ▼
    ┌─────────────────────────────────────────────┐
    │   CENTRAL API (VinhKhanh.API)               │
    │  ├─ Database Layer (EF Core + SQLite)      │
    │  ├─ REST Endpoints                         │
    │  │  ├─ /api/poi                           │
    │  │  ├─ /api/audio                         │
    │  │  ├─ /api/content                       │
    │  │  └─ /api/admin/*                       │
    │  │                                        │
    │  └─ SignalR Hub (/sync)                   │
    │     ├─ Broadcasting POI events             │
    │     ├─ Broadcasting Audio events           │
    │     └─ Broadcasting Content events         │
    └──────────────┬────────────────────────────┘
                   │
            ┌──────┴──────┐
            │             │
        WebSocket   WebSocket
            │             │
    ┌───────▼──────┐    ┌───▼────────┐
    │  MOBILE APP  │    │ MOBILE APP  │
    │ (Instance 1) │    │(Instance 2) │
    │              │    │             │
    │  ┌─────────┐ │    │ ┌─────────┐ │
    │  │SignalR  │◄┼────┼─┤SignalR  │ │
    │  │Client   │ │    │ │Client   │ │
    │  └────┬────┘ │    │ └────┬────┘ │
    │       │      │    │      │      │
    │  ┌────▼────┐ │    │ ┌────▼────┐ │
    │  │RealtimeS│ │    │ │RealtimeS│ │
    │  │Manager  │ │    │ │Manager  │ │
    │  └────┬────┘ │    │ └────┬────┘ │
    │       │      │    │      │      │
    │  ┌────▼────┐ │    │ ┌────▼────┐ │
    │  │Local DB │ │    │ │Local DB  │ │
    │  │(SQLite) │ │    │ │(SQLite)  │ │
    │  └─────────┘ │    │ └─────────┘ │
    └──────────────┘    └─────────────┘
         App 1              App 2
```

---

## 2. Create POI - Real-time Flow

```
ADMIN PORTAL                           API                         MOBILE APP
─────────────                         ───                         ──────────

Admin clicks
"Create POI"
    │
    ├─ Fills form
    │  └─ Name: "Chùa Vinh Nghiêm"
    │  └─ Category: "Temple"
    │  └─ Coordinates: (10.7769, 106.7009)
    │
    └─ Clicks "Create"
         │
         │ POST /api/poi
         │ {name, category, lat, lng}
         ├────────────────────────────────────►
                                     │
                          1. Validate request
                          2. Save to Database
                               │
                          3. Generate QR Code
                               │
                          4. Broadcast via SignalR
                          └─► _hubContext.Clients.All
                              .SendAsync("PoiAdded", poi)
                                          │
         Connected to Hub                 │
         ◄──────────────────────────────────┘
         │
    SignalRSyncService
    receives "PoiAdded"
         │
    RealtimeSyncManager
    .OnPoiAdded fired
         │
    1. Save to local SQLite
    2. Raise PoiDataChanged event
         │
         └─► UI Page
             .OnPoiChanged()
             └─► Refresh POI List
                 └─► Map Updates ✅
                     List Updates ✅

[TIMELINE]
T+0ms    Admin clicks Create
T+100ms  Request received by API
T+150ms  Data saved to DB
T+160ms  SignalR broadcast sent
T+200ms  Mobile app receives event
T+250ms  Local DB updated
T+300ms  UI refreshes
T+350ms  User sees new POI on map ✅
```

---

## 3. Upload Audio - Real-time Flow

```
ADMIN PORTAL                           API                         MOBILE APP
─────────────                         ───                         ──────────

Admin selects
"Upload Audio"
    │
    ├─ Chooses POI
    ├─ Selects language: "Tiếng Việt"
    └─ Selects MP3 file: "description.mp3"
         │
         │ POST /api/audio/upload
         │ multipart/form-data
         │ [POI_ID=5, language=vi, file=...]
         ├────────────────────────────────────►
                                     │
                          1. Validate file (MP3)
                          2. Save to /uploads/
                          3. Save metadata to DB
                          4. Broadcast AudioUploaded
                          └─► _hubContext.Clients.All
                              .SendAsync("AudioUploaded", audio)
                                          │
         Connected to Hub                 │
         ◄──────────────────────────────────┘
         │
    OnAudioUploaded
    event triggered
         │
    RealtimeSyncManager
    .HandleAudioUploaded()
         │
    1. Notify UI
    2. Raise AudioDataChanged event
         │
         └─► UI Page (POI Details)
             .OnAudioChanged()
             └─► Reload Audio List
                 └─► Shows MP3 available ✅
                     Play button active ✅

[OPTIONAL: TTS Generation]

Admin clicks
"Generate English"
    │
    └─ POST /api/audio/process/13 (TTS)
         │
         ├────────────────────────────────────►
                                     │
                          1. Fetch Vietnamese audio
                          2. Run TTS: VI → EN
                          3. Save EN MP3 to /uploads/
                          4. Update DB (IsProcessed=true)
                          5. Broadcast AudioProcessed
                          └─► _hubContext.Clients.All
                              .SendAsync("AudioProcessed", audio)
                                          │
         Connected to Hub                 │
         ◄──────────────────────────────────┘
         │
    OnAudioProcessed
    event triggered
         │
    RealtimeSyncManager
    .HandleAudioProcessed()
         │
         └─► UI Page
             .OnAudioChanged()
             └─► English audio now available ✅
                 User can select EN language ✅
```

---

## 4. Edit POI - Real-time Flow

```
ADMIN PORTAL                           API                         MOBILE APP
─────────────                         ───                         ──────────

Admin clicks
"Edit POI #5"
    │
    ├─ Changes name:
    │  "Chùa Vinh Nghiêm" → "Chùa Vinh Nghiêm Mới"
    │
    └─ Clicks "Save"
         │
         │ PUT /api/poi/5
         │ {id, name, category, ...}
         ├────────────────────────────────────►
                                     │
                          1. Find POI #5
                          2. Update all fields
                          3. Regenerate QR Code
                          4. Save to Database
                          5. Broadcast PoiUpdated
                          └─► _hubContext.Clients.All
                              .SendAsync("PoiUpdated", poi)
                                          │
         Connected to Hub                 │
         ◄──────────────────────────────────┘
         │
    OnPoiUpdated
    event triggered
         │
    RealtimeSyncManager
    .HandlePoiUpdated()
         │
    1. Update local DB
    2. Raise PoiDataChanged event
         │
         └─► UI Pages listening:
             ├─► MapPage.OnPoiChanged()
             │   └─► Update marker name ✅
             │       Refresh popup ✅
             │
             └─► PoiListPage.OnPoiChanged()
                 └─► Update list item ✅
                     Refresh sort (if by name) ✅
```

---

## 5. Delete POI - Real-time Flow

```
ADMIN PORTAL                           API                         MOBILE APP
─────────────                         ───                         ──────────

Admin right-clicks
POI in table
    │
    └─ Confirms delete
         │
         │ DELETE /api/poi/5
         ├────────────────────────────────────►
                                     │
                          1. Find POI #5
                          2. Delete from DB
                          3. Delete associated
                             audio/content
                          4. Broadcast PoiDeleted
                          └─► _hubContext.Clients.All
                              .SendAsync("PoiDeleted", {id: 5})
                                          │
         Connected to Hub                 │
         ◄──────────────────────────────────┘
         │
    OnPoiDeleted
    event triggered
         │
    RealtimeSyncManager
    .HandlePoiDeleted()
         │
    1. Notify UI about deletion
    2. Raise PoiDataChanged event
         │
         └─► All UI Pages:
             ├─► MapPage
             │   └─► Remove marker #5 ✅
             │       Center to next POI ✅
             │
             └─► ListPage
                 └─► Remove item from list ✅
                     Refresh count ✅
```

---

## 6. Connection Status Diagram

```
┌──────────────────┐
│  DISCONNECTED    │  (App just started)
└────────┬─────────┘
         │
         └─► Call: StartAsync("https://localhost:7001/sync")
             │
             ├─► Connecting...
             │   ├─► HubConnection created
             │   ├─► Event handlers registered
             │   └─► WebSocket established
             │
             ▼
    ┌──────────────────┐
    │  CONNECTED ✅    │  (Receiving real-time updates)
    └────────┬─────────┘
             │
      ┌──────┴──────┐
      │             │
   [Network Lost]   [Normal]
      │             │
      ▼             ├─► Continue receiving
    ┌─────────────┐ │   updates
    │RECONNECTING │ │
    │(2s delay)   │ └─► Updates auto-sync
    └──────┬──────┘    └─► No manual refresh
           │               needed
      [Retry Failed]
           │
           ▼
    ┌──────────────┐
    │RECONNECTING  │
    │(10s delay)   │  (Exponential backoff)
    └──────┬───────┘
           │
      [Success]
           │
           ▼
    ┌──────────────────┐
    │  RECONNECTED ✅  │
    │ (Auto sync all)  │  (Requests full POI
    └────────┬─────────┘   refresh from server)
             │
             └─► Back to CONNECTED state
```

---

## 7. Message Flow - High Level

```
                        SignalR Hub Events

┌─────────────┐         POST/PUT/DELETE      ┌────────────┐
│ Admin makes │────► PoiController ┐         │  SyncHub   │
│  changes    │                     ├─ Action ├─► Broadcast
└─────────────┘                     │         │  to all
              AudioController ┐     │         │  clients
                              ├─────┤         │
              ContentController    │         │
                              ┘     │         │
                                    │         │
                             SaveChanges()   │
                                    │        │
                            ┌───────┼────────┤
                            │       │        │
                          Save to   Emit     │
                          Database  Event    │
                            │              │
                            ▼              ▼
                        [DB Updated]  [SignalR Hub]
                                          │
                    ┌─────────────────────┼─────────────────────┐
                    │                     │                     │
               Mobile 1              Mobile 2              Mobile 3
               Receives              Receives              Receives
                  Event                 Event                 Event
                    │                     │                     │
                    ▼                     ▼                     ▼
              Update Local DB        Update Local DB        Update Local DB
              (SQLite)               (SQLite)               (SQLite)
                    │                     │                     │
                    ▼                     ▼                     ▼
              Raise Event             Raise Event             Raise Event
                    │                     │                     │
                    ▼                     ▼                     ▼
              UI Refresh             UI Refresh              UI Refresh
                    │                     │                     │
                    ▼                     ▼                     ▼
              User sees ✅           User sees ✅            User sees ✅
              change on              change on               change on
              their device           their device           their device
```

---

## 8. Event Sequence Diagram

```
Admin        AdminPortal        API              SignalR      MobileApp1      MobileApp2
 │               │               │                 │             │               │
 │   Create POI  │               │                 │             │               │
 ├──────────────►│               │                 │             │               │
 │               │  POST /api/poi│                 │             │               │
 │               ├──────────────►│                 │             │               │
 │               │               │ Save to DB      │             │               │
 │               │               │ (EF Core)       │             │               │
 │               │               │─────────┐       │             │               │
 │               │               │         │       │             │               │
 │               │               │◄────────┘       │             │               │
 │               │               │                 │             │               │
 │               │               │ Generate QR     │             │               │
 │               │               │─────────┐       │             │               │
 │               │               │         │       │             │               │
 │               │               │◄────────┘       │             │               │
 │               │               │                 │             │               │
 │               │               │ Broadcast       │             │               │
 │               │               │ PoiAdded        │             │               │
 │               │               ├────────────────►│             │               │
 │               │               │                 │ Forward     │               │
 │               │               │                 ├────────────►│               │
 │               │               │                 │             │ OnPoiAdded    │
 │               │               │                 │             │ handler       │
 │               │               │                 │             │─────┐         │
 │               │               │                 │             │     │Update   │
 │               │               │                 │             │     │Local DB │
 │               │               │                 │             │◄────┘         │
 │               │               │                 │             │               │
 │               │               │                 │             │ UI Refresh    │
 │               │               │                 │             │─────┐         │
 │               │               │                 │             │     │         │
 │               │               │                 │ Forward     │◄────┘         │
 │               │               │                 ├───────────────────────────►│
 │               │               │                 │             │               │ OnPoiAdded
 │               │               │                 │             │               │ handler
 │               │               │                 │             │               │─────┐
 │               │               │                 │             │               │     │Update
 │               │               │                 │             │               │     │Local DB
 │               │               │                 │             │               │◄────┘
 │               │  OK 201       │                 │             │               │
 │               │◄──────────────┤                 │             │  ✅ POI        │  ✅ POI
 │◄──────────────┤               │                 │             │  Updated      │  Updated
 │               │               │                 │             │  on Device    │  on Device

Time: ~350ms total latency
      100ms (request) + 50ms (DB) + 50ms (broadcast) + 50ms (receive) + 50ms (update UI)
```

---

## Summary

```
┌─────────────────────────────────────────────────────────────────┐
│                    REAL-TIME SYNC PIPELINE                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  1. Admin Action (Web)     → 2. REST Endpoint → 3. Database    │
│     (Create/Edit/Delete)      (POST/PUT/DELETE)  (SQLite/SQL)  │
│                                                      │          │
│  6. App UI Updates ◄─────── 5. Event Handler ◄──── 4. SignalR  │
│     (Refresh View)          (RealtimeSyncManager)   Broadcast  │
│                                                                 │
│  ✅ All connected apps see changes instantly!                  │
│  ✅ No manual refresh needed!                                  │
│  ✅ Automatic reconnection if network drops!                   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

**This is the architecture powering real-time synchronization in VinhKhanh!** 🚀
