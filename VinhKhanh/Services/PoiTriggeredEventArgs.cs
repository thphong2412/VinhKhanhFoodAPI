using System;
using VinhKhanh.Shared;

namespace VinhKhanh.Services
{
    public class PoiTriggeredEventArgs : EventArgs
    {
        public PoiModel Poi { get; }
        public double DistanceMeters { get; }

        public PoiTriggeredEventArgs(PoiModel poi, double distanceMeters)
        {
            Poi = poi;
            DistanceMeters = distanceMeters;
        }
    }
}
