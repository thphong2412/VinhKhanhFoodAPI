using System;
using System.ComponentModel.DataAnnotations;

namespace VinhKhanh.API.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public string Role { get; set; } = "owner"; // 'owner' or 'admin'
        public string PermissionsJson { get; set; } = "";
        public bool IsVerified { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
