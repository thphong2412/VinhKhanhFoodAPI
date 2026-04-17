using System;
using System.ComponentModel.DataAnnotations;

namespace VinhKhanh.API.Models
{
    public class AiUsageLog
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Feature { get; set; } = "enhance_description";
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "success";
        public int RetryCount { get; set; }
        public int PromptLength { get; set; }
        public int OutputLength { get; set; }
        public int EstimatedInputTokens { get; set; }
        public int EstimatedOutputTokens { get; set; }
        public decimal EstimatedCostUsd { get; set; }
        public string Model { get; set; } = string.Empty;
    }
}
