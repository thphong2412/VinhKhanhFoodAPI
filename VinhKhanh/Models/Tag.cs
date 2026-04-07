using System.Text.Json.Serialization;
using Microsoft.Maui.Controls; // FIX LỖI: Brush, SolidColorBrush
using Microsoft.Maui.Graphics; // FIX LỖI: Color

namespace VinhKhanh.Models
{
    public class Tag
    {
        public int ID { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Color { get; set; } = "#FF0000";

        [JsonIgnore]
        public Brush ColorBrush
        {
            get
            {
                return new SolidColorBrush(Microsoft.Maui.Graphics.Color.FromArgb(Color));
            }
        }

        [JsonIgnore]
        public Color DisplayColor
        {
            get
            {
                return Microsoft.Maui.Graphics.Color.FromArgb(Color);
            }
        }

        [JsonIgnore]
        public Color DisplayDarkColor
        {
            get
            {
                // Dùng phương thức chuẩn WithLuminosity hoặc WithAlpha để tạo màu tối/sáng
                return DisplayColor.WithLuminosity(0.3f);
            }
        }

        [JsonIgnore]
        public Color DisplayLightColor
        {
            get
            {
                return DisplayColor.WithLuminosity(0.8f);
            }
        }

        [JsonIgnore]
        public bool IsSelected { get; set; }
    }
}