using System;
using System.Threading.Tasks;

namespace VinhKhanh.Pages
{
    public partial class MapPage
    {


        private string NormalizeToSupportedLanguage(string? language)
        {
            var normalized = NormalizeLanguageCode(language ?? string.Empty);
            return string.IsNullOrWhiteSpace(normalized) ? "en" : normalized;
        }

        private static bool IsLikelyImageUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var source = value.ToLowerInvariant();
            return source.EndsWith(".jpg") || source.EndsWith(".jpeg") || source.EndsWith(".png") || source.EndsWith(".webp") || source.EndsWith(".gif");
        }


    }
}