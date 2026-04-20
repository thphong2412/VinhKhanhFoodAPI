using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace VinhKhanh.Pages
{
    public partial class MapPage
    {
        private CancellationTokenSource? _startupDeferredWorkCts;

        protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
        {
            base.OnNavigatedFrom(args);
            _isTrackingActive = false;
            Interlocked.Increment(ref _detailRequestVersion);
            try
            {
                _appearingCts?.Cancel();
                _appearingCts?.Dispose();
                _appearingCts = null;

                _searchDebounceCts?.Cancel();
                _searchDebounceCts?.Dispose();
                _searchDebounceCts = null;

                _detailCts?.Cancel();
                _detailCts?.Dispose();
                _detailCts = null;

                _realtimeMapRefreshCts?.Cancel();
                _realtimeMapRefreshCts?.Dispose();
                _realtimeMapRefreshCts = null;

                _realtimeHighlightsRefreshCts?.Cancel();
                _realtimeHighlightsRefreshCts?.Dispose();
                _realtimeHighlightsRefreshCts = null;

                _realtimeDetailRefreshCts?.Cancel();
                _realtimeDetailRefreshCts?.Dispose();
                _realtimeDetailRefreshCts = null;

                _languageRefreshCts?.Cancel();
                _languageRefreshCts?.Dispose();
                _languageRefreshCts = null;

                _startupDeferredWorkCts?.Cancel();
                _startupDeferredWorkCts?.Dispose();
                _startupDeferredWorkCts = null;

                try
                {
                    Connectivity.ConnectivityChanged -= OnConnectivityChanged;
                }
                catch { }

                try
                {
                    if (_isRealtimeEventsSubscribed && _realtimeSyncManager != null)
                    {
                        _realtimeSyncManager.PoiDataChanged -= HandleRealtimePoiChanged;
                        _realtimeSyncManager.ContentDataChanged -= HandleRealtimeContentChanged;
                        _realtimeSyncManager.AudioDataChanged -= HandleRealtimeAudioChanged;
                        _realtimeSyncManager.FullSyncRequested -= HandleRealtimeFullSyncRequested;
                        _isRealtimeEventsSubscribed = false;
                    }
                }
                catch { }
            }
            catch { }
        }

        private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (e.NetworkAccess != NetworkAccess.Internet && _offlineMapEnabled)
                    {
                        await TrySwitchToOfflineMapAsync();
                        UpdateOfflineMapStatusUi(await GetOfflineMapStatusTextAsync("offline_using"));
                        return;
                    }

                    if (MapboxOfflineWebView != null && vinhKhanhMap != null)
                    {
                        MapboxOfflineWebView.IsVisible = false;
                        MapboxOfflineWebView.InputTransparent = true;
                        vinhKhanhMap.IsVisible = true;
                        UpdateOfflineMapStatusUi(_offlineMapEnabled
                            ? await GetOfflineMapStatusTextAsync("online_with_offline_ready")
                            : await GetOfflineMapStatusTextAsync("online"));
                    }

                    if (e.NetworkAccess != NetworkAccess.Internet && !_offlineMapEnabled)
                    {
                        UpdateOfflineMapStatusUi(await GetOfflineMapStatusTextAsync("offline_no_pack"));
                    }
                });
            }
            catch { }
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (_isPageInitializing) return;

            _appearingCts?.Cancel();
            _appearingCts?.Dispose();
            _appearingCts = new CancellationTokenSource();

            await InitializeOnAppearingAsync(_appearingCts.Token);
        }

        private async Task InitializeOnAppearingAsync(CancellationToken cancellationToken)
        {
            _isPageInitializing = true;
            Interlocked.Increment(ref _mapRefreshVersion);
            SetMapLoadingState(false);

            try
            {
                EnsureRealtimeSyncSubscriptions();
                if (_realtimeSyncManager != null && !_realtimeSyncManager.IsConnected)
                {
                    AddLog("Realtime chưa kết nối, đang kết nối lại...");
                    _ = Task.Run(async () =>
                    {
                        try { await _realtimeSyncManager.StartAsync(); } catch { }
                    });
                }

                // Load local data first to avoid startup ANR/freeze.
                _pois = await _dbService.GetPoisAsync();
                RefreshGeofencePoisFromCurrentState();
                if (cancellationToken.IsCancellationRequested) return;
                if (Shell.Current?.CurrentPage is not MapPage) return;

                // Keep old seed behavior when completely empty.
                if (_pois == null || !_pois.Any())
                {
                    try
                    {
                        await EnsureApiBaseReadyAsync();
                        if (_realtimeSyncManager != null)
                        {
                            await RunSingleFullSyncAndApplyUiAsync("Synced {0} POIs from Admin/API");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"Sync from Admin/API failed: {ex.Message}");
                    }

                    AddLog("No POIs from server, seeding sample data...");
                    await SeedFullData();
                    _pois = await _dbService.GetPoisAsync();
                }
                else
                {
                    AddLog($"Loaded {_pois.Count} POIs from server");
                }

                if (cancellationToken.IsCancellationRequested) return;
                if (Shell.Current?.CurrentPage is not MapPage) return;

                // Fast pin render first
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    try
                    {
                        if (MapboxOfflineWebView != null)
                        {
                            MapboxOfflineWebView.Source = null;
                            MapboxOfflineWebView.IsVisible = false;
                            MapboxOfflineWebView.InputTransparent = true;
                        }

                        if (vinhKhanhMap != null)
                        {
                            vinhKhanhMap.IsVisible = true;
                            vinhKhanhMap.Opacity = 1;
                            vinhKhanhMap.InputTransparent = false;
                            vinhKhanhMap.IsEnabled = true;
                        }

                        AddPoisToMap();
                        BtnShowSaved.IsVisible = _pois.Any(p => p.IsSaved);

                        // reset map visibility to avoid stale blank state from previous fallback
                        if (MapboxOfflineWebView != null && vinhKhanhMap != null)
                        {
                            var hasOfflineToken = !string.IsNullOrWhiteSpace(_runtimeMapboxToken)
                                || !string.IsNullOrWhiteSpace(Preferences.Default.Get("runtime_mapbox_token", string.Empty));
                            var canUseOffline = _offlineMapEnabled
                                && Connectivity.NetworkAccess != NetworkAccess.Internet
                                && hasOfflineToken;

                            MapboxOfflineWebView.IsVisible = canUseOffline;
                            MapboxOfflineWebView.InputTransparent = !canUseOffline;
                            vinhKhanhMap.IsVisible = !canUseOffline;
                            vinhKhanhMap.InputTransparent = canUseOffline;
                        }
                    }
                    catch { }
                });

                // Defer heavier localized pin rendering to background to avoid blocking first frame
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        if (Shell.Current?.CurrentPage is not MapPage) return;
                        // Use background thread to build data then update UI on MainThread
                        await DisplayAllPois(cancellationToken);
                    }
                    catch { }
                });

                _startupDeferredWorkCts?.Cancel();
                _startupDeferredWorkCts?.Dispose();
                _startupDeferredWorkCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _ = RunDeferredStartupWorkAsync(_startupDeferredWorkCts.Token);

                _ = Task.Run(async () =>
                {
                    try { await EnsureTrackingStartedAsync(); } catch { }
                });

                try
                {
                    if (PoiDetailPanel?.IsVisible == true)
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            try { PoiDetailPanel.IsVisible = false; } catch { }
                        });
                    }
                }
                catch { }

                // Do not block UI thread while checking map rendering/location
                _ = Task.Run(async () =>
                {
                    try { await CenterMapOnUserFirstAsync(); } catch { }
                    if (cancellationToken.IsCancellationRequested) return;
                    if (Shell.Current?.CurrentPage is not MapPage) return;
                    try { await CheckMapDisplayAsync(); } catch { }
                });

                try
                {
                    Connectivity.ConnectivityChanged -= OnConnectivityChanged;
                    Connectivity.ConnectivityChanged += OnConnectivityChanged;
                }
                catch { }

                await TrySwitchToOfflineMapAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi load dữ liệu: {ex.Message}");
                SetMapLoadingState(false);
            }
            finally
            {
                _isPageInitializing = false;
            }
        }

        private async Task RunDeferredStartupWorkAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(900, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                if (Shell.Current?.CurrentPage is not MapPage) return;

                try
                {
                    var highlights = (_pois ?? new List<VinhKhanh.Shared.PoiModel>())
                        .OrderByDescending(p => p.Priority)
                        .Take(6)
                        .ToList();
                    await RenderHighlightsAsync(highlights, lightweight: true);
                }
                catch { }

                if (cancellationToken.IsCancellationRequested) return;

                try
                {
                    await DisplayAllPois(cancellationToken);
                }
                catch { }

                try
                {
                    var highlights = (_pois ?? new List<VinhKhanh.Shared.PoiModel>())
                        .OrderByDescending(p => p.Priority)
                        .Take(6)
                        .ToList();
                    await RenderHighlightsAsync(highlights, lightweight: false);
                }
                catch { }

                if (cancellationToken.IsCancellationRequested) return;
                _geofenceEngine?.UpdatePois(_pois);

                if (_realtimeSyncManager == null) return;
                if (_backgroundFullSyncTask != null && !_backgroundFullSyncTask.IsCompleted) return;

                var shouldBackgroundSync = false;
                try
                {
                    // Keep startup smooth: do not trigger automatic full-sync in deferred startup.
                    shouldBackgroundSync = false;
                }
                catch { }

                if (!shouldBackgroundSync || cancellationToken.IsCancellationRequested) return;

                _backgroundFullSyncTask = Task.Run(async () =>
                {
                    try
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        AddLog("Syncing POIs from server...");
                        await RunSingleFullSyncAndApplyUiAsync();
                    }
                    catch { }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch { }
        }

        private async Task RunSingleFullSyncAndApplyUiAsync(string? successLogFormat = null)
        {
            if (_realtimeSyncManager == null) return;

            await _fullSyncGate.WaitAsync();
            try
            {
                _suppressNextRealtimeFullSyncEvent = true;
                await _realtimeSyncManager.SyncAllPoisAsync();
                var syncedPois = await _dbService.GetPoisAsync();
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    _pois = syncedPois;
                    if (_pois == null) _pois = new();
                    AddPoisToMap();
                    try { BtnShowSaved.IsVisible = _pois.Any(p => p.IsSaved); } catch { }
                    var highlights = _pois.OrderByDescending(p => p.Priority).Take(6).ToList();
                    await RenderHighlightsAsync(highlights);
                    if (!string.IsNullOrWhiteSpace(successLogFormat))
                    {
                        AddLog(string.Format(successLogFormat, _pois.Count));
                    }
                });
            }
            finally
            {
                _fullSyncGate.Release();
            }
        }
    }
}
