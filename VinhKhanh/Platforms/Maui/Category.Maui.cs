using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using VinhKhanh.Models;

namespace VinhKhanh.Models
{
    public partial class Category
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public Brush ColorBrush => new SolidColorBrush(ColorFromHex(Color));

        [System.Text.Json.Serialization.JsonIgnore]
        public Color DisplayColor => ColorFromHex(Color);

        private static Color ColorFromHex(string hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return Colors.Transparent;
                return Microsoft.Maui.Graphics.Color.FromArgb(hex);
            }
            catch
            {
                return Colors.Transparent;
            }
        }
    }
}
