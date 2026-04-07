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
        // Minimal debounce between triggers in seconds for same POI
        private const int DefaultDebounceSeconds = 5;

        public event EventHandler<PoiTriggeredEventArgs> PoiTriggered;

        public void UpdatePois(IEnumerable<PoiModel> pois)
        {
            _pois = pois?.ToList() ?? new List<PoiModel>();
        }

        public void ProcessLocation(double latitude, double longitude)
        {
            if (_pois == null || !_pois.Any())
                return;

            // Find all POIs within their radius
            var candidates = new List<(PoiModel poi, double dist)>();
            foreach (var poi in _pois)
            {
                var d = HaversineDistanceMeters(latitude, longitude, poi.Latitude, poi.Longitude);
                if (d <= Math.Max(1, poi.Radius)) // if poi.Radius is 0 treat as 1m safe
                {
                    candidates.Add((poi, d));
                }
            }

            if (!candidates.Any()) return;

            // Choose by Priority (higher) then by distance (closer)
            var chosen = candidates
                .OrderByDescending(c => c.poi.Priority)
                .ThenBy(c => c.dist)
                .First();

            // Check cooldown/debounce
            var now = DateTime.UtcNow;
            int cooldown = Math.Max(0, chosen.poi.CooldownSeconds);
            if (cooldown == 0) cooldown = 30; // default 30s if not set

            if (_lastTriggered.TryGetValue(chosen.poi.Id, out var last))
            {
                if ((now - last).TotalSeconds < Math.Max(DefaultDebounceSeconds, cooldown))
                {
                    // still cooling down
                    return;
                }
            }

            _lastTriggered[chosen.poi.Id] = now;

            // Raise event
            PoiTriggered?.Invoke(this, new PoiTriggeredEventArgs(chosen.poi, chosen.dist));
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
