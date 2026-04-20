using SQLite;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace VinhKhanh.Shared
{
    public class PoiModel
    {
        [PrimaryKey, AutoIncrement]
        [JsonPropertyName("id")]
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
        [JsonPropertyName("Contents")]
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
            get => NormalizeVndPriceValue(string.IsNullOrWhiteSpace(_priceMin) ? ExtractRangePart(PriceRange, 0) : _priceMin);
            set => _priceMin = value;
        }

        [Ignore, NotMapped]
        public string? PriceMax
        {
            get => NormalizeVndPriceValue(string.IsNullOrWhiteSpace(_priceMax) ? ExtractRangePart(PriceRange, 1) : _priceMax);
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
                PriceMin = NormalizeVndPriceValue(PriceMin);
                PriceMax = NormalizeVndPriceValue(PriceMax);
                PriceRange = BuildRange(PriceMin, PriceMax);
            }
            else
            {
                PriceRange = NormalizeVndPriceRange(PriceRange);
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
            return $"{min} - {max}";
        }

        public string? GetNormalizedPriceRangeDisplay()
        {
            if (!string.IsNullOrWhiteSpace(PriceMin) || !string.IsNullOrWhiteSpace(PriceMax))
            {
                var min = NormalizeVndPriceValue(PriceMin);
                var max = NormalizeVndPriceValue(PriceMax);
                if (string.IsNullOrWhiteSpace(min)) return max;
                if (string.IsNullOrWhiteSpace(max)) return min;
                return $"{min} - {max}";
            }

            return NormalizeVndPriceRange(PriceRange);
        }

        public static string? NormalizeVndPriceRange(string? range)
        {
            if (string.IsNullOrWhiteSpace(range)) return range;

            var source = range.Trim();
            var splitByDash = source.Split('-', System.StringSplitOptions.TrimEntries | System.StringSplitOptions.RemoveEmptyEntries);
            if (splitByDash.Length >= 2)
            {
                var min = NormalizeVndPriceValue(splitByDash[0]);
                var max = NormalizeVndPriceValue(splitByDash[1]);
                if (!string.IsNullOrWhiteSpace(min) && !string.IsNullOrWhiteSpace(max)) return $"{min} - {max}";
            }

            var splitByTo = source.Split("to", System.StringSplitOptions.TrimEntries | System.StringSplitOptions.RemoveEmptyEntries);
            if (splitByTo.Length == 2)
            {
                var min = NormalizeVndPriceValue(splitByTo[0]);
                var max = NormalizeVndPriceValue(splitByTo[1]);
                if (!string.IsNullOrWhiteSpace(min) && !string.IsNullOrWhiteSpace(max)) return $"{min} - {max}";
            }

            return NormalizeVndPriceValue(source) ?? source;
        }

        public static string? NormalizeVndPriceValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            var original = value.Trim();
            var lower = original.ToLowerInvariant();
            var hasK = lower.Contains('k');
            var hasTrieu = lower.Contains("triệu") || lower.Contains("trieu");

            decimal amount;
            if (hasTrieu || hasK)
            {
                var unitNumeric = ExtractDecimalNumeric(original);
                if (string.IsNullOrWhiteSpace(unitNumeric)) return original;
                if (!decimal.TryParse(unitNumeric, NumberStyles.Any, CultureInfo.InvariantCulture, out amount)) return original;
                amount *= hasTrieu ? 1_000_000m : 1_000m;
            }
            else
            {
                var absoluteDigits = ExtractDigitsOnly(original);
                if (string.IsNullOrWhiteSpace(absoluteDigits)) return original;
                if (!decimal.TryParse(absoluteDigits, NumberStyles.Any, CultureInfo.InvariantCulture, out amount)) return original;
                if (amount < 1_000m)
                {
                    amount *= 1_000m;
                }
            }

            amount = decimal.Round(amount, 0, MidpointRounding.AwayFromZero);
            return string.Format(CultureInfo.GetCultureInfo("vi-VN"), "{0:N0} VND", amount);
        }

        private static string ExtractDecimalNumeric(string source)
        {
            var sb = new StringBuilder();
            var hasDecimalSeparator = false;
            foreach (var ch in source)
            {
                if (char.IsDigit(ch))
                {
                    sb.Append(ch);
                    continue;
                }

                if ((ch == '.' || ch == ',') && !hasDecimalSeparator)
                {
                    sb.Append('.');
                    hasDecimalSeparator = true;
                }
            }

            return sb.ToString();
        }

        private static string ExtractDigitsOnly(string source)
        {
            var digits = source.Where(char.IsDigit).ToArray();
            return new string(digits);
        }
    }
}