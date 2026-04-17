using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using VinhKhanh.Shared;

namespace VinhKhanh.Services
{
    public class GeofenceEngine : IGeofenceEngine
    {
        private List<PoiModel> _pois = new();
        // Track last triggered time per POI id
        private readonly ConcurrentDictionary<int, DateTime> _lastTriggered = new();
        // Track last time user exited a POI zone (for exit-aware cooldown)
        private readonly ConcurrentDictionary<int, DateTime> _lastExited = new();
        // Track current inside-zone state per POI
        private readonly HashSet<int> _insidePoiIds = new();
        private readonly object _stateLock = new();
        // Minimal debounce between triggers in seconds for same POI
        private const int DefaultDebounceSeconds = 5;

        public event EventHandler<PoiTriggeredEventArgs> PoiTriggered;

        public void UpdatePois(IEnumerable<PoiModel> pois)
        {
            _pois = pois?.ToList() ?? new List<PoiModel>();
            System.Diagnostics.Debug.WriteLine($"[GeofenceEngine] UpdatePois: loaded {_pois.Count} POIs");
        }

        public void ProcessLocation(double latitude, double longitude)
        {
            if (_pois == null || !_pois.Any())
            {
                System.Diagnostics.Debug.WriteLine("[GeofenceEngine] No POIs loaded.");
                return;
            }

            // Find all POIs within their radius
            var candidates = new List<(PoiModel poi, double dist)>();
            foreach (var poi in _pois)
            {
                var d = HaversineDistanceMeters(latitude, longitude, poi.Latitude, poi.Longitude);
                if (d <= Math.Max(1, poi.Radius))
                {
                    candidates.Add((poi, d));
                }
            }

            if (!candidates.Any())
            {
                System.Diagnostics.Debug.WriteLine($"[GeofenceEngine] No POI within radius for location {latitude},{longitude}");
                return;
            }

            var now = DateTime.UtcNow;
            var insideNow = candidates.Select(c => c.poi.Id).ToHashSet();

            (PoiModel poi, double dist)? toTrigger = null;

            lock (_stateLock)
            {
                // Detect exits and start cooldown from exit time
                var exited = _insidePoiIds.Where(id => !insideNow.Contains(id)).ToList();
                foreach (var exitedId in exited)
                {
                    _insidePoiIds.Remove(exitedId);
                    _lastExited[exitedId] = now;
                }

                // Detect newly entered POIs only (prevents repeated trigger while still inside)
                var entered = candidates
                    .Where(c => !_insidePoiIds.Contains(c.poi.Id))
                    .OrderByDescending(c => c.poi.Priority)
                    .ThenBy(c => c.dist)
                    .ToList();

                foreach (var enteredCandidate in entered)
                {
                    _insidePoiIds.Add(enteredCandidate.poi.Id);
                }

                if (!entered.Any())
                {
                    return;
                }

                // Trigger the highest-priority newly-entered POI that passed exit-aware cooldown
                foreach (var enteredCandidate in entered)
                {
                    var poi = enteredCandidate.poi;
                    var cooldown = Math.Max(DefaultDebounceSeconds, poi.CooldownSeconds > 0 ? poi.CooldownSeconds : 30);

                    if (_lastExited.TryGetValue(poi.Id, out var lastExit)
                        && (now - lastExit).TotalSeconds < cooldown)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GeofenceEngine] POI {poi.Name} re-entered too soon after exit (cooldown)");
                        continue;
                    }

                    toTrigger = enteredCandidate;
                    _lastTriggered[poi.Id] = now;
                    break;
                }
            }

            if (toTrigger.HasValue)
            {
                var chosen = toTrigger.Value;
                System.Diagnostics.Debug.WriteLine($"[GeofenceEngine] Entered POI {chosen.poi.Name} ({chosen.poi.Id}) at distance {chosen.dist}m");
                PoiTriggered?.Invoke(this, new PoiTriggeredEventArgs(chosen.poi, chosen.dist));
            }
        }

        public void TriggerPoiById(int poiId)
        {
            try
            {
                var poi = _pois.FirstOrDefault(p => p.Id == poiId);
                if (poi == null) return;
                var now = DateTime.UtcNow;
                if (_lastTriggered.TryGetValue(poi.Id, out var last))
                {
                    if ((now - last).TotalSeconds < Math.Max(DefaultDebounceSeconds, Math.Max(1, poi.CooldownSeconds))) return;
                }
                _lastTriggered[poi.Id] = now;
                System.Diagnostics.Debug.WriteLine($"[GeofenceEngine] TriggerPoiById: Triggered POI {poi.Name} ({poi.Id})");
                PoiTriggered?.Invoke(this, new PoiTriggeredEventArgs(poi, 0));
            }
            catch { }
        }

        // Haversine formula
        private static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            double R = 6371000; // meters
            double dLat = ToRadians(lat2 - lat1);
            double dLon = ToRadians(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double ToRadians(double deg) => deg * (Math.PI / 180.0);
    }
}
