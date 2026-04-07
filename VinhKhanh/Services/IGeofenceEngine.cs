using System;
using System.Collections.Generic;
using VinhKhanh.Shared;

namespace VinhKhanh.Services
{
    public interface IGeofenceEngine
    {
        /// <summary>
        /// Load or update the list of POIs the engine should evaluate
        /// </summary>
        void UpdatePois(IEnumerable<PoiModel> pois);

        /// <summary>
        /// Process a location update (lat, lng in decimal degrees)
        /// </summary>
        void ProcessLocation(double latitude, double longitude);

        /// <summary>
        /// Raised when a POI should be triggered according to engine rules
        /// </summary>
        event EventHandler<PoiTriggeredEventArgs> PoiTriggered;
    }
}
