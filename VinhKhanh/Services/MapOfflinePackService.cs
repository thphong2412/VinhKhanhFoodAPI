using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using VinhKhanh.Shared;

namespace VinhKhanh.Services
{
    public interface IMapOfflinePackService
    {
        string OfflineRootPath { get; }
        Task<MapOfflineDownloadResult> DownloadPackAsync(string version, IProgress<MapOfflineProgress>? progress = null, CancellationToken cancellationToken = default);
        Task<bool> HasPackAsync(string version);
        Task<string?> GetLocalEntryHtmlAsync(string version, string? suggestedEntryHtml);
    }

    public sealed class MapOfflinePackService : IMapOfflinePackService
    {
        private readonly ApiService _apiService;
        private readonly HttpClient _httpClient;

        public MapOfflinePackService(ApiService apiService, HttpClient httpClient)
        {
            _apiService = apiService;
            _httpClient = httpClient;
        }

        public string OfflineRootPath => Path.Combine(FileSystem.AppDataDirectory, "offline_packs");

        public async Task<MapOfflineDownloadResult> DownloadPackAsync(string version, IProgress<MapOfflineProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var ver = string.IsNullOrWhiteSpace(version) ? "q4-v1" : version.Trim();
            var manifest = await _apiService.GetMapOfflineManifestAsync(ver);
            if (manifest == null || manifest.Assets == null || manifest.Assets.Count == 0)
            {
                return new MapOfflineDownloadResult
                {
                    Success = false,
                    Version = ver,
                    Error = "manifest_empty"
                };
            }

            var totalBytes = manifest.TotalBytes > 0 ? manifest.TotalBytes : manifest.Assets.Sum(a => a?.Size ?? 0);
            var versionRoot = Path.Combine(OfflineRootPath, ver);
            Directory.CreateDirectory(versionRoot);

            long downloadedBytes = 0;
            int downloadedFiles = 0;

            foreach (var asset in manifest.Assets.Where(a => a != null))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relative = NormalizeRelativeAssetPath(asset.Url);
                if (string.IsNullOrWhiteSpace(relative))
                {
                    continue;
                }

                var localPath = Path.Combine(versionRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                var localDir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrWhiteSpace(localDir))
                {
                    Directory.CreateDirectory(localDir);
                }

                if (File.Exists(localPath) && asset.Size > 0)
                {
                    var fi = new FileInfo(localPath);
                    if (fi.Length == asset.Size)
                    {
                        if (!string.IsNullOrWhiteSpace(asset.Sha256))
                        {
                            var existingHash = await ComputeSha256Async(localPath, cancellationToken);
                            if (existingHash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                            {
                                downloadedBytes += fi.Length;
                                downloadedFiles++;
                                Report(progress, downloadedFiles, manifest.Assets.Count, downloadedBytes, totalBytes, "cache_hit", relative);
                                continue;
                            }
                        }
                        else
                        {
                            downloadedBytes += fi.Length;
                            downloadedFiles++;
                            Report(progress, downloadedFiles, manifest.Assets.Count, downloadedBytes, totalBytes, "cache_hit", relative);
                            continue;
                        }
                    }
                }

                var absolute = ToAbsoluteAssetUrl(asset.Url);
                if (string.IsNullOrWhiteSpace(absolute))
                {
                    continue;
                }

                using var response = await _httpClient.GetAsync(absolute, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using (var fs = File.Create(localPath))
                await using (var rs = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    await rs.CopyToAsync(fs, cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(asset.Sha256))
                {
                    var hash = await ComputeSha256Async(localPath, cancellationToken);
                    if (!hash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"sha256_mismatch:{relative}");
                    }
                }

                var fileInfo = new FileInfo(localPath);
                downloadedBytes += fileInfo.Exists ? fileInfo.Length : 0;
                downloadedFiles++;
                Report(progress, downloadedFiles, manifest.Assets.Count, downloadedBytes, totalBytes, "downloaded", relative);
            }

            var entryHtml = await GetLocalEntryHtmlAsync(ver, manifest.SuggestedEntryHtml);
            return new MapOfflineDownloadResult
            {
                Success = true,
                Version = ver,
                DownloadedFiles = downloadedFiles,
                TotalFiles = manifest.Assets.Count,
                DownloadedBytes = downloadedBytes,
                TotalBytes = totalBytes,
                LocalPackDirectory = versionRoot,
                LocalEntryHtml = entryHtml
            };
        }

        public Task<bool> HasPackAsync(string version)
        {
            var ver = string.IsNullOrWhiteSpace(version) ? "q4-v1" : version.Trim();
            var path = Path.Combine(OfflineRootPath, ver);
            var exists = Directory.Exists(path) && Directory.GetFiles(path, "*", SearchOption.AllDirectories).Any();
            return Task.FromResult(exists);
        }

        public Task<string?> GetLocalEntryHtmlAsync(string version, string? suggestedEntryHtml)
        {
            var ver = string.IsNullOrWhiteSpace(version) ? "q4-v1" : version.Trim();
            var root = Path.Combine(OfflineRootPath, ver);
            if (!Directory.Exists(root)) return Task.FromResult<string?>(null);

            var suggestedRel = NormalizeRelativeAssetPath(suggestedEntryHtml);
            if (!string.IsNullOrWhiteSpace(suggestedRel))
            {
                var suggestedPath = Path.Combine(root, suggestedRel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(suggestedPath)) return Task.FromResult<string?>(suggestedPath);
            }

            var fallback = Directory.GetFiles(root, "*.html", SearchOption.AllDirectories).FirstOrDefault();
            return Task.FromResult<string?>(fallback);
        }

        private string? ToAbsoluteAssetUrl(string? assetUrl)
        {
            if (string.IsNullOrWhiteSpace(assetUrl)) return null;
            if (Uri.TryCreate(assetUrl, UriKind.Absolute, out var absolute)) return absolute.ToString();

            var baseUrl = _apiService.CurrentBaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var apiUri)) return null;

            var authority = apiUri.GetLeftPart(UriPartial.Authority);
            var normalizedPath = assetUrl.StartsWith("/") ? assetUrl : "/" + assetUrl;
            return authority + normalizedPath;
        }

        private static string NormalizeRelativeAssetPath(string? assetUrl)
        {
            if (string.IsNullOrWhiteSpace(assetUrl)) return string.Empty;
            var value = assetUrl.Trim();

            if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
            {
                value = absolute.AbsolutePath;
            }

            value = value.TrimStart('/').Replace('\\', '/');
            if (value.StartsWith("map-packs/", StringComparison.OrdinalIgnoreCase))
            {
                var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 3)
                {
                    value = string.Join('/', segments.Skip(2));
                }
            }

            return value;
        }

        private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
        {
            await using var fs = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(fs, cancellationToken);
            return Convert.ToHexString(hash);
        }

        private static void Report(IProgress<MapOfflineProgress>? progress, int doneFiles, int totalFiles, long doneBytes, long totalBytes, string stage, string currentAsset)
        {
            if (progress == null) return;
            progress.Report(new MapOfflineProgress
            {
                DownloadedFiles = doneFiles,
                TotalFiles = totalFiles,
                DownloadedBytes = doneBytes,
                TotalBytes = totalBytes,
                Stage = stage,
                CurrentAsset = currentAsset
            });
        }
    }

    public sealed class MapOfflineDownloadResult
    {
        public bool Success { get; set; }
        public string Version { get; set; } = string.Empty;
        public int DownloadedFiles { get; set; }
        public int TotalFiles { get; set; }
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public string? LocalPackDirectory { get; set; }
        public string? LocalEntryHtml { get; set; }
        public string? Error { get; set; }
    }

    public sealed class MapOfflineProgress
    {
        public int DownloadedFiles { get; set; }
        public int TotalFiles { get; set; }
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string CurrentAsset { get; set; } = string.Empty;
        public double Percent => TotalBytes <= 0 ? 0 : Math.Min(100d, (double)DownloadedBytes / TotalBytes * 100d);
    }
}
