namespace VinhKhanh.Shared
{
    public class MapOfflineManifestDto
    {
        public string Version { get; set; } = "q4-v1";
        public string Area { get; set; } = "Quan4";
        public string Provider { get; set; } = string.Empty;
        public string RecommendedMode { get; set; } = string.Empty;
        public int TotalAssets { get; set; }
        public long TotalBytes { get; set; }
        public string? SuggestedEntryHtml { get; set; }
        public string? SuggestedPmtiles { get; set; }
        public List<MapOfflineAssetDto> Assets { get; set; } = new();
        public DateTime GeneratedAtUtc { get; set; }
    }

    public class MapOfflineAssetDto
    {
        public string Url { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public DateTime LastWriteUtc { get; set; }
    }
}
