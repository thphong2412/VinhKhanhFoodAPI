namespace VinhKhanh.AdminPortal.Models
{
    public class OwnerRegistrationDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string ShopName { get; set; }
        public string ShopAddress { get; set; }
        public string CccdEncrypted { get; set; }
        public string Status { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string ReviewedBy { get; set; }
        public string Notes { get; set; }
        public string Email { get; set; }
    }
}
