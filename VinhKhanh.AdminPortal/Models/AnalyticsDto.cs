namespace VinhKhanh.AdminPortal.Models
{
    public class TopPoiDto
    {
        public int PoiId { get; set; }
        public int Count { get; set; }
        public string PoiName { get; set; } = string.Empty;
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
        public int Count { get; set; }
    }

    public class PoiEngagementDto
    {
        public int PoiId { get; set; }
        public string PoiName { get; set; } = string.Empty;
        public int TotalListens { get; set; }
        public int TtsPlays { get; set; }
        public int AudioPlays { get; set; }
        public int ListenStarts { get; set; }
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
}
