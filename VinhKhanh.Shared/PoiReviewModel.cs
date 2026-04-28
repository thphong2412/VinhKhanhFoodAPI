using System;
using System.ComponentModel.DataAnnotations;

namespace VinhKhanh.Shared
{
    public class PoiReviewModel
    {
        [Key]
        public int Id { get; set; }
        public int PoiId { get; set; }
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public string LanguageCode { get; set; } = "vi";
        public string DeviceId { get; set; } = string.Empty;
        public bool IsHidden { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
