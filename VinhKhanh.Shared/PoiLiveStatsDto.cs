namespace VinhKhanh.Shared
{
    public class PoiLiveStatsDto
    {
        public int PoiId { get; set; }
        public string PoiName { get; set; }
        public bool IsHot { get; set; }
        public int ActiveUsers { get; set; }
        public int EnRouteUsers { get; set; }
        public int VisitedUsers { get; set; }
        public int QrScanCount { get; set; }
        public double Rating { get; set; }
        public bool IsOpen { get; set; }
        public double? DistanceMeters { get; set; }
        public double SponsoredWeight { get; set; }
        public double ConversionScore { get; set; }
    }
}
