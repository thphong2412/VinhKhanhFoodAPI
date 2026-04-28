using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;
using VinhKhanh.Shared;

namespace VinhKhanh.Pages
{
    public partial class MapPage
    {
        // Japanese and Korean selection handlers
        // Close POI panel when clicking on map background
        private void OnMapClicked(object sender, Microsoft.Maui.Controls.Maps.MapClickedEventArgs e)
        {
            try
            {
                if (PoiDetailPanel != null && PoiDetailPanel.IsVisible)
                {
                    try { _ = _audioQueue?.StopAsync(); } catch { }
                    try { _narrationService?.Stop(); } catch { }
                    try { _ = _audioService?.StopAsync(); } catch { }
                    HideMiniPlayer();
                    PoiDetailPanel.IsVisible = false;
                }

                if (HighlightsPanel != null && _selectedPoi == null)
                {
                    HighlightsPanel.IsVisible = true;
                }
            }
            catch { }
        }

        // When user taps highlight item
        private async void OnHighlightSelected(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var selVm = e.CurrentSelection?.FirstOrDefault() as VinhKhanh.Shared.HighlightViewModel;
                var sel = selVm?.Poi;
                if (sel == null) return;
                await OpenPoiDetailFromSelectionAsync(sel, "highlight_select", userInitiated: true);
                // Clear selection so tapping the same card again still triggers SelectionChanged
                try
                {
                    if (sender is CollectionView cv) cv.SelectedItem = null;
                }
                catch { }
            }
            catch { }
        }

        // Called when user taps image or name inside highlight card
        private async void OnHighlightItemTapped(object sender, EventArgs e)
        {
            try
            {
                // Determine binding context from sender or parent
                PoiModel poi = null;
                if (sender is VisualElement ve && ve.BindingContext is VinhKhanh.Shared.HighlightViewModel hvm && hvm.Poi != null)
                {
                    poi = hvm.Poi;
                }
                else
                {
                    // fall back: try to find nearest BindingContext up the visual tree
                    if (sender is Element elem)
                    {
                        var current = elem;
                        while (current != null && !(current is CollectionView))
                        {
                            if (current.BindingContext is VinhKhanh.Shared.HighlightViewModel bc && bc.Poi != null)
                            {
                                poi = bc.Poi;
                                break;
                            }
                            current = current.Parent;
                        }
                    }
                }

                if (poi != null)
                {
                    await OpenPoiDetailFromSelectionAsync(poi, "highlight_tap_image_or_name", userInitiated: true);
                }
            }
            catch { }
        }

        private async void OnHighlightsTitleTapped(object sender, EventArgs e)
        {
            try
            {
                // open full highlights list
                try
                {
                    var list = new VinhKhanh.Pages.HighlightsListPage(
                        _pois.OrderByDescending(p => p.Priority).ToList(),
                        _currentLanguage,
                        _dbService,
                        _apiService,
                        onPoiSelected: HandleHighlightListPoiSelectedAsync);
                    await Navigation.PushAsync(list);
                }
                catch
                {
                    await ShowHighlightsListFallback(_pois.OrderByDescending(p => p.Priority).ToList());
                }
            }
            catch { }
        }

        private async void OnViewAllHighlightsClicked(object sender, EventArgs e)
        {
            try
            {
                // Open highlights list page
                try
                {
                    var fullList = (_pois ?? new List<PoiModel>())
                        .Where(p => p != null)
                        .DistinctBy(p => p.Id)
                        .OrderByDescending(p => p.Priority)
                        .ThenBy(p => p.Name)
                        .ToList();

                    var list = new VinhKhanh.Pages.HighlightsListPage(
                        fullList,
                        _currentLanguage,
                        _dbService,
                        _apiService,
                        onPoiSelected: HandleHighlightListPoiSelectedAsync);
                    await Navigation.PushAsync(list);
                }
                catch
                {
                    await ShowHighlightsListFallback((_pois ?? new List<PoiModel>())
                        .Where(p => p != null)
                        .DistinctBy(p => p.Id)
                        .OrderByDescending(p => p.Priority)
                        .ThenBy(p => p.Name)
                        .ToList());
                }
            }
            catch { }
        }

        // Khi user chạm 1 quán trong "Địa điểm thịnh hành" → mở POI detail card trên map
        // (HighlightsListPage tự động pop về MapPage sau khi gọi callback này)
        private Task HandleHighlightListPoiSelectedAsync(PoiModel poi)
        {
            return OpenPoiDetailFromSelectionAsync(poi, "highlight_list_select", userInitiated: true);
        }

        // tapped on the highlight card (frame)
        private async void OnHighlightTapped(object sender, EventArgs e)
        {
            try
            {
                // frame's BindingContext is HighlightViewModel
                if (sender is VisualElement ve && ve.BindingContext is VinhKhanh.Shared.HighlightViewModel vm && vm.Poi != null)
                {
                    await OpenPoiDetailFromSelectionAsync(vm.Poi, "highlight_tap", userInitiated: true);
                }
            }
            catch { }
        }

        private async Task OpenPoiDetailFromSelectionAsync(PoiModel poi, string trigger, bool userInitiated)
        {
            if (poi == null) return;

            try
            {
                _selectedPoi = poi;
                _ = TrackPoiEventAsync("poi_click", poi.Id, $"\"trigger\":\"{trigger}\",\"lang\":\"{NormalizeLanguageCode(_currentLanguage)}\"");

                if (HighlightsPanel != null)
                {
                    HighlightsPanel.IsVisible = true;
                    SetHighlightsExpandedState(false);
                }

                await ShowPoiDetail(poi, userInitiated);
            }
            catch
            {
                _selectedPoi = null;
                try
                {
                    if (PoiDetailPanel != null) PoiDetailPanel.IsVisible = false;
                    if (HighlightsPanel != null) HighlightsPanel.IsVisible = true;
                }
                catch { }
            }
        }

        private void OnToggleHighlightsClicked(object sender, EventArgs e)
        {
            try
            {
                if (HighlightsPanel == null) return;
                var expandNow = !_isHighlightsExpanded;
                SetHighlightsExpandedState(expandNow);

                if (expandNow)
                {
                    _ = MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        try
                        {
                            await Task.Delay(120);
                            CvHighlights?.ScrollTo(0, position: ScrollToPosition.Start, animate: true);
                        }
                        catch { }
                    });
                }
            }
            catch { }
        }

        private async void OnViewSavedClicked(object sender, EventArgs e)
        {
            try
            {
                _ = TrackPoiEventAsync("poi_click", 0, $"\"trigger\":\"saved_shortcut\",\"lang\":\"{NormalizeLanguageCode(_currentLanguage)}\"");
                await ShowSavedPoisInHighlightsAsync();
            }
            catch { }
        }

        protected override void OnNavigatedTo(NavigatedToEventArgs args)
        {
            base.OnNavigatedTo(args);
        }

        // Legacy fallback - viewport throttling is handled in MapPage.MapRendering.cs
        private void OnMapVisibleRegionChanged(object sender, EventArgs e) { }

        private async Task CheckMapDisplayAsync()
        {
            try
            {
                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    try
                    {
                        if (MapboxOfflineWebView != null)
                        {
                            MapboxOfflineWebView.IsVisible = false;
                            MapboxOfflineWebView.InputTransparent = true;
                        }

                        if (vinhKhanhMap != null)
                        {
                            vinhKhanhMap.IsVisible = true;
                            vinhKhanhMap.InputTransparent = false;
                        }
                    }
                    catch { }
                }

                // wait a short time for map to initialize
                await Task.Delay(2500);
                // If VisibleRegion is null or center NaN, consider map not rendered
                if (vinhKhanhMap == null || vinhKhanhMap.VisibleRegion == null || double.IsNaN(vinhKhanhMap.VisibleRegion.Center?.Latitude ?? double.NaN))
                {
                    AddLog("Map control did not render. Keep GG map visible and retry positioning.");
                    try
                    {
                        if (MapboxOfflineWebView != null)
                        {
                            MapboxOfflineWebView.IsVisible = false;
                            MapboxOfflineWebView.InputTransparent = true;
                        }

                        if (vinhKhanhMap != null)
                        {
                            vinhKhanhMap.IsVisible = true;
                            vinhKhanhMap.InputTransparent = false;
                            CenterMapOnVinhKhanh();
                        }
                    }
                    catch { }
                }
                else
                {
                    AddLog("Map rendered successfully.");
                }

                SetMapLoadingState(false);
            }
            catch (Exception ex)
            {
                AddLog($"CheckMapDisplay error: {ex.Message}");
                SetMapLoadingState(false);
            }
        }

        // Make handler public so XAML loader can find it reliably
        public void OnMenuClicked(object sender, EventArgs e)
        {
            // If a POI detail is open, close it when opening the language menu
            if (PoiDetailPanel != null && PoiDetailPanel.IsVisible)
                PoiDetailPanel.IsVisible = false;

            if (HighlightsPanel != null)
                HighlightsPanel.IsVisible = false;

            if (GpsButtonFrame != null)
                GpsButtonFrame.IsVisible = false;

            _isLanguageModalOpen = true;
            LanguagePanel.IsVisible = true;
            // Update the visual state of language buttons
            UpdateLanguageSelectionUI();
        }

        private void OnCloseMenuClicked(object sender, EventArgs e)
        {
            _isLanguageModalOpen = false;
            LanguagePanel.IsVisible = false;

            try
            {
                if (GpsButtonFrame != null)
                    GpsButtonFrame.IsVisible = true;

                if (HighlightsPanel != null && _selectedPoi == null)
                    HighlightsPanel.IsVisible = true;
            }
            catch { }
        }

        // Tabs click handlers
        private void OnTabOverviewClicked(object sender, EventArgs e)
        {
            try
            {
                var overview = this.FindByName<VisualElement>("OverviewPanel");
                var intro = this.FindByName<VisualElement>("IntroPanel");
                var review = this.FindByName<VisualElement>("ReviewPanel");
                var tabO = this.FindByName<Button>("TabOverview");
                var tabI = this.FindByName<Button>("TabIntro");
                var tabR = this.FindByName<Button>("TabReview");
                if (overview != null) overview.IsVisible = true;
                if (intro != null) intro.IsVisible = false;
                if (review != null) review.IsVisible = false;
                if (tabO != null) tabO.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#00796B");
                if (tabI != null) tabI.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                if (tabR != null) tabR.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                if (tabO != null) tabO.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#E3F2FD");
                if (tabI != null) tabI.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent");
                if (tabR != null) tabR.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent");
            }
            catch { }
        }

        private void OnTabIntroClicked(object sender, EventArgs e)
        {
            try
            {
                var overview = this.FindByName<VisualElement>("OverviewPanel");
                var intro = this.FindByName<VisualElement>("IntroPanel");
                var review = this.FindByName<VisualElement>("ReviewPanel");
                var tabO = this.FindByName<Button>("TabOverview");
                var tabI = this.FindByName<Button>("TabIntro");
                var tabR = this.FindByName<Button>("TabReview");
                if (overview != null) overview.IsVisible = false;
                if (intro != null) intro.IsVisible = true;
                if (review != null) review.IsVisible = false;
                if (tabO != null) tabO.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                if (tabI != null) tabI.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#00796B");
                if (tabR != null) tabR.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                if (tabI != null) tabI.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#E3F2FD");
                if (tabO != null) tabO.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent");
                if (tabR != null) tabR.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent");
            }
            catch { }
        }

        private void OnTabReviewClicked(object sender, EventArgs e)
        {
            try
            {
                var overview = this.FindByName<VisualElement>("OverviewPanel");
                var intro = this.FindByName<VisualElement>("IntroPanel");
                var review = this.FindByName<VisualElement>("ReviewPanel");
                var tabO = this.FindByName<Button>("TabOverview");
                var tabI = this.FindByName<Button>("TabIntro");
                var tabR = this.FindByName<Button>("TabReview");
                if (overview != null) overview.IsVisible = false;
                if (intro != null) intro.IsVisible = false;
                if (review != null) review.IsVisible = true;
                if (tabO != null) tabO.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                if (tabI != null) tabI.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray");
                if (tabR != null) tabR.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#00796B");
                if (tabR != null) tabR.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#E3F2FD");
                if (tabO != null) tabO.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent");
                if (tabI != null) tabI.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent");
            }
            catch { }
        }

        private async void OnCloseDetailClicked(object sender, EventArgs e)
        {
            try
            {
                // stop any ongoing narration immediately when closing the POI detail
                try { if (_audioQueue != null) await _audioQueue.StopAsync(); else _narrationService?.Stop(); } catch { }
                try { if (_audioService != null) await _audioService.StopAsync(); } catch { }
                // also clear local speaking flag so user can start again
                _isSpeaking = false;
                HideMiniPlayer();
            }
            catch { }

            PoiDetailPanel.IsVisible = false;
            _selectedPoi = null;

            try
            {
                if (!_isLanguageModalOpen && HighlightsPanel != null)
                {
                    HighlightsPanel.IsVisible = true;
                    SetHighlightsExpandedState(_isHighlightsExpanded);
                }
            }
            catch { }

            try
            {
                if (CvHighlights != null)
                {
                    CvHighlights.SelectedItem = null;
                }
            }
            catch { }
        }

        private void CenterMapOnVinhKhanh()
        {
            try
            {
                if (MainThread.IsMainThread)
                {
                    vinhKhanhMap?.MoveToRegion(MapSpan.FromCenterAndRadius(new Location(10.7584, 106.7058), Distance.FromKilometers(0.4)));
                    return;
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        vinhKhanhMap?.MoveToRegion(MapSpan.FromCenterAndRadius(new Location(10.7584, 106.7058), Distance.FromKilometers(0.4)));
                    }
                    catch { }
                });
            }
            catch { }
        }

        private async Task CenterMapOnUserFirstAsync()
        {
            try
            {
                var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)));
                if (location != null)
                {
                    _lastLocation = location;
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        try
                        {
                            vinhKhanhMap?.MoveToRegion(MapSpan.FromCenterAndRadius(location, Distance.FromKilometers(0.5)));
                        }
                        catch { }
                    });
                    return;
                }
            }
            catch { }

            CenterMapOnVinhKhanh();
        }

        // ================== CÁC NÚT BẤM UI ==================
        private async void OnMyLocationClicked(object sender, EventArgs e)
        {
            try
            {
                var location = await Geolocation.Default.GetLocationAsync();
                if (location != null)
                     vinhKhanhMap.MoveToRegion(MapSpan.FromCenterAndRadius(location, Distance.FromKilometers(0.15)));
            }
            catch
            {
                var t = await GetDialogTextsAsync();
                await DisplayAlert(t["error"], t["permission_denied_msg"], t["ok"]);
            }
        }

        private void OnZoomInClicked(object sender, EventArgs e)
        {
            ZoomMap(0.7);
        }

        private void OnZoomOutClicked(object sender, EventArgs e)
        {
            ZoomMap(1.4);
        }

        private void ZoomMap(double factor)
        {
            try
            {
                if (vinhKhanhMap == null || factor <= 0) return;

                var region = vinhKhanhMap.VisibleRegion;
                var center = region?.Center
                    ?? _lastLocation
                    ?? new Location(10.7584, 106.7058);

                // Radius-based zoom is more stable across devices/emulator than raw LatitudeDegrees fallback.
                var currentRadiusKm = region != null
                    ? Math.Clamp((region.LatitudeDegrees * 111d) / 2d, 0.03, 20d)
                    : 0.15d;

                var nextRadiusKm = Math.Clamp(currentRadiusKm * factor, 0.03, 20d);
                vinhKhanhMap.MoveToRegion(MapSpan.FromCenterAndRadius(center, Distance.FromKilometers(nextRadiusKm)));
            }
            catch { }
        }

        private async void OnForceSyncNowClicked(object sender, EventArgs e)
        {
            if (_realtimeSyncManager == null)
            {
                var t = await GetDialogTextsAsync();
                await DisplayAlert(t["sync"], t["sync_service_missing"], t["ok"]);
                return;
            }

            try
            {
                if (BtnForceSyncNow != null)
                {
                    var syncDialog = await GetDialogTextsAsync();
                    BtnForceSyncNow.IsEnabled = false;
                    BtnForceSyncNow.Text = syncDialog["syncing"];
                }

                var syncedCount = await RunFastPoiSyncAndApplyUiAsync();
                var syncText = await GetDialogTextsAsync();
                AddLog(string.Format(syncText["sync_done_log"], syncedCount));

                if (_selectedPoi != null && PoiDetailPanel?.IsVisible == true)
                {
                    await ShowPoiDetail(_selectedPoi);
                }

                if (_backgroundFullSyncTask == null || _backgroundFullSyncTask.IsCompleted)
                {
                    _backgroundFullSyncTask = Task.Run(async () =>
                    {
                        try
                        {
                            await RunSingleFullSyncAndApplyUiAsync();
                        }
                        catch { }
                    });
                }

                var t = await GetDialogTextsAsync();
                await DisplayAlert(t["sync"], t["sync_success"], t["ok"]);
            }
            catch (Exception ex)
            {
                AddLog($"Force sync failed: {ex.Message}");
                var t = await GetDialogTextsAsync();
                await DisplayAlert(t["sync"], t["sync_failed"], t["ok"]);
            }
            finally
            {
                if (BtnForceSyncNow != null)
                {
                    var ui = await BuildDynamicUiTextAsync(_currentLanguage);
                    BtnForceSyncNow.IsEnabled = true;
                    BtnForceSyncNow.Text = ui["force_sync_now"];
                }
            }
        }
    }
}