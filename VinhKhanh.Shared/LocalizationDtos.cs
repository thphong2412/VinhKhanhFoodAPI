namespace VinhKhanh.Shared
{
    public class LocalizationPrepareRequest
    {
        public List<int> PoiIds { get; set; } = new();
        public string Lang { get; set; } = "en";
    }

    public class LocalizationOnDemandRequest
    {
        public int PoiId { get; set; }
        public string Lang { get; set; } = "en";
    }

    public class LocalizationPrepareResult
    {
        public int ReadyCount { get; set; }
        public int PendingCount { get; set; }
        public List<ContentModel> Items { get; set; } = new();
    }

    public class LocalizationOnDemandResult
    {
        public string Status { get; set; } = "cached";
        public ContentModel? Localization { get; set; }
    }

    public class LocalizationWarmupRequest
    {
        public string Lang { get; set; } = "en";
    }

    public class LocalizationWarmupStatusDto
    {
        public string Lang { get; set; } = "en";
        public string Status { get; set; } = "idle";
        public int TotalPois { get; set; }
        public int Ready { get; set; }
        public int Failed { get; set; }
        public double Progress { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string? LastMessage { get; set; }
    }
}
