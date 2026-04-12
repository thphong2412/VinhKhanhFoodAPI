using System;
using System.ComponentModel.DataAnnotations;

namespace VinhKhanh.API.Models
{
    public class OwnerRegistration
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public string ShopName { get; set; }
        public string ShopAddress { get; set; }
        // encrypted PII
        public string CccdEncrypted { get; set; }
        public string Status { get; set; } = "pending"; // pending/approved/rejected
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public int? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string Notes { get; set; }
    }
}
