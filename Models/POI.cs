using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VinhKhanhFoodAPI.Models
{
    [Table("POI")]
    public class POI
    {
        [Key]
        public int Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }


        public string QRCode { get; set; }
        public string? AudioUrl { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public int Radius { get; set; }

        public string? Address { get; set; }
    }
}