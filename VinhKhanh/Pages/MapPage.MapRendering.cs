using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using VinhKhanh.Shared;

namespace VinhKhanh.Pages
{
    public partial class MapPage
    {
        private int _isMapRenderRunning;
        private int _pendingMapRender;
        private DateTime _lastViewportRenderUtc = DateTime.MinValue;
        private Location? _lastViewportRenderCenter;
        // Tăng cooldown để giảm tần suất re-render pin khi pan/zoom map.
        // 4200ms → 5500ms: tránh giật khi user vuốt map liên tục.
        private static readonly TimeSpan MapViewportRenderCooldown = TimeSpan.FromMilliseconds(5500);
        // Yêu cầu di chuyển ≥180m mới re-render (trước là 140m).
        private const double MinViewportMoveMetersToRefresh = 180d;

        // Debounced property changed handler for map VisibleRegion updates
        private void OnMapPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try
            {
                if (e == null) return;
                if (!string.Equals(e.PropertyName, "VisibleRegion", StringComparison.OrdinalIgnoreCase)) return;
                if (_isPageInitializing) return;
                if (PoiDetailPanel?.IsVisible == true) return;
                if (_pois == null || _pois.Count == 0) return;

                _mapMoveDebounceCts?.Cancel();
                _mapMoveDebounceCts?.Dispose();
                _mapMoveDebounceCts = new CancellationTokenSource();
                var token = _mapMoveDebounceCts.Token;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1300, token);
                        if (token.IsCancellationRequested) return;
                        if (_isPageInitializing) return;
                        if (_isMapRenderRunning == 1) return;

                        if (!ShouldRefreshViewportPins()) return;

                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            try { AddPoisToMap(); } catch { }
                        });

                        try
                        {
                            var center = vinhKhanhMap?.VisibleRegion?.Center;
                            _lastViewportRenderCenter = center;
                            _lastViewportRenderUtc = DateTime.UtcNow;
                        }
                        catch { }
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                });
            }
            catch { }
        }

        private bool ShouldRefreshViewportPins()
        {
            try
            {
                if (_isPageInitializing) return false;
                if (_pois == null || _pois.Count == 0) return false;

                var now = DateTime.UtcNow;
                var center = vinhKhanhMap?.VisibleRegion?.Center;
                if (center == null)
                {
                    return (now - _lastViewportRenderUtc) >= MapViewportRenderCooldown;
                }

                if (_lastViewportRenderCenter == null)
                {
                    return true;
                }

                var moveMeters = HaversineDistanceMeters(
                    center.Latitude,
                    center.Longitude,
                    _lastViewportRenderCenter.Latitude,
                    _lastViewportRenderCenter.Longitude);

                if (moveMeters < MinViewportMoveMetersToRefresh
                    && (now - _lastViewportRenderUtc) < MapViewportRenderCooldown)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return true;
            }
        }

        private void AddPoisToMap()
        {
            if (!MainThread.IsMainThread)
            {
                MainThread.BeginInvokeOnMainThread(AddPoisToMap);
                return;
            }

            if (Interlocked.CompareExchange(ref _isMapRenderRunning, 1, 0) == 1)
            {
                Interlocked.Exchange(ref _pendingMapRender, 1);
                return;
            }

            try
            {
                AddPoisToMapCore();
            }
            finally
            {
                Interlocked.Exchange(ref _isMapRenderRunning, 0);
                if (Interlocked.Exchange(ref _pendingMapRender, 0) == 1)
                {
                    MainThread.BeginInvokeOnMainThread(AddPoisToMap);
                }
            }
        }

        private void AddPoisToMapCore()
        {
            try
            {
                if (_pinByPoiId == null) _pinByPoiId = new Dictionary<int, Pin>();

                var desired = BuildDesiredPoisForVisibleRegion();
                var desiredIds = new HashSet<int>(desired.Select(d => d.Id));
                var currentIds = new HashSet<int>(_pinByPoiId.Keys);

                if (currentIds.SetEquals(desiredIds))
                {
                    return;
                }

                var toRemove = _pinByPoiId.Keys.Where(id => !desiredIds.Contains(id)).ToList();
                foreach (var id in toRemove)
                {
                    try
                    {
                        if (_pinByPoiId.TryGetValue(id, out var pin))
                        {
                            try { vinhKhanhMap.Pins.Remove(pin); } catch { }
                            try { _pinByPoiId.Remove(id); } catch { }
                        }
                    }
                    catch { }
                }

                foreach (var poi in desired)
                {
                    try
                    {
                        if (_pinByPoiId.ContainsKey(poi.Id)) continue;

                        var pin = new Pin
                        {
                            Label = poi.Name,
                            Address = poi.Category,
                            Location = new Location(poi.Latitude, poi.Longitude),
                            Type = PinType.Place
                        };

                        pin.MarkerClicked += async (s, e) =>
                        {
                            try
                            {
                                try { e.HideInfoWindow = true; } catch { }
                                await OpenPoiDetailFromSelectionAsync(poi, "map_pin", userInitiated: true);
                            }
                            catch { }
                        };

                        vinhKhanhMap.Pins.Add(pin);
                        _pinByPoiId[poi.Id] = pin;
                    }
                    catch { }
                }

                if (_selectedPoi != null && _selectedPoi.Id > 0)
                {
                    try
                    {
                        if (_pinByPoiId.TryGetValue(_selectedPoi.Id, out var selectedPin))
                        {
                            if (!vinhKhanhMap.Pins.Contains(selectedPin))
                            {
                                vinhKhanhMap.Pins.Add(selectedPin);
                            }
                        }
                        else
                        {
                            var selectedPoi = (_pois ?? new List<PoiModel>()).FirstOrDefault(p => p != null && p.Id == _selectedPoi.Id);
                            if (selectedPoi != null)
                            {
                                var selectedPoiPin = new Pin
                                {
                                    Label = selectedPoi.Name,
                                    Address = selectedPoi.Category,
                                    Location = new Location(selectedPoi.Latitude, selectedPoi.Longitude),
                                    Type = PinType.Place
                                };

                                selectedPoiPin.MarkerClicked += async (s, e) =>
                                {
                                    try
                                    {
                                        try { e.HideInfoWindow = true; } catch { }
                                        await OpenPoiDetailFromSelectionAsync(selectedPoi, "map_pin", userInitiated: true);
                                    }
                                    catch { }
                                };

                                vinhKhanhMap.Pins.Add(selectedPoiPin);
                                _pinByPoiId[selectedPoi.Id] = selectedPoiPin;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private List<PoiModel> BuildDesiredPoisForVisibleRegion()
        {
            var candidates = (_pois ?? new List<PoiModel>())
                .Where(p => p != null)
                .OrderByDescending(p => p.Priority)
                .Take(Math.Max(MaxPinsToRender + 20, 120))
                .ToList();

            Location center = null;
            Microsoft.Maui.Maps.MapSpan visible = null;
            try
            {
                visible = vinhKhanhMap?.VisibleRegion;
                center = visible?.Center;
            }
            catch { }

            if (center != null && visible != null)
            {
                try
                {
                    var latHalf = Math.Max(0.001, visible.LatitudeDegrees / 2.0);
                    var lngHalf = Math.Max(0.001, visible.LongitudeDegrees / 2.0);

                    var latMin = center.Latitude - (latHalf * 1.2);
                    var latMax = center.Latitude + (latHalf * 1.2);
                    var lngMin = center.Longitude - (lngHalf * 1.2);
                    var lngMax = center.Longitude + (lngHalf * 1.2);

                    var inBox = candidates
                        .Where(p => p.Latitude >= latMin && p.Latitude <= latMax && p.Longitude >= lngMin && p.Longitude <= lngMax)
                        .ToList();

                    if (inBox.Count > 0)
                    {
                        candidates = inBox;
                    }
                }
                catch { }
            }

            if (center != null)
            {
                candidates = candidates
                    .OrderBy(p => HaversineDistanceMeters(center.Latitude, center.Longitude, p.Latitude, p.Longitude))
                    .ThenByDescending(p => p.Priority)
                    .ToList();
            }
            else
            {
                candidates = candidates.OrderByDescending(p => p.Priority).ToList();
            }

            return candidates.Take(GetMapPinRenderLimit()).ToList();
        }
    }
}
