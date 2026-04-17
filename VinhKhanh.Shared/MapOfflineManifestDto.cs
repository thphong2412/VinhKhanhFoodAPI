namespace VinhKhanh.Shared
{
    public class MapOfflineManifestDto
    {
        public string Version { get; set; } = "q4-v1";
        public string Area { get; set; } = "Quan4";
        public List<MapOfflineAssetDto> Assets { get; set; } = new();
        public DateTime GeneratedAtUtc { get; set; }
    }

    public class MapOfflineAssetDto
    {
        public string Url { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }
}
