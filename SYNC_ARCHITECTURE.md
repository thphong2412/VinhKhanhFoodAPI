# Data Synchronization Architecture

## System Overview

```
┌─────────────────────┐         ┌──────────────────┐         ┌──────────────────┐
│  Web Admin Portal   │         │   Central API    │         │   Mobile App     │
│  (Admin makes       │         │  (Data layer)    │         │  (Consumes data) │
│   changes here)     │         │                  │         │                  │
└──────────┬──────────┘         └────────┬─────────┘         └────────┬─────────┘
           │                             │                           │
           │ HTTP Requests              │                           │
           │ (POST/PUT/DELETE)          │ Database                  │ HTTP Requests
           ├────────────────────────────┼──────────────────────────►(GET)
           │                            │ Persistence               │
           │                            │                           │
           │  1. POST /api/poi          │                    3. fetch from
           │  2. PUT /api/poi/{id}      │                       /api/poi
           │  3. DELETE /api/poi/{id}   │                    4. Display to user
           │                            │                           │
           └────────────────────────────┘                           │
```

## Real-World Examples

### Scenario 1: Create a New POI

**Admin Portal (Web):**
```
1. Admin clicks "Add POI"
2. Fills form: Name="Ốc Oanh 534", Category="Food", Coordinates=(10.7584, 106.7058)
3. Clicks "Create"
4. Browser sends: POST /api/poi with full POI data + X-API-Key: admin123
```

**Central API:**
```
5. Receives POST request
6. Validates X-API-Key ✓
7. Saves to Database
8. Returns: { id: 1, name: "Ốc Oanh 534", ... }
```

**Mobile App (Next time it opens or syncs):**
```
9. App makes: GET /api/poi
10. Receives full list including new POI #1
11. Updates local SQLite database
12. Shows "Ốc Oanh 534" in POI list ✓
```

### Scenario 2: Edit a POI

**Admin Portal (Web):**
```
1. Admin clicks Edit on POI #1
2. Changes: Price from "$10-20" to "$5-15"
3. Clicks "Update"
4. Browser sends: PUT /api/poi/1 with updated data
```

**Central API:**
```
5. Validates X-API-Key ✓
6. Updates POI #1 in Database
7. Returns: { id: 1, updated: true, ... }
```

**Mobile App:**
```
8. On next data refresh: GET /api/poi/1
9. Receives updated price
10. Updates local cache
11. User sees new price ✓
```

### Scenario 3: Delete a POI

**Admin Portal (Web):**
```
1. Admin right-clicks POI in table
2. Confirms deletion
3. Browser sends: DELETE /api/poi/1 + X-API-Key: admin123
```

**Central API:**
```
4. Validates key ✓
5. Deletes POI #1 from Database
6. Returns: { deleted: true }
```

**Mobile App:**
```
7. On next sync: GET /api/poi
8. POI #1 is no longer in list
9. App removes from local database
10. User won't see POI #1 anymore ✓
```

## API Endpoints Structure

### POI Management
```
GET    /api/poi              → List all POIs
GET    /api/poi/{id}         → Get single POI
POST   /api/poi              → Create new POI (requires X-API-Key)
PUT    /api/poi/{id}         → Update POI (requires X-API-Key)
DELETE /api/poi/{id}         → Delete POI (requires X-API-Key)
```

### Tour Management
```
GET    /api/tour             → List all tours
GET    /api/tour/{id}        → Get single tour
POST   /api/tour             → Create new tour (requires X-API-Key)
PUT    /api/tour/{id}        → Update tour (requires X-API-Key)
DELETE /api/tour/{id}        → Delete tour (requires X-API-Key)
```

### Content (POI Translations)
```
GET    /api/content          → List all content
POST   /api/content          → Add content (requires X-API-Key)
PUT    /api/content/{id}     → Update content (requires X-API-Key)
DELETE /api/content/{id}     → Delete content (requires X-API-Key)
```

### Analytics
```
GET    /api/analytics/topPois      → Top POIs by listen count
GET    /api/analytics/heatmap      → User location heatmap
GET    /api/analytics/avg-duration → Average listening time per POI
GET    /api/analytics/logs         → Trace logs
POST   /api/analytics              → Log user interaction
```

## Request/Response Examples

### Create POI (Web Admin → API)

**Request:**
```http
POST /api/poi HTTP/1.1
Host: localhost:6076
X-API-Key: admin123
Content-Type: application/json

{
  "name": "Nhà Hàng Lẩu Xua",
  "category": "Restaurant",
  "latitude": 10.758,
  "longitude": 106.705,
  "radius": 50,
  "priority": 1,
  "cooldownSeconds": 300,
  "imageUrl": "https://example.com/image.jpg",
  "websiteUrl": "https://example.com",
  "isPublished": true
}
```

**Response (Success):**
```json
{
  "id": 5,
  "name": "Nhà Hàng Lẩu Xua",
  "category": "Restaurant",
  "latitude": 10.758,
  "longitude": 106.705,
  "radius": 50,
  "priority": 1,
  "cooldownSeconds": 300,
  "imageUrl": "https://example.com/image.jpg",
  "websiteUrl": "https://example.com",
  "isPublished": true,
  "contents": []
}
```

### Get POI (Mobile App → API)

**Request:**
```http
GET /api/poi/5 HTTP/1.1
Host: api.example.com
```

**Response:**
```json
{
  "id": 5,
  "name": "Nhà Hàng Lẩu Xua",
  "category": "Restaurant",
  "latitude": 10.758,
  "longitude": 106.705,
  "radius": 50,
  "contents": [
    {
      "id": 1,
      "languageCode": "vi",
      "title": "Nhà Hàng Lẩu Xua",
      "description": "Quán lẩu ngon tại...",
      "audioUrl": "https://api.example.com/audio/5_vi.mp3",
      "rating": 4.5
    }
  ]
}
```

## Key Points

✅ **Single Source of Truth**: API database is the authoritative source
✅ **Real-time Updates**: Changes appear on mobile after next sync
✅ **Security**: All write operations require X-API-Key header
✅ **Scalability**: Mobile app can work offline, syncs when reconnected
✅ **Flexibility**: Can add new features (tours, content versions) easily

## Development Workflow

### For Admin (Web)
1. Log into admin portal
2. Make changes (create/edit/delete POI or Tours)
3. Changes go to API immediately
4. Test by checking mobile app

### For Developers
1. Change is made in admin portal
2. HTTP request sent to API
3. API validates and stores in database
4. Mobile app fetches on next refresh
5. Feature complete

## Debugging Synchronization Issues

### Check if Changes Sync

**Step 1: Admin makes change**
```
In browser: Check Network tab → Should see POST/PUT/DELETE request succeed
```

**Step 2: Verify API received it**
```
In API logs: Should see request logged with 200 status
```

**Step 3: Mobile refreshes data**
```
In app: Pull to refresh → Should fetch latest from API
```

### Common Issues

| Problem | Solution |
|---------|----------|
| Mobile doesn't see new POI | Check API key is correct, mobile cached data |
| Changes don't appear | Verify X-API-Key header in request |
| Slow sync | Check network latency, API performance |
| Conflicts | Admin should ensure POI IDs don't duplicate |

## Future Enhancements

- [ ] Bidirectional sync (mobile → web)
- [ ] Conflict resolution for simultaneous edits
- [ ] Offline queue for mobile changes
- [ ] Real-time notifications (WebSocket)
- [ ] Version history/audit trail
- [ ] Bulk operations (batch create/delete)
