namespace VinhKhanh.AdminPortal.Models
{
    public class PoiRegistrationDto
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Radius { get; set; }
        public int Priority { get; set; }
        public int CooldownSeconds { get; set; }
        public string? ImageUrl { get; set; }
        public string? WebsiteUrl { get; set; }
        public string? QrCode { get; set; }
        public string RequestType { get; set; } = "create";
        public int? TargetPoiId { get; set; }
        public string? ContentTitle { get; set; }
        public string? ContentSubtitle { get; set; }
        public string? ContentDescription { get; set; }
        public string? ContentPriceMin { get; set; }
        public string? ContentPriceMax { get; set; }
        public double? ContentRating { get; set; }
        public string? ContentOpenTime { get; set; }
        public string? ContentCloseTime { get; set; }
        public string? ContentPhoneNumber { get; set; }
        public string? ContentAddress { get; set; }
        public string Status { get; set; }
        public int? ApprovedPoiId { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewNotes { get; set; }
        public int? ReviewedBy { get; set; }
        public List<string> ChangeSummary { get; set; } = new();
    }
}
