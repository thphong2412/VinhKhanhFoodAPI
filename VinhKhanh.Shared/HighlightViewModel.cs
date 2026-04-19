namespace VinhKhanh.Shared
{
    public class HighlightViewModel
    {
        public PoiModel Poi { get; set; }
        public int PoiId => Poi?.Id ?? 0;
        public string ImageUrl { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string RatingDisplay { get; set; }
        public string PriceDisplay { get; set; }
        public int ReviewCount { get; set; }
        public string ReviewCountDisplay => ReviewCount > 0 ? $"({ReviewCount})" : string.Empty;
        public string Address { get; set; }
        public string OpeningHours { get; set; }
        public string OpenStatus { get; set; }
        // Hex color string, e.g. "#388E3C"
        public string OpenStatusColorHex { get; set; }
        public string BadgeText { get; set; }
        public string BadgeColorHex { get; set; }
        public string PopularHint { get; set; }
        public bool IsHot { get; set; }
    }
}
