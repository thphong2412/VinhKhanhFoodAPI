using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace VinhKhanh.Services
{
    public interface IOfflineStorageService
    {
        Task<OfflineStorageResult> EnforceBudgetAsync(long? budgetBytes = null);
        Task<OfflineStorageResult> GetUsageAsync(long? budgetBytes = null);
    }

    public class OfflineStorageService : IOfflineStorageService
    {
        private readonly ILogger<OfflineStorageService> _logger;
        private const long DefaultBudgetBytes = 180L * 1024L * 1024L;

        public OfflineStorageService(ILogger<OfflineStorageService> logger)
        {
            _logger = logger;
        }

        public async Task<OfflineStorageResult> EnforceBudgetAsync(long? budgetBytes = null)
        {
            return await Task.Run(() =>
            {
                var budget = NormalizeBudget(budgetBytes);
                var entries = GetTrackedFiles();
                var totalBefore = entries.Sum(x => x.SizeBytes);

                if (totalBefore <= budget)
                {
                    return new OfflineStorageResult
                    {
                        BudgetBytes = budget,
                        TotalBytesBefore = totalBefore,
                        TotalBytesAfter = totalBefore,
                        DeletedBytes = 0,
                        DeletedFiles = 0
                    };
                }

                var target = (long)(budget * 0.9); // keep headroom to avoid frequent thrash
                long deletedBytes = 0;
                int deletedFiles = 0;

                foreach (var item in entries.OrderBy(x => x.LastWriteUtc))
                {
                    if (totalBefore - deletedBytes <= target) break;

                    try
                    {
                        File.Delete(item.Path);
                        deletedBytes += item.SizeBytes;
                        deletedFiles++;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "[OfflineStorage] Failed to delete {Path}", item.Path);
                    }
                }

                var totalAfter = Math.Max(0, totalBefore - deletedBytes);
                _logger?.LogInformation("[OfflineStorage] LRU cleanup done: before={Before} bytes, after={After} bytes, deletedFiles={DeletedFiles}", totalBefore, totalAfter, deletedFiles);

                return new OfflineStorageResult
                {
                    BudgetBytes = budget,
                    TotalBytesBefore = totalBefore,
                    TotalBytesAfter = totalAfter,
                    DeletedBytes = deletedBytes,
                    DeletedFiles = deletedFiles
                };
            });
        }

        public async Task<OfflineStorageResult> GetUsageAsync(long? budgetBytes = null)
        {
            return await Task.Run(() =>
            {
                var budget = NormalizeBudget(budgetBytes);
                var total = GetTrackedFiles().Sum(x => x.SizeBytes);
                return new OfflineStorageResult
                {
                    BudgetBytes = budget,
                    TotalBytesBefore = total,
                    TotalBytesAfter = total,
                    DeletedBytes = 0,
                    DeletedFiles = 0
                };
            });
        }

        private static long NormalizeBudget(long? budgetBytes)
        {
            if (budgetBytes.HasValue && budgetBytes.Value > 0) return budgetBytes.Value;
            return DefaultBudgetBytes;
        }

        private static IEnumerable<OfflineFileEntry> GetTrackedFiles()
        {
            var appDir = FileSystem.AppDataDirectory;
            if (!Directory.Exists(appDir)) return Enumerable.Empty<OfflineFileEntry>();

            var tracked = new List<OfflineFileEntry>();
            var dirs = new[]
            {
                Path.Combine(appDir, "edge_tts_cache"),
                Path.Combine(appDir, "localization_cache"),
                Path.Combine(appDir, "offline_packs")
            };

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                tracked.AddRange(Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Select(ToEntry));
            }

            tracked.AddRange(Directory.GetFiles(appDir, "native_tts_*", SearchOption.TopDirectoryOnly).Select(ToEntry));
            tracked.AddRange(Directory.GetFiles(appDir, "cloud_tts_*", SearchOption.TopDirectoryOnly).Select(ToEntry));
            tracked.AddRange(Directory.GetFiles(appDir, "poi_*", SearchOption.TopDirectoryOnly).Select(ToEntry));
            tracked.AddRange(Directory.GetFiles(appDir, "*.pmtiles", SearchOption.TopDirectoryOnly).Select(ToEntry));
            tracked.AddRange(Directory.GetFiles(appDir, "*.mbtiles", SearchOption.TopDirectoryOnly).Select(ToEntry));

            return tracked
                .Where(x => x.SizeBytes > 0)
                .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
        }

        private static OfflineFileEntry ToEntry(string path)
        {
            var fi = new FileInfo(path);
            return new OfflineFileEntry
            {
                Path = path,
                SizeBytes = fi.Exists ? fi.Length : 0,
                LastWriteUtc = fi.Exists ? fi.LastWriteTimeUtc : DateTime.MinValue
            };
        }

        private class OfflineFileEntry
        {
            public string Path { get; set; } = string.Empty;
            public long SizeBytes { get; set; }
            public DateTime LastWriteUtc { get; set; }
        }
    }

    public class OfflineStorageResult
    {
        public long BudgetBytes { get; set; }
        public long TotalBytesBefore { get; set; }
        public long TotalBytesAfter { get; set; }
        public long DeletedBytes { get; set; }
        public int DeletedFiles { get; set; }
        public double UsagePercent => BudgetBytes <= 0 ? 0 : (double)TotalBytesAfter / BudgetBytes * 100d;
    }
}
