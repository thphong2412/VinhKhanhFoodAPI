using System.Text.Json.Serialization;

namespace VinhKhanh.Models
{
    public class Category
    {
        public int ID { get; set; }
        public string Title { get; set; } = string.Empty;
        // Keep color as hex string so shared model does not depend on MAUI types
        public string Color { get; set; } = "#FF0000";

        [JsonIgnore]
        public string ColorDarkHex => AdjustLuminosityHex(Color, -0.2f);

        public override string ToString() => $"{Title}";

        private static string AdjustLuminosityHex(string hex, float delta)
        {
            if (string.IsNullOrWhiteSpace(hex)) return hex ?? string.Empty;
            var h = hex.Trim();
            if (h.StartsWith("#")) h = h.Substring(1);
            try
            {
                int start = 0;
                if (h.Length == 8) start = 2; // skip alpha

                byte r = Convert.ToByte(h.Substring(start, 2), 16);
                byte g = Convert.ToByte(h.Substring(start + 2, 2), 16);
                byte b = Convert.ToByte(h.Substring(start + 4, 2), 16);

                float fr = Clamp01(r / 255f + delta);
                float fg = Clamp01(g / 255f + delta);
                float fb = Clamp01(b / 255f + delta);

                return $"#{(int)(fr * 255):X2}{(int)(fg * 255):X2}{(int)(fb * 255):X2}";
            }
            catch
            {
                return hex;
            }
        }

        private static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
