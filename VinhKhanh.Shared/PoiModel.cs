using SQLite;
using System.Collections.Generic;

namespace VinhKhanh.Shared
{
    public class PoiModel
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int? OwnerId { get; set; }

        public string Name { get; set; }

        public string Category { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Radius { get; set; }
        public int Priority { get; set; }
        public int CooldownSeconds { get; set; }
        public string? ImageUrl { get; set; } // Cho phép null
        public string? WebsiteUrl { get; set; } // Cho phép null
        public string? QrCode { get; set; } // Cho phép null

        public bool IsSaved { get; set; } = false;
        public bool IsPublished { get; set; } = true;

        [Ignore]
        public List<ContentModel> Contents { get; set; } = new List<ContentModel>();
    }

    public class ContentModel
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int PoiId { get; set; }

        public string? LanguageCode { get; set; }
        public string? Title { get; set; }
        public string? Subtitle { get; set; }
        public string? Description { get; set; }
        public string? AudioUrl { get; set; }
        public bool IsTTS { get; set; }
        public string? PriceRange { get; set; }
        public double Rating { get; set; }
        public string? OpeningHours { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public string? ShareUrl { get; set; }
    }
}