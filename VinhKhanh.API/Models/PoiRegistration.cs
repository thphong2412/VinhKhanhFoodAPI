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
