namespace VinhKhanh.AdminPortal.Models
{
    public class AdminPoiOverviewDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Radius { get; set; }
        public int Priority { get; set; }
        public bool IsPublished { get; set; }
        public bool IsSaved { get; set; }
        public int? OwnerId { get; set; }
        public string OwnerEmail { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;
        public DateTime? ApprovedAtUtc { get; set; }
        public string? PendingRequestType { get; set; }
        public DateTime? PendingRequestSubmittedAtUtc { get; set; }
        public bool HasImage { get; set; }
        public bool HasAnyContent { get; set; }
        public bool HasContentVi { get; set; }
        public bool HasContentEn { get; set; }
        public bool HasAnyAudio { get; set; }
        public bool HasAudioVi { get; set; }
        public bool HasAudioEn { get; set; }
        public DateTime? LastHeartbeatUtc { get; set; }
        public int HeartbeatCountLast20m { get; set; }
        public List<string> Warnings { get; set; } = new();
    }
}
