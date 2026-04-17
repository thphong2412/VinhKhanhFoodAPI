namespace VinhKhanh.AdminPortal.Models
{
    public class OwnerDetailDto
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsVerified { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? ShopName { get; set; }
        public string? ShopAddress { get; set; }
        public DateTime? OwnerSubmittedAt { get; set; }
        public DateTime? OwnerReviewedAt { get; set; }
        public string? OwnerRegistrationStatus { get; set; }
        public List<OwnerPoiDto> Pois { get; set; } = new();
    }
}
