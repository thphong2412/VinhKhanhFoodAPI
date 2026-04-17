namespace VinhKhanh.AdminPortal.Models
{
    public class TopPoiDto
    {
        public int PoiId { get; set; }
        public int Count { get; set; }
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
}
