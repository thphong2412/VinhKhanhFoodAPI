using SQLite; // <--- QUAN TRỌNG: Phải thêm cái này
using System.Collections.Generic;

namespace VinhKhanh.Shared
{
    public class PoiModel
    {
        [PrimaryKey, AutoIncrement] // <--- Thêm dòng này để SQLite tự quản lý ID
        public int Id { get; set; }

        public string Name { get; set; }

        public string Category { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        // Radius in meters used for geofence trigger
        public double Radius { get; set; }
        // Priority for resolving conflicts when multiple POIs are nearby (higher = more important)
        public int Priority { get; set; }
        // Cooldown in seconds after this POI was triggered to avoid spam
        public int CooldownSeconds { get; set; }
        public string ImageUrl { get; set; }
        // Website/URL of the POI (trang web của quán)
        public string WebsiteUrl { get; set; }


        // NEW: QR code payload (e.g. "POI:123" or custom token) for QR-based activation
        public string QrCode { get; set; }

        // If user saved/bookmarked this POI
        public bool IsSaved { get; set; } = false;

        [Ignore] // <--- SQLite không lưu được List trực tiếp, nên mình bảo nó "bỏ qua" cái này
        public List<ContentModel> Contents { get; set; } = new List<ContentModel>();
    }

    public class ContentModel
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed] // Đánh chỉ mục để tìm kiếm theo Poi nhanh hơn
        public int PoiId { get; set; }

        public string LanguageCode { get; set; }
        // Title and subtitle allow localized headings
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Description { get; set; }
        public string AudioUrl { get; set; }
        public bool IsTTS { get; set; }
        // Optional metadata for UI
        public string PriceRange { get; set; }
        public double Rating { get; set; }
        public string OpeningHours { get; set; }
        public string PhoneNumber { get; set; }
        // Full postal or street address for display
        public string Address { get; set; }
        public string ShareUrl { get; set; }
    }
}
