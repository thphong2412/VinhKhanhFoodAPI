using System.Text.Json.Serialization;
using Microsoft.Maui.Controls; // FIX LỖI: Brush, SolidColorBrush
using Microsoft.Maui.Graphics; // Hỗ trợ xử lý mã màu Argb

namespace VinhKhanh.Models
{
    public class Category
    {
        public int ID { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Color { get; set; } = "#FF0000";

        [JsonIgnore]
        public Brush ColorBrush
        {
            get
            {
                // Sử dụng Microsoft.Maui.Graphics.Color để convert từ mã String sang Color
                return new SolidColorBrush(Microsoft.Maui.Graphics.Color.FromArgb(Color));
            }
        }

        public override string ToString() => $"{Title}";
    }
}