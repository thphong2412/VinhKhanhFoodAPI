namespace VinhKhanh.AdminPortal.Models
{
    public class TopPoiDto
    {
        public int PoiId { get; set; }
        public int Count { get; set; }
        public string PoiName { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;
    }

    public class HeatmapPointDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public class AvgDurationDto
    {
        public int PoiId { get; set; }
        public double Avg { get; set; }
    }

    public class QrScanCountDto
    {
        public int PoiId { get; set; }
        public string PoiName { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class PoiEngagementDto
    {
        public int PoiId { get; set; }
        public string PoiName { get; set; } = string.Empty;
        public int TotalListens { get; set; }
        public int TtsPlays { get; set; }
        public int AudioPlays { get; set; }
        public int DetailOpens { get; set; }
        public int UniqueUsers { get; set; }
        public List<string> Users { get; set; } = new();
    }

    public class ActiveUserDto
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceModel { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string DeviceVersion { get; set; } = string.Empty;
        public int TotalEvents { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public List<int> PoiIds { get; set; } = new();
    }

    public class AnonymousRoutePointDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime TimestampUtc { get; set; }
        public int PoiId { get; set; }
    }

    public class AnonymousUserRouteDto
    {
        public string DeviceId { get; set; } = string.Empty;
        public DateTime LastSeenUtc { get; set; }
        public int TotalPoints { get; set; }
        public List<AnonymousRoutePointDto> Points { get; set; } = new();
    }

    public class AnalyticsSummaryDto
    {
        public int OnlineUsers { get; set; }
        public int VisitorsToday { get; set; }
        public DateTime SampledAtUtc { get; set; }
    }

    public class TopVisitedTodayDto
    {
        public int PoiId { get; set; }
        public string PoiName { get; set; } = string.Empty;
        public int VisitorsToday { get; set; }
        public int CurrentlyInside { get; set; }
        public DateTime LastVisitUtc { get; set; }
    }

    public class TraceLogRowDto
    {
        public int Id { get; set; }
        public int PoiId { get; set; }
        public string PoiName { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? ExtraJson { get; set; }
        public double? DurationSeconds { get; set; }
    }
}
