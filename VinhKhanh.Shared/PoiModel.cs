using SQLite;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

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
        [Ignore, NotMapped]
        public string? PriceMin
        {
            get => string.IsNullOrWhiteSpace(_priceMin) ? ExtractRangePart(PriceRange, 0) : _priceMin;
            set => _priceMin = value;
        }

        [Ignore, NotMapped]
        public string? PriceMax
        {
            get => string.IsNullOrWhiteSpace(_priceMax) ? ExtractRangePart(PriceRange, 1) : _priceMax;
            set => _priceMax = value;
        }

        public double Rating { get; set; }
        public string? OpeningHours { get; set; }
        [Ignore, NotMapped]
        public string? OpenTime
        {
            get => string.IsNullOrWhiteSpace(_openTime) ? ExtractRangePart(OpeningHours, 0) : _openTime;
            set => _openTime = value;
        }

        [Ignore, NotMapped]
        public string? CloseTime
        {
            get => string.IsNullOrWhiteSpace(_closeTime) ? ExtractRangePart(OpeningHours, 1) : _closeTime;
            set => _closeTime = value;
        }

        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public string? ShareUrl { get; set; }

        public void NormalizeCompositeFields()
        {
            if (!string.IsNullOrWhiteSpace(PriceMin) || !string.IsNullOrWhiteSpace(PriceMax))
            {
                PriceRange = BuildRange(PriceMin, PriceMax);
            }

            if (!string.IsNullOrWhiteSpace(OpenTime) || !string.IsNullOrWhiteSpace(CloseTime))
            {
                OpeningHours = BuildRange(OpenTime, CloseTime);
            }
        }

        private string? _priceMin;
        private string? _priceMax;
        private string? _openTime;
        private string? _closeTime;

        private static string? ExtractRangePart(string? source, int index)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;

            var parts = source.Split('-', System.StringSplitOptions.TrimEntries | System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= index) return null;
            return parts[index];
        }

        private static string BuildRange(string? minValue, string? maxValue)
        {
            var min = minValue?.Trim() ?? string.Empty;
            var max = maxValue?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(min)) return max;
            if (string.IsNullOrWhiteSpace(max)) return min;
            return $"{min}-{max}";
        }
    }
}