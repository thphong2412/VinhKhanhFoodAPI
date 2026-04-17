namespace VinhKhanh.AdminPortal.Models
{
    public class OwnerPoiDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool IsPublished { get; set; }
        public double Radius { get; set; }
        public int Priority { get; set; }
        public int CooldownSeconds { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}
