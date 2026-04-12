using System;

namespace VinhKhanh.Shared
{
    public class AudioModel
    {
        public int Id { get; set; }
        public int PoiId { get; set; }
        public string Url { get; set; } = string.Empty;
        public string LanguageCode { get; set; } = "vi";
        public bool IsTts { get; set; }
        public bool IsProcessed { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
