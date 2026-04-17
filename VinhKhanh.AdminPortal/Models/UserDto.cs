namespace VinhKhanh.AdminPortal.Models
{
    public class UserDto
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public string Role { get; set; }
        public bool IsVerified { get; set; }
        public DateTime CreatedAt { get; set; }

        // Owner profile details (nếu có)
        public string? ShopName { get; set; }
        public string? ShopAddress { get; set; }
        public DateTime? OwnerSubmittedAt { get; set; }
        public DateTime? OwnerReviewedAt { get; set; }
        public string? OwnerRegistrationStatus { get; set; }
    }
}
