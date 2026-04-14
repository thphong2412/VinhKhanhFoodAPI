namespace VinhKhanh
{
    /// <summary>
    /// DTO for audio file information
    /// </summary>
    public class AudioFileInfo
    {
        public int Id { get; set; }
        public int PoiId { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string Duration { get; set; }
        public string LanguageCode { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public bool IsProcessed { get; set; }
        public bool IsTts { get; set; }
    }
}
