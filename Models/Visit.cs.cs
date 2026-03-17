using System;

namespace VinhKhanhFoodAPI.Models
{
    public class Visit
    {
        public int Id { get; set; }

        public int PoiId { get; set; }

        public DateTime VisitTime { get; set; }

        public string DeviceId { get; set; }
    }
}