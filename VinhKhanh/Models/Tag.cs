using System.Text.Json.Serialization;

namespace VinhKhanh.Models
{
    public class Tag
    {
        public int ID { get; set; }
        public string Title { get; set; } = string.Empty;
        // Store color as hex string (e.g. "#RRGGBB" or "#AARRGGBB") to keep the shared model free of UI frameworks
        public string Color { get; set; } = "#FF0000";

        // UI-specific helpers (Brush/Color) are implemented in the MAUI project via a partial class.
        // Keep only platform-agnostic properties here so non-MAUI projects can build the shared library.

        [JsonIgnore]
        public bool IsSelected { get; set; }

        // Convenience helpers that return darker / lighter hex colors for use in non-MAUI logic
        [JsonIgnore]
        public string DisplayDarkColorHex => AdjustLuminosityHex(Color, -0.25f);

        [JsonIgnore]
        public string DisplayLightColorHex => AdjustLuminosityHex(Color, 0.25f);

        private static string AdjustLuminosityHex(string hex, float delta)
        {
            if (string.IsNullOrWhiteSpace(hex)) return hex ?? string.Empty;
            var h = hex.Trim();
            if (h.StartsWith("#")) h = h.Substring(1);
            // support RGB (6) or ARGB/RGBA (8)
            try
            {
                int start = 0;
                byte a = 255;
                if (h.Length == 8)
                {
                    a = Convert.ToByte(h.Substring(0, 2), 16);
                    start = 2;
                }

                byte r = Convert.ToByte(h.Substring(start, 2), 16);
                byte g = Convert.ToByte(h.Substring(start + 2, 2), 16);
                byte b = Convert.ToByte(h.Substring(start + 4, 2), 16);

                float fr = r / 255f;
                float fg = g / 255f;
                float fb = b / 255f;

                fr = Clamp01(fr + delta);
                fg = Clamp01(fg + delta);
                fb = Clamp01(fb + delta);

                string result;
                if (h.Length == 8)
                    result = $"#{a:X2}{(int)(fr * 255):X2}{(int)(fg * 255):X2}{(int)(fb * 255):X2}";
                else
                    result = $"#{(int)(fr * 255):X2}{(int)(fg * 255):X2}{(int)(fb * 255):X2}";

                return result;
            }
            catch
            {
                return hex; // fallback if parsing fails
            }
        }

        private static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
