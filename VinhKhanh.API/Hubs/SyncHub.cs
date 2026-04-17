using Microsoft.AspNetCore.SignalR;

namespace VinhKhanh.API.Hubs
{
    public class SyncHub : Hub
    {
        // Called when a client connects
        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
            await Clients.Caller.SendAsync("Connected", new { message = "Connected to sync hub", timestamp = DateTime.UtcNow });
        }

        // Called when a client disconnects
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }

        // For debugging: clients can request full POI list refresh
        public async Task RequestFullSync()
        {
            await Clients.All.SendAsync("RequestFullPoiSync", new { timestamp = DateTime.UtcNow });
        }

        // Broadcast POI changes to all connected clients
        public async Task BroadcastPoiCreated(object poi)
        {
            await Clients.All.SendAsync("PoiCreated", poi);
        }

        public async Task BroadcastPoiUpdated(object poi)
        {
            await Clients.All.SendAsync("PoiUpdated", poi);
        }

        public async Task BroadcastPoiDeleted(int poiId)
        {
            await Clients.All.SendAsync("PoiDeleted", poiId);
        }

        // Broadcast audio changes
        public async Task BroadcastAudioUploaded(object audio)
        {
            await Clients.All.SendAsync("AudioUploaded", audio);
        }

        public async Task BroadcastAudioDeleted(int audioId, int poiId)
        {
            await Clients.All.SendAsync("AudioDeleted", new { id = audioId, poiId });
        }

        // Broadcast content changes
        public async Task BroadcastContentUpdated(object content)
        {
            await Clients.All.SendAsync("ContentUpdated", content);
        }
    }
}
