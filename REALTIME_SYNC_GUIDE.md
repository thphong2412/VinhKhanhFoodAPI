# Real-time Synchronization with SignalR

## Overview

The system now supports **real-time, bi-directional sync** between the Web Admin Portal and the Mobile App using SignalR. When admins make changes (create/edit/delete POI, upload audio, etc.), all connected mobile apps receive instant notifications and update automatically.

---

## 🔄 Architecture

```
┌──────────────────┐                    ┌──────────────┐                    ┌──────────────┐
│  Web Admin       │  HTTP + SignalR    │   Central    │   SignalR Broadcast │ Mobile App  │
│  (Thay đổi POI)  │ ◄──────────────►  │     API      │ ◄──────────────────► │   (Real-time) │
└──────────────────┘                    └──────────────┘                    └──────────────┘
      Admin                              (Controllers +                       (App Listener)
   creates POI      ────►  API saves    Hubs send                 ────►  App updates
                          to DB & broadcasts                            local DB
                          to all clients                                & UI refresh
```

---

## 📋 Signal Events

### POI Events
- **PoiAdded**: Triggered when a new POI is created
- **PoiUpdated**: Triggered when a POI is edited
- **PoiDeleted**: Triggered when a POI is deleted

### Audio Events
- **AudioUploaded**: Triggered when audio file is uploaded (any language)
- **AudioDeleted**: Triggered when audio file is deleted
- **AudioProcessed**: Triggered when TTS audio is generated and ready

### Content Events
- **ContentCreated**: Triggered when description/details are added
- **ContentUpdated**: Triggered when description/details are edited
- **ContentDeleted**: Triggered when description/details are deleted

---

## 🚀 How to Use in MAUI App

### 1. **Initialize SignalR Service** (on App Startup)

In your `MauiProgram.cs` - **already configured**:

```csharp
builder.Services.AddSingleton<SignalRSyncService>();
builder.Services.AddSingleton<RealtimeSyncManager>();
```

### 2. **Connect to SignalR Hub** (in your Page/ViewModel)

```csharp
public partial class MapPage : ContentPage
{
    private readonly RealtimeSyncManager _syncManager;

    public MapPage(RealtimeSyncManager syncManager)
    {
        InitializeComponent();
        _syncManager = syncManager;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Start listening to real-time updates
        await _syncManager.StartAsync("https://localhost:7001/sync");

        // Subscribe to events
        _syncManager.PoiDataChanged += OnPoiChanged;
        _syncManager.AudioDataChanged += OnAudioChanged;
        _syncManager.FullSyncRequested += OnFullSyncRequested;
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await _syncManager.StopAsync();
    }

    // Handle POI changes
    private async Task OnPoiChanged(PoiModel poi)
    {
        // POI was added/updated/deleted
        // Refresh your UI or local data
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await RefreshPoiListAsync();
        });
    }

    // Handle Audio changes
    private async Task OnAudioChanged(AudioModel audio)
    {
        // Audio was uploaded or processed
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Re-load audio for specific POI
            await LoadAudioForPoiAsync(audio.PoiId);
        });
    }

    // Full sync request
    private async Task OnFullSyncRequested()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Refresh all data from server
            await RefreshAllDataAsync();
        });
    }
}
```

---

## 🧪 Testing Real-time Sync

### Test Scenario: Create POI on Web Admin

1. **Start API**: `dotnet run --project VinhKhanh.API`
2. **Start Admin Portal**: Open `https://localhost:7291`
3. **Start Mobile App**: `F5` in Visual Studio (Android or iOS emulator)

### Test Steps:

1. **On Web Admin:**
   - Login and navigate to POI List
   - Click "Add New POI"
   - Fill in: Name="Test POI", Category="Food", Coordinates
   - Click "Create"

2. **On Mobile App:**
   - ✅ Within 1 second, new POI appears in the list
   - ✅ Map updates showing new location
   - ✅ Local SQLite database is updated

3. **On Web Admin (Edit):**
   - Click Edit on the POI you just created
   - Change name to "Updated POI"
   - Save

4. **On Mobile App:**
   - ✅ POI name updates instantly
   - ✅ No manual refresh needed

5. **Audio Upload Test:**
   - On Web Admin: Upload audio file (MP3) for the POI
   - ✅ On Mobile App: Audio appears immediately in POI details

---

## 📡 Event Data Examples

### PoiCreated Event
```json
{
  "id": 5,
  "name": "Chùa Vinh Nghiêm",
  "category": "Temple",
  "latitude": 10.7769,
  "longitude": 106.7009,
  "isPublished": true,
  "ownerId": 1,
  "timestamp": "2024-01-15T10:30:45Z"
}
```

### AudioUploaded Event
```json
{
  "id": 12,
  "poiId": 5,
  "url": "/uploads/audio_5_abc123.mp3",
  "languageCode": "vi",
  "isTts": false,
  "isProcessed": true,
  "timestamp": "2024-01-15T10:31:20Z"
}
```

### AudioProcessed Event (TTS Generated)
```json
{
  "id": 13,
  "poiId": 5,
  "url": "/uploads/tts_en_abc456.mp3",
  "languageCode": "en",
  "isTts": true,
  "isProcessed": true,
  "timestamp": "2024-01-15T10:35:00Z"
}
```

---

## 🔌 Connection Status

Check if app is connected to SignalR:

```csharp
if (_syncManager.IsConnected)
{
    Debug.WriteLine("✅ App is synced with server - real-time updates active");
}
else
{
    Debug.WriteLine("❌ App is offline - changes will sync when reconnected");
}
```

---

## ⚡ Auto-Reconnect Behavior

- **Connection Lost**: App automatically tries to reconnect every 2, 10 seconds
- **Network Restored**: Auto-reconnects and requests full data sync
- **Heartbeat**: SignalR pings server every 30 seconds to detect connection loss

---

## 🚨 Troubleshooting

### Problem: App not receiving updates
**Solution:**
1. Check API is running: `https://localhost:7001/swagger`
2. Check app URL matches: `https://localhost:7001/sync`
3. Check firewall allows WebSocket on port 7001
4. Check browser console for SignalR errors

### Problem: "Connection refused" error
**Solution:**
1. Verify API is running
2. On Android emulator: Use `10.0.2.2` instead of `localhost`
3. On iOS simulator: Use `localhost`
4. On physical device: Use your machine's IP (e.g., `https://192.168.1.100:7001/sync`)

### Problem: Updates delayed or not appearing
**Solution:**
1. Check network connectivity
2. Try manual refresh: `await _syncManager.StopAsync()` then `await _syncManager.StartAsync()`
3. Check server logs for errors

---

## 📝 API Endpoints That Broadcast Events

All these endpoints now emit SignalR events:

```
POST   /api/poi                   → PoiAdded
PUT    /api/poi/{id}             → PoiUpdated
DELETE /api/poi/{id}             → PoiDeleted

POST   /api/audio/upload         → AudioUploaded
DELETE /api/audio/{id}           → AudioDeleted
POST   /api/audio/process/{id}   → AudioProcessed

POST   /api/content              → ContentCreated
PUT    /api/content/{id}         → ContentUpdated
DELETE /api/content/{id}         → ContentDeleted
```

---

## 🎯 Key Features

✅ **Real-time Sync**: Changes appear instantly (< 1 second)
✅ **Auto-reconnect**: Seamless recovery if connection drops
✅ **Multi-language Support**: Audio events include language code
✅ **TTS Integration**: Automatic notification when TTS audio is ready
✅ **Low Bandwidth**: Only events sent (not full data dumps)
✅ **Secure**: Uses same X-API-Key authentication as HTTP endpoints

---

## 🔐 Production Setup

For **production** deployment:

1. **Remove SSL bypass** in `SignalRSyncService.cs`:
```csharp
// ⚠️ DEVELOPMENT ONLY - Remove in production
handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
```

2. **Use proper SSL certificates** on server

3. **Configure CORS** in `Program.cs`:
```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("https://yourdomain.com")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
```

4. **Enable scaling** if using multiple API instances:
```csharp
services.AddSignalR().AddAzureSignalR("connection-string");
```

---

## 📊 Monitoring

View SignalR connections in debug output:

```
[SignalR] Connected to hub
[RealtimeSync] Handling POI added: Chùa Vinh Nghiêm
[SignalR] POI Added: Chùa Vinh Nghiêm
```

---

**That's it!** Your app now has real-time, automatic synchronization. When admins make changes on the web, mobile users see them instantly. 🚀
