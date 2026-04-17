using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VinhKhanh.Shared;
using VinhKhanh.Data;

namespace VinhKhanh.Services
{
    /// <summary>
    /// Manages real-time data synchronization with the backend API
    /// Listens to SignalR events and updates local SQLite database
    /// </summary>
    public class RealtimeSyncManager
    {
        private readonly SignalRSyncService _signalRService;
        private readonly PoiRepository _poiRepository;
        private readonly ApiService _apiService;
        private readonly DatabaseService _databaseService;

        // Events to notify UI of changes
        public event Func<PoiModel, Task> PoiDataChanged;
        public event Func<AudioModel, Task> AudioDataChanged;
        public event Func<ContentModel, Task> ContentDataChanged;
        public event Func<Task> FullSyncRequested;

        public RealtimeSyncManager(SignalRSyncService signalRService, PoiRepository poiRepository, ApiService apiService, DatabaseService databaseService)
        {
            _signalRService = signalRService;
            _poiRepository = poiRepository;
            _apiService = apiService;
            _databaseService = databaseService;

            // Subscribe to SignalR events
            SubscribeToSignalREvents();
        }

        private void SubscribeToSignalREvents()
        {
            // POI Events
            _signalRService.OnPoiAdded += HandlePoiAdded;
            _signalRService.OnPoiUpdated += HandlePoiUpdated;
            _signalRService.OnPoiDeleted += HandlePoiDeleted;

            // Audio Events
            _signalRService.OnAudioUploaded += HandleAudioUploaded;
            _signalRService.OnAudioDeleted += HandleAudioDeleted;
            _signalRService.OnAudioProcessed += HandleAudioProcessed;

            // Content Events
            _signalRService.OnContentCreated += HandleContentCreated;
            _signalRService.OnContentUpdated += HandleContentUpdated;
            _signalRService.OnContentDeleted += HandleContentDeleted;

            // Connection Events
            _signalRService.OnConnected += HandleConnected;
            _signalRService.OnDisconnected += HandleDisconnected;
        }

        public async Task StartAsync(string? hubUrl = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hubUrl))
                {
                    await _signalRService.ConnectForDeviceAsync();
                }
                else
                {
                    await _signalRService.ConnectAsync(hubUrl);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RealtimeSyncManager start error: {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            try
            {
                await _signalRService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RealtimeSyncManager stop error: {ex.Message}");
            }
        }

        public bool IsConnected => _signalRService.IsConnected;

        // ========== POI Handlers ==========
        private async Task HandlePoiAdded(PoiModel poi)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling POI added: {poi.Name}");
                if (_databaseService != null)
                {
                    await _databaseService.SavePoiAsync(poi);
                }
                else
                {
                    await _poiRepository.SaveAsync(poi);
                }
                await (PoiDataChanged?.Invoke(poi) ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling POI added: {ex.Message}");
            }
        }

        private async Task HandlePoiUpdated(PoiModel poi)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling POI updated: {poi.Name}");
                if (_databaseService != null)
                {
                    await _databaseService.SavePoiAsync(poi);
                }
                else
                {
                    await _poiRepository.SaveAsync(poi);
                }
                await (PoiDataChanged?.Invoke(poi) ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling POI updated: {ex.Message}");
            }
        }

        private async Task HandlePoiDeleted(int poiId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling POI deleted: {poiId}");
                if (_databaseService != null)
                {
                    var deletedPoi = await _databaseService.DeletePoiByIdAsync(poiId);
                    var deletedContents = await _databaseService.DeleteContentsByPoiIdAsync(poiId);
                    var deletedAudios = await _databaseService.DeleteAudiosByPoiIdAsync(poiId);
                    System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Deleted local POI={deletedPoi}, contents={deletedContents}, audios={deletedAudios} for poiId={poiId}");
                }

                await (PoiDataChanged?.Invoke(new PoiModel { Id = poiId }) ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling POI deleted: {ex.Message}");
            }
        }

        // ========== Audio Handlers ==========
        private async Task HandleAudioUploaded(AudioModel audio)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling audio uploaded: POI {audio.PoiId}");
                // Store audio metadata in local DB
                await (AudioDataChanged?.Invoke(audio) ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling audio uploaded: {ex.Message}");
            }
        }

        private async Task HandleAudioDeleted(int audioId, int poiId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling audio deleted: {audioId}");
                await (AudioDataChanged?.Invoke(new AudioModel { Id = audioId, PoiId = poiId }) ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling audio deleted: {ex.Message}");
            }
        }

        private async Task HandleAudioProcessed(AudioModel audio)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling audio processed (TTS): {audio.Id}");
                await (AudioDataChanged?.Invoke(audio) ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling audio processed: {ex.Message}");
            }
        }

        // ========== Content Handlers ==========
        private async Task HandleContentCreated(ContentModel content)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling content created: {content.Title}");
                await (ContentDataChanged?.Invoke(content) ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling content created: {ex.Message}");
            }
        }

        private async Task HandleContentUpdated(ContentModel content)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling content updated: {content.Title}");
                await (ContentDataChanged?.Invoke(content) ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling content updated: {ex.Message}");
            }
        }

        private async Task HandleContentDeleted(int contentId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling content deleted: {contentId}");
                await (ContentDataChanged?.Invoke(new ContentModel { Id = contentId }) ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling content deleted: {ex.Message}");
            }
        }

        // ========== Connection Handlers ==========
        private async Task HandleConnected()
        {
            System.Diagnostics.Debug.WriteLine("[RealtimeSync] Connected to server - requesting full sync");
            // Trigger full sync when connected
            await SyncAllPoisAsync();
            await (FullSyncRequested?.Invoke() ?? Task.CompletedTask);
        }

        private async Task HandleDisconnected()
        {
            System.Diagnostics.Debug.WriteLine("[RealtimeSync] Disconnected from server - will try to reconnect");
            await Task.CompletedTask;
        }

        /// <summary>
        /// Fetch all POIs from API and update local database
        /// </summary>
        public async Task SyncAllPoisAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[RealtimeSync] Syncing all POIs from server...");

                if (_apiService == null)
                {
                    System.Diagnostics.Debug.WriteLine("[RealtimeSync] ApiService is null - cannot sync");
                    return;
                }

                var serverPois = await _apiService.GetPoisAsync();
                if (serverPois == null || serverPois.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[RealtimeSync] No POIs returned from server");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Received {serverPois.Count} POIs from server");

                // Update local DB with server POIs
                foreach (var poi in serverPois)
                {
                    if (_databaseService != null)
                    {
                        await _databaseService.SavePoiAsync(poi);
                    }
                    else
                    {
                        await _poiRepository.SaveAsync(poi);
                    }
                    await (PoiDataChanged?.Invoke(poi) ?? Task.CompletedTask);
                }

                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Completed sync of {serverPois.Count} POIs");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Error syncing POIs: {ex.Message}");
            }
        }
    }
}
