using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
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
        private bool _isFullSyncInProgress;
        private bool _hasCompletedInitialSync;
        private DateTime _lastFullSyncUtc = DateTime.MinValue;
        private readonly SemaphoreSlim _syncThrottleLock = new(1, 1);
        private static readonly TimeSpan FullSyncMinInterval = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan RealtimeEventCooldown = TimeSpan.FromMilliseconds(800);
        private static readonly TimeSpan FullSyncRequestCooldown = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan PoiContentSyncCooldown = TimeSpan.FromMilliseconds(1200);
        private readonly SemaphoreSlim _realtimeEventGate = new(1, 1);
        private DateTime _lastRealtimeEventUtc = DateTime.MinValue;
        private DateTime _lastFullSyncRequestUtc = DateTime.MinValue;
        private readonly ConcurrentDictionary<int, DateTime> _lastPoiContentSyncUtc = new();
        private readonly ConcurrentDictionary<int, string> _poiChangeFingerprint = new();
        private readonly ConcurrentDictionary<int, string> _contentChangeFingerprint = new();
        private readonly ConcurrentDictionary<int, string> _audioChangeFingerprint = new();
        private readonly ConcurrentDictionary<int, string> _poiQrSnapshot = new();

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
            _signalRService.OnRequestFullSync += HandleRequestFullSync;
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
                if (poi == null) return;
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling POI added: {poi.Name}");
                if (!ShouldProcessRealtimeEvent(ShouldBypassRealtimeCooldownForPoi(poi))) return;
                if (poi != null && !ShouldApplyPoiChange(poi)) return;
                if (_databaseService != null)
                {
                    await _databaseService.SavePoiAsync(poi);
                }
                else
                {
                    await _poiRepository.SaveAsync(poi);
                }

                UpdatePoiQrSnapshot(poi);

                if (!_isFullSyncInProgress)
                {
                    await (PoiDataChanged?.Invoke(poi) ?? Task.CompletedTask);
                }
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
                if (poi == null) return;
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling POI updated: {poi.Name}");
                if (!ShouldProcessRealtimeEvent(ShouldBypassRealtimeCooldownForPoi(poi))) return;
                if (poi != null && !ShouldApplyPoiChange(poi)) return;
                if (_databaseService != null)
                {
                    await _databaseService.SavePoiAsync(poi);
                }
                else
                {
                    await _poiRepository.SaveAsync(poi);
                }

                UpdatePoiQrSnapshot(poi);

                if (!_isFullSyncInProgress)
                {
                    await (PoiDataChanged?.Invoke(poi) ?? Task.CompletedTask);
                }
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
                if (!ShouldProcessRealtimeEvent()) return;
                if (_databaseService != null)
                {
                    var deletedPoi = await _databaseService.DeletePoiByIdAsync(poiId);
                    var deletedContents = await _databaseService.DeleteContentsByPoiIdAsync(poiId);
                    var deletedAudios = await _databaseService.DeleteAudiosByPoiIdAsync(poiId);
                    System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Deleted local POI={deletedPoi}, contents={deletedContents}, audios={deletedAudios} for poiId={poiId}");
                }

                _poiChangeFingerprint.TryRemove(poiId, out _);
                _lastPoiContentSyncUtc.TryRemove(poiId, out _);
                _poiQrSnapshot.TryRemove(poiId, out _);

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
                if (audio == null) return;
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling audio uploaded: POI {audio.PoiId}");
                if (!ShouldProcessRealtimeEvent()) return;
                if (audio != null && !ShouldApplyAudioChange(audio)) return;
                if (_databaseService != null && audio != null)
                {
                    await _databaseService.SaveAudioAsync(audio);
                }
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
                if (!ShouldProcessRealtimeEvent()) return;
                if (_databaseService != null && audioId > 0)
                {
                    var deleted = await _databaseService.DeleteAudioByIdAsync(audioId);
                    System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Deleted local audioId={audioId}, rows={deleted}");
                }

                _audioChangeFingerprint.TryRemove(audioId, out _);

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
                if (audio == null) return;
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling audio processed (TTS): {audio.Id}");
                if (!ShouldProcessRealtimeEvent()) return;
                if (audio != null && !ShouldApplyAudioChange(audio)) return;
                if (_databaseService != null && audio != null)
                {
                    await _databaseService.SaveAudioAsync(audio);
                }
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
                if (content == null) return;
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling content created: {content.Title}");
                if (!ShouldProcessRealtimeEvent()) return;
                if (content != null && !ShouldApplyContentChange(content)) return;
                if (_databaseService != null && content != null)
                {
                    await _databaseService.SaveContentAsync(content);

                    if (content.PoiId > 0)
                    {
                        await SyncPoiContentsAsync(content.PoiId);
                    }
                }
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
                if (content == null) return;
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Handling content updated: {content.Title}");
                if (!ShouldProcessRealtimeEvent()) return;
                if (content != null && !ShouldApplyContentChange(content)) return;
                if (_databaseService != null && content != null)
                {
                    await _databaseService.SaveContentAsync(content);

                    if (content.PoiId > 0)
                    {
                        await SyncPoiContentsAsync(content.PoiId);
                    }
                }
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
                ContentModel? deletedContent = null;
                if (_databaseService != null && contentId > 0)
                {
                    deletedContent = await _databaseService.GetContentByIdAsync(contentId);
                    var deleted = await _databaseService.DeleteContentByIdAsync(contentId);
                    System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Deleted local contentId={contentId}, rows={deleted}");

                    if (deletedContent?.PoiId > 0)
                    {
                        await SyncPoiContentsAsync(deletedContent.PoiId);
                    }
                }

                _contentChangeFingerprint.TryRemove(contentId, out _);

                await (ContentDataChanged?.Invoke(new ContentModel
                {
                    Id = contentId,
                    PoiId = deletedContent?.PoiId ?? 0,
                    LanguageCode = deletedContent?.LanguageCode
                }) ?? Task.CompletedTask);
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
            try
            {
                if (_hasCompletedInitialSync)
                {
                    System.Diagnostics.Debug.WriteLine("[RealtimeSync] Skip full sync: already completed initial sync");
                    return;
                }

                if (_databaseService != null)
                {
                    var localPois = await _databaseService.GetPoisAsync();
                    if (localPois != null && localPois.Count > 0)
                    {
                        _hasCompletedInitialSync = true;
                        System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Skip full sync on reconnect: using cached local POIs ({localPois.Count})");
                        return;
                    }
                }
            }
            catch { }

            await SyncAllPoisAsync();
        }

        private async Task HandleDisconnected()
        {
            System.Diagnostics.Debug.WriteLine("[RealtimeSync] Disconnected from server - will try to reconnect");
            await Task.CompletedTask;
        }

        private async Task HandleRequestFullSync()
        {
            var now = DateTime.UtcNow;
            if (_lastFullSyncRequestUtc != DateTime.MinValue
                && (now - _lastFullSyncRequestUtc) < FullSyncRequestCooldown)
            {
                System.Diagnostics.Debug.WriteLine("[RealtimeSync] Skip RequestFullPoiSync burst: throttled");
                return;
            }

            _lastFullSyncRequestUtc = now;
            System.Diagnostics.Debug.WriteLine("[RealtimeSync] RequestFullPoiSync event received");
            await SyncAllPoisAsync();
            await (FullSyncRequested?.Invoke() ?? Task.CompletedTask);
        }

        /// <summary>
        /// Fetch all POIs from API and update local database
        /// </summary>
        public async Task SyncAllPoisAsync()
        {
            try
            {
                await _syncThrottleLock.WaitAsync();
                if (_isFullSyncInProgress)
                {
                    System.Diagnostics.Debug.WriteLine("[RealtimeSync] Full sync is already running - skip duplicate request");
                    return;
                }

                var now = DateTime.UtcNow;
                if (_lastFullSyncUtc != DateTime.MinValue
                    && (now - _lastFullSyncUtc) < FullSyncMinInterval)
                {
                    System.Diagnostics.Debug.WriteLine("[RealtimeSync] Skip full sync burst: throttled");
                    return;
                }

                _isFullSyncInProgress = true;
                _lastFullSyncUtc = now;
                System.Diagnostics.Debug.WriteLine("[RealtimeSync] Syncing all POIs from server...");

                if (_apiService == null)
                {
                    System.Diagnostics.Debug.WriteLine("[RealtimeSync] ApiService is null - cannot sync");
                    return;
                }

                var selectedLanguage = NormalizeLanguageCode(Preferences.Default.Get("selected_language", "vi"));
                var loadAll = await _apiService.GetPoisLoadAllAsync(selectedLanguage);
                if ((loadAll?.Items == null || loadAll.Items.Count == 0)
                    && !string.Equals(selectedLanguage, "vi", StringComparison.OrdinalIgnoreCase))
                {
                    loadAll = await _apiService.GetPoisLoadAllAsync("vi");
                }
                var serverPois = loadAll?.Items?
                    .Select(x => x?.Poi)
                    .Where(p => p != null)
                    .GroupBy(p => p.Id)
                    .Select(g => g.First())
                    .ToList();

                var localizationByPoiId = loadAll?.Items?
                    .Where(x => x?.Poi != null && x.Poi.Id > 0 && x.Localization.HasValue)
                    .GroupBy(x => x!.Poi.Id)
                    .ToDictionary(g => g.Key, g => g.First().Localization);

                if (serverPois == null || serverPois.Count == 0)
                {
                    serverPois = await _apiService.GetPoisAsync();
                }

                if (serverPois == null || serverPois.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[RealtimeSync] No POIs returned from server");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Received {serverPois.Count} POIs from server");

                Dictionary<int, PoiModel> localPoiById = new();
                if (_databaseService != null)
                {
                    try
                    {
                        var localPois = await _databaseService.GetPoisAsync();
                        localPoiById = localPois?
                            .Where(x => x != null && x.Id > 0)
                            .GroupBy(x => x.Id)
                            .ToDictionary(g => g.Key, g => g.First())
                            ?? new Dictionary<int, PoiModel>();
                        var localIds = localPoiById.Keys.ToHashSet();
                        var serverIds = serverPois.Select(x => x.Id).Where(id => id > 0).ToHashSet();
                        var staleIds = localIds.Where(id => !serverIds.Contains(id)).ToList();

                        foreach (var stalePoiId in staleIds)
                        {
                            var deletedPoi = await _databaseService.DeletePoiByIdAsync(stalePoiId);
                            var deletedContents = await _databaseService.DeleteContentsByPoiIdAsync(stalePoiId);
                            var deletedAudios = await _databaseService.DeleteAudiosByPoiIdAsync(stalePoiId);
                            System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Removed stale local POI={deletedPoi}, contents={deletedContents}, audios={deletedAudios} for poiId={stalePoiId}");
                            if (!_isFullSyncInProgress)
                            {
                                await (PoiDataChanged?.Invoke(new PoiModel { Id = stalePoiId }) ?? Task.CompletedTask);
                            }
                        }

                        // Defensive cleanup: remove any orphaned content/audio rows that no longer have a parent POI.
                        var livePoiIds = serverIds;
                        var localContents = await _databaseService.GetAllContentsAsync();
                        foreach (var orphanContent in localContents.Where(c => c != null && c.PoiId > 0 && !livePoiIds.Contains(c.PoiId)).ToList())
                        {
                            var removed = await _databaseService.DeleteContentByIdAsync(orphanContent.Id);
                            System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Removed orphan contentId={orphanContent.Id}, rows={removed}, poiId={orphanContent.PoiId}");
                            if (!_isFullSyncInProgress)
                            {
                                await (ContentDataChanged?.Invoke(new ContentModel { Id = orphanContent.Id, PoiId = orphanContent.PoiId, LanguageCode = orphanContent.LanguageCode }) ?? Task.CompletedTask);
                            }
                        }

                        var localAudios = await _databaseService.GetAllAudiosAsync();
                        foreach (var orphanAudio in localAudios.Where(a => a != null && a.PoiId > 0 && !livePoiIds.Contains(a.PoiId)).ToList())
                        {
                            var removed = await _databaseService.DeleteAudioByIdAsync(orphanAudio.Id);
                            System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Removed orphan audioId={orphanAudio.Id}, rows={removed}, poiId={orphanAudio.PoiId}");
                            if (!_isFullSyncInProgress)
                            {
                                await (AudioDataChanged?.Invoke(new AudioModel { Id = orphanAudio.Id, PoiId = orphanAudio.PoiId, LanguageCode = orphanAudio.LanguageCode }) ?? Task.CompletedTask);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Failed to remove stale local POIs: {ex.Message}");
                    }
                }

                // Update local DB with server POIs
                foreach (var poi in serverPois)
                {
                    if (poi == null) continue;

                    if (_databaseService != null)
                    {
                        var hadLocalPoi = localPoiById.TryGetValue(poi.Id, out var localPoi);
                        var hasPoiChanged = !hadLocalPoi || !string.Equals(BuildPoiSyncFingerprint(localPoi), BuildPoiSyncFingerprint(poi), StringComparison.Ordinal);

                        await _databaseService.SavePoiAsync(poi);
                        UpdatePoiQrSnapshot(poi);

                        if (hadLocalPoi && !hasPoiChanged)
                        {
                            continue;
                        }

                        try
                        {
                            if (localizationByPoiId != null
                                && localizationByPoiId.TryGetValue(poi.Id, out var localizationElement)
                                && localizationElement.HasValue
                                && localizationElement.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                var localization = localizationElement.Value;
                                var fallbackContent = new ContentModel
                                {
                                    PoiId = poi.Id,
                                    LanguageCode = localization.TryGetProperty("languageCode", out var lng)
                                        ? (lng.GetString()?.Trim().ToLowerInvariant() ?? "vi")
                                        : "vi",
                                    Title = localization.TryGetProperty("title", out var title) ? title.GetString() : null,
                                    Subtitle = localization.TryGetProperty("subtitle", out var subtitle) ? subtitle.GetString() : null,
                                    Description = localization.TryGetProperty("description", out var description) ? description.GetString() : null,
                                    AudioUrl = localization.TryGetProperty("audio_url", out var audioUrl) ? audioUrl.GetString() : null,
                                    IsTTS = localization.TryGetProperty("isTTS", out var isTts) && isTts.ValueKind == System.Text.Json.JsonValueKind.True,
                                    PriceRange = localization.TryGetProperty("priceRange", out var priceRange) ? priceRange.GetString() : null,
                                    Rating = localization.TryGetProperty("rating", out var rating) && rating.TryGetDouble(out var r) ? r : 0,
                                    OpeningHours = localization.TryGetProperty("openingHours", out var openingHours) ? openingHours.GetString() : null,
                                    PhoneNumber = localization.TryGetProperty("phoneNumber", out var phoneNumber) ? phoneNumber.GetString() : null,
                                    Address = localization.TryGetProperty("address", out var address) ? address.GetString() : null,
                                    ShareUrl = localization.TryGetProperty("shareUrl", out var shareUrl) ? shareUrl.GetString() : null
                                };

                                if (!string.IsNullOrWhiteSpace(fallbackContent.Title)
                                    || !string.IsNullOrWhiteSpace(fallbackContent.Description)
                                    || !string.IsNullOrWhiteSpace(fallbackContent.Address)
                                    || !string.IsNullOrWhiteSpace(fallbackContent.OpeningHours)
                                    || fallbackContent.Rating > 0)
                                {
                                    await _databaseService.SaveContentAsync(fallbackContent);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Failed to store load-all localization for POI {poi.Id}: {ex.Message}");
                        }

                        try
                        {
                            var contents = await _apiService.GetContentsByPoiIdAsync(poi.Id);
                            if (contents != null)
                            {
                                var serverContentIds = contents.Where(c => c != null && c.Id > 0).Select(c => c.Id).ToHashSet();
                                foreach (var content in contents)
                                {
                                    if (content == null) continue;
                                    await _databaseService.SaveContentAsync(content);
                                    if (!_isFullSyncInProgress)
                                    {
                                        await (ContentDataChanged?.Invoke(content) ?? Task.CompletedTask);
                                    }
                                }

                                try
                                {
                                    var localContents = await _databaseService.GetContentsByPoiIdAsync(poi.Id);
                                    var staleLocalContents = localContents
                                        .Where(c => c != null && c.Id > 0 && !serverContentIds.Contains(c.Id))
                                        .ToList();

                                    foreach (var stale in staleLocalContents)
                                    {
                                        var removed = await _databaseService.DeleteContentByIdAsync(stale.Id);
                                        System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Removed stale local contentId={stale.Id}, rows={removed}, poiId={poi.Id}");
                                        if (!_isFullSyncInProgress)
                                        {
                                            await (ContentDataChanged?.Invoke(new ContentModel { Id = stale.Id, PoiId = poi.Id, LanguageCode = stale.LanguageCode }) ?? Task.CompletedTask);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Failed to reconcile stale contents for POI {poi.Id}: {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Failed to sync contents for POI {poi.Id}: {ex.Message}");
                        }

                        try
                        {
                            var audios = await _apiService.GetAudiosByPoiIdAsync(poi.Id);
                            if (audios != null)
                            {
                                var serverAudioIds = audios.Where(a => a != null && a.Id > 0).Select(a => a.Id).ToHashSet();
                                foreach (var audio in audios)
                                {
                                    if (audio == null) continue;
                                    await _databaseService.SaveAudioAsync(audio);
                                    if (!_isFullSyncInProgress)
                                    {
                                        await (AudioDataChanged?.Invoke(audio) ?? Task.CompletedTask);
                                    }
                                }

                                try
                                {
                                    var localAudios = await _databaseService.GetAudiosByPoiAsync(poi.Id);
                                    var staleLocalAudios = localAudios
                                        .Where(a => a != null && a.Id > 0 && !serverAudioIds.Contains(a.Id))
                                        .ToList();

                                    foreach (var stale in staleLocalAudios)
                                    {
                                        var removed = await _databaseService.DeleteAudioByIdAsync(stale.Id);
                                        System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Removed stale local audioId={stale.Id}, rows={removed}, poiId={poi.Id}");
                                        if (!_isFullSyncInProgress)
                                        {
                                            await (AudioDataChanged?.Invoke(new AudioModel { Id = stale.Id, PoiId = poi.Id, LanguageCode = stale.LanguageCode }) ?? Task.CompletedTask);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Failed to reconcile stale audios for POI {poi.Id}: {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Failed to sync audios for POI {poi.Id}: {ex.Message}");
                        }
                    }
                    else
                    {
                        await _poiRepository.SaveAsync(poi);
                    }
                    if (!_isFullSyncInProgress)
                    {
                        await (PoiDataChanged?.Invoke(poi) ?? Task.CompletedTask);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Completed sync of {serverPois.Count} POIs");
                _hasCompletedInitialSync = true;
                await (FullSyncRequested?.Invoke() ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Error syncing POIs: {ex.Message}");
            }
            finally
            {
                _isFullSyncInProgress = false;
                try { _syncThrottleLock.Release(); } catch { }
            }
        }

        private async Task SyncPoiContentsAsync(int poiId)
        {
            try
            {
                if (_databaseService == null || _apiService == null || poiId <= 0) return;

                var now = DateTime.UtcNow;
                if (_lastPoiContentSyncUtc.TryGetValue(poiId, out var lastSyncUtc)
                    && (now - lastSyncUtc) < PoiContentSyncCooldown)
                {
                    return;
                }

                _lastPoiContentSyncUtc[poiId] = now;

                var serverContents = await _apiService.GetContentsByPoiIdAsync(poiId) ?? new List<ContentModel>();
                var serverContentIds = serverContents
                    .Where(c => c != null && c.Id > 0)
                    .Select(c => c.Id)
                    .ToHashSet();

                foreach (var serverContent in serverContents)
                {
                    if (serverContent == null) continue;
                    await _databaseService.SaveContentAsync(serverContent);
                }

                var localContents = await _databaseService.GetContentsByPoiIdAsync(poiId);
                var staleLocalContents = localContents
                    .Where(c => c != null && c.Id > 0 && !serverContentIds.Contains(c.Id))
                    .ToList();

                foreach (var stale in staleLocalContents)
                {
                    var removed = await _databaseService.DeleteContentByIdAsync(stale.Id);
                    System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Removed stale local contentId={stale.Id}, rows={removed}, poiId={poiId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RealtimeSync] Failed to sync realtime contents for poiId={poiId}: {ex.Message}");
            }
        }

        private bool ShouldProcessRealtimeEvent(bool bypassCooldown = false)
        {
            if (bypassCooldown)
            {
                _lastRealtimeEventUtc = DateTime.UtcNow;
                return true;
            }

            var entered = _realtimeEventGate.Wait(0);
            if (!entered)
            {
                return false;
            }

            try
            {
                var now = DateTime.UtcNow;
                if (_lastRealtimeEventUtc != DateTime.MinValue
                    && (now - _lastRealtimeEventUtc) < RealtimeEventCooldown)
                {
                    return false;
                }

                _lastRealtimeEventUtc = now;
                return true;
            }
            finally
            {
                _realtimeEventGate.Release();
            }
        }

        private bool ShouldApplyPoiChange(PoiModel poi)
        {
            if (poi == null || poi.Id <= 0) return true;

            var fingerprint = BuildPoiSyncFingerprint(poi);
            if (_poiChangeFingerprint.TryGetValue(poi.Id, out var existing)
                && string.Equals(existing, fingerprint, StringComparison.Ordinal))
            {
                return false;
            }

            _poiChangeFingerprint[poi.Id] = fingerprint;
            return true;
        }

        private bool ShouldBypassRealtimeCooldownForPoi(PoiModel poi)
        {
            if (poi == null || poi.Id <= 0) return false;

            var currentQr = NormalizeQr(poi.QrCode);
            if (!_poiQrSnapshot.TryGetValue(poi.Id, out var previousQr))
            {
                return !string.IsNullOrWhiteSpace(currentQr);
            }

            return !string.Equals(previousQr, currentQr, StringComparison.Ordinal);
        }

        private void UpdatePoiQrSnapshot(PoiModel poi)
        {
            if (poi == null || poi.Id <= 0) return;
            _poiQrSnapshot[poi.Id] = NormalizeQr(poi.QrCode);
        }

        private static string BuildPoiSyncFingerprint(PoiModel poi)
        {
            if (poi == null) return string.Empty;

            return string.Join("|",
                poi.Name?.Trim() ?? string.Empty,
                poi.Category?.Trim() ?? string.Empty,
                poi.Latitude,
                poi.Longitude,
                poi.Radius,
                poi.Priority,
                poi.CooldownSeconds,
                poi.ImageUrl?.Trim() ?? string.Empty,
                poi.WebsiteUrl?.Trim() ?? string.Empty,
                NormalizeQr(poi.QrCode),
                poi.IsPublished);
        }

        private static string NormalizeQr(string qrCode)
        {
            return string.IsNullOrWhiteSpace(qrCode) ? string.Empty : qrCode.Trim();
        }

        private bool ShouldApplyAudioChange(AudioModel audio)
        {
            if (audio == null || audio.Id <= 0) return true;

            var fingerprint = string.Join("|",
                audio.PoiId,
                audio.Url?.Trim() ?? string.Empty,
                audio.LanguageCode?.Trim().ToLowerInvariant() ?? string.Empty,
                audio.IsTts,
                audio.IsProcessed);

            if (_audioChangeFingerprint.TryGetValue(audio.Id, out var existing)
                && string.Equals(existing, fingerprint, StringComparison.Ordinal))
            {
                return false;
            }

            _audioChangeFingerprint[audio.Id] = fingerprint;
            return true;
        }

        private bool ShouldApplyContentChange(ContentModel content)
        {
            if (content == null || content.Id <= 0) return true;

            var fingerprint = string.Join("|",
                content.PoiId,
                content.LanguageCode?.Trim().ToLowerInvariant() ?? string.Empty,
                content.Title?.Trim() ?? string.Empty,
                content.Subtitle?.Trim() ?? string.Empty,
                content.Description?.Trim() ?? string.Empty,
                content.AudioUrl?.Trim() ?? string.Empty,
                content.IsTTS,
                content.PriceRange?.Trim() ?? string.Empty,
                content.Rating,
                content.OpeningHours?.Trim() ?? string.Empty,
                content.PhoneNumber?.Trim() ?? string.Empty,
                content.Address?.Trim() ?? string.Empty,
                content.ShareUrl?.Trim() ?? string.Empty);

            if (_contentChangeFingerprint.TryGetValue(content.Id, out var existing)
                && string.Equals(existing, fingerprint, StringComparison.Ordinal))
            {
                return false;
            }

            _contentChangeFingerprint[content.Id] = fingerprint;
            return true;
        }

        private static string NormalizeLanguageCode(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return "vi";

            var normalized = language.Trim().ToLowerInvariant();
            if (normalized.Contains('-'))
            {
                normalized = normalized.Split('-')[0];
            }
            else if (normalized.Contains('_'))
            {
                normalized = normalized.Split('_')[0];
            }

            return normalized;
        }
    }
}
