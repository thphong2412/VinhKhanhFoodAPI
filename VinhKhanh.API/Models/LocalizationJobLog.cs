using System;
using System.ComponentModel.DataAnnotations;

namespace VinhKhanh.API.Models
{
    public class LocalizationJobLog
    {
        [Key]
        public int Id { get; set; }
        public int PoiId { get; set; }
        public string LanguageCode { get; set; } = "en";
        public string JobType { get; set; } = "on_demand"; // on_demand | hotset | warmup
        public string Status { get; set; } = "completed"; // completed | blocked | failed
        public string? Notes { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }
}
