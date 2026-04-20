using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using VinhKhanh.Shared;

namespace VinhKhanh.Pages
{
    public partial class MapPage
    {
        private DateTime _lastRealtimeMapRefreshUtc = DateTime.MinValue;
        private DateTime _lastRealtimeHighlightsRefreshUtc = DateTime.MinValue;
        private DateTime _lastRealtimeDetailRefreshUtc = DateTime.MinValue;
        private static readonly TimeSpan RealtimeMapRefreshCooldown = TimeSpan.FromSeconds(2.4);
        private static readonly TimeSpan RealtimeHighlightsRefreshCooldown = TimeSpan.FromSeconds(3.2);
        private static readonly TimeSpan RealtimeDetailRefreshCooldown = TimeSpan.FromSeconds(2.2);

        private void EnsureRealtimeSyncSubscriptions()
        {
            if (_isRealtimeEventsSubscribed || _realtimeSyncManager == null) return;

            _realtimeSyncManager.PoiDataChanged += HandleRealtimePoiChanged;
            _realtimeSyncManager.ContentDataChanged += HandleRealtimeContentChanged;
            _realtimeSyncManager.AudioDataChanged += HandleRealtimeAudioChanged;
            _realtimeSyncManager.FullSyncRequested += HandleRealtimeFullSyncRequested;
            _isRealtimeEventsSubscribed = true;
        }

        private async Task HandleRealtimePoiChanged(PoiModel poi)
        {
            try
            {
                _ = ScheduleRealtimeMapRefreshAsync(refreshSelectedPoi: true);
                _ = PushPoisToOfflineMapAsync();
            }
            catch { }

            await Task.CompletedTask;
        }

        private async Task HandleRealtimeContentChanged(ContentModel content)
        {
            try
            {
                if (_selectedPoi != null
                    && content != null
                    && content.PoiId == _selectedPoi.Id
                    && PoiDetailPanel != null
                    && PoiDetailPanel.IsVisible)
                {
                    _ = ScheduleRealtimeSelectedPoiDetailRefreshAsync();
                }

                _ = ScheduleRealtimeHighlightsRefreshAsync();
            }
            catch { }

            await Task.CompletedTask;
        }

        private async Task ScheduleRealtimeSelectedPoiDetailRefreshAsync()
        {
            try
            {
                _realtimeDetailRefreshCts?.Cancel();
                _realtimeDetailRefreshCts?.Dispose();
                _realtimeDetailRefreshCts = new CancellationTokenSource();
                var token = _realtimeDetailRefreshCts.Token;

                await Task.Delay(320, token);
                if (token.IsCancellationRequested) return;

                var now = DateTime.UtcNow;
                if ((now - _lastRealtimeDetailRefreshUtc) < RealtimeDetailRefreshCooldown)
                {
                    return;
                }

                _lastRealtimeDetailRefreshUtc = now;

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (token.IsCancellationRequested) return;
                    if (_selectedPoi == null) return;
                    await ShowPoiDetail(_selectedPoi);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch { }
        }

        private async Task HandleRealtimeAudioChanged(AudioModel audio)
        {
            try
            {
                if (audio == null) return;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (_selectedPoi != null && audio.PoiId == _selectedPoi.Id)
                    {
                        AddLog($"Audio cập nhật cho POI #{audio.PoiId}");
                    }

                    return Task.CompletedTask;
                });
            }
            catch { }

            await Task.CompletedTask;
        }

        private async Task HandleRealtimeFullSyncRequested()
        {
            try
            {
                if (_suppressNextRealtimeFullSyncEvent)
                {
                    _suppressNextRealtimeFullSyncEvent = false;
                    return;
                }

                _ = ScheduleRealtimeMapRefreshAsync(refreshSelectedPoi: true);
            }
            catch { }

            await Task.CompletedTask;
        }

        private async Task ScheduleRealtimeMapRefreshAsync(bool refreshSelectedPoi)
        {
            try
            {
                _realtimeMapRefreshCts?.Cancel();
                _realtimeMapRefreshCts?.Dispose();
                _realtimeMapRefreshCts = new CancellationTokenSource();
                var token = _realtimeMapRefreshCts.Token;

                await Task.Delay(620, token);
                if (token.IsCancellationRequested) return;

                var now = DateTime.UtcNow;
                if ((now - _lastRealtimeMapRefreshUtc) < RealtimeMapRefreshCooldown)
                {
                    return;
                }

                var updatedPois = await _dbService.GetPoisAsync();
                if (token.IsCancellationRequested) return;

                _lastRealtimeMapRefreshUtc = now;

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    _pois = updatedPois ?? new List<PoiModel>();
                    RefreshGeofencePoisFromCurrentState();
                    AddPoisToMap();
                    try { BtnShowSaved.IsVisible = _pois.Any(p => p.IsSaved); } catch { }

                    _ = ScheduleRealtimeHighlightsRefreshAsync();

                    if (refreshSelectedPoi && _selectedPoi != null)
                    {
                        var refreshedSelected = _pois.FirstOrDefault(p => p.Id == _selectedPoi.Id);
                        if (refreshedSelected == null)
                        {
                            _selectedPoi = null;
                            if (PoiDetailPanel != null) PoiDetailPanel.IsVisible = false;
                        }
                        else if (PoiDetailPanel?.IsVisible == true)
                        {
                            _selectedPoi = refreshedSelected;
                            await ShowPoiDetail(refreshedSelected);
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch { }
        }

        private async Task ScheduleRealtimeHighlightsRefreshAsync()
        {
            try
            {
                _realtimeHighlightsRefreshCts?.Cancel();
                _realtimeHighlightsRefreshCts?.Dispose();
                _realtimeHighlightsRefreshCts = new CancellationTokenSource();
                var token = _realtimeHighlightsRefreshCts.Token;

                await Task.Delay(760, token);
                if (token.IsCancellationRequested) return;

                var now = DateTime.UtcNow;
                if ((now - _lastRealtimeHighlightsRefreshUtc) < RealtimeHighlightsRefreshCooldown)
                {
                    return;
                }

                _lastRealtimeHighlightsRefreshUtc = now;

                var top = (_pois ?? new List<PoiModel>()).OrderByDescending(p => p.Priority).Take(6).ToList();
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (token.IsCancellationRequested) return;
                    await RenderHighlightsAsync(top);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch { }
        }
    }
}
