using System;
using System.Collections.Generic;

namespace VinhKhanh.Shared
{
    public class TourModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<int> PoiIds { get; set; } = new List<int>();
        public bool IsPublished { get; set; } = false;
    }
}
