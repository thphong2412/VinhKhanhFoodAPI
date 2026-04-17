using System;
using System.ComponentModel.DataAnnotations;

namespace VinhKhanh.API.Models
{
    public class PoiRegistration
    {
        [Key]
        public int Id { get; set; }

        public int OwnerId { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public string Category { get; set; }

        [Required]
        public double Latitude { get; set; }

        [Required]
        public double Longitude { get; set; }

        public double Radius { get; set; }
        public int Priority { get; set; }
        public int CooldownSeconds { get; set; }
        public string? ImageUrl { get; set; }
        public string? WebsiteUrl { get; set; }
        public string? QrCode { get; set; }

        // Nội dung mặc định (vi) do Owner nhập khi đăng ký POI
        public string? ContentTitle { get; set; }
        public string? ContentSubtitle { get; set; }
        public string? ContentDescription { get; set; }
        public string? ContentPriceMin { get; set; }
        public string? ContentPriceMax { get; set; }
        public double? ContentRating { get; set; }
        public string? ContentOpenTime { get; set; }
        public string? ContentCloseTime { get; set; }
        public string? ContentPhoneNumber { get; set; }
        public string? ContentAddress { get; set; }

        /// <summary>
        /// create | update | delete
        /// </summary>
        public string RequestType { get; set; } = "create";

        /// <summary>
        /// Target POI ID for update/delete requests
        /// </summary>
        public int? TargetPoiId { get; set; }

        /// <summary>
        /// Status: pending, approved, rejected
        /// </summary>
        public string Status { get; set; } = "pending";

        /// <summary>
        /// POI ID after approval (if approved)
        /// </summary>
        public int? ApprovedPoiId { get; set; }

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewNotes { get; set; }
        public int? ReviewedBy { get; set; }
    }
}
