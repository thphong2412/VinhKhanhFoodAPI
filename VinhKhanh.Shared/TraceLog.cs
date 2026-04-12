using System;

namespace VinhKhanh.Shared
{
    public class TraceLog
    {
        public int Id { get; set; }
        public int PoiId { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string DeviceId { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        // Optional JSON metadata (can include event type, duration etc.)
        public string ExtraJson { get; set; }

        // Optional duration in seconds for listen events
        public double? DurationSeconds { get; set; }
    }
}
