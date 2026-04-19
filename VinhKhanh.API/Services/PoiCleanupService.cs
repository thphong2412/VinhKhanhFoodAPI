using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;
using VinhKhanh.Shared;

namespace VinhKhanh.API.Services
{
    /// <summary>
    /// Service to handle cleanup of POI-related files and data when a POI is deleted.
    /// This ensures cascade cleanup for audio files, translations, and related disk storage.
    /// </summary>
    public interface IPoiCleanupService
    {
        /// <summary>
        /// Perform complete cleanup for a deleted POI including audio files and disk storage.
        /// </summary>
        Task CleanupPoiAsync(int poiId);

        /// <summary>
        /// Remove audio files from disk for a given POI.
        /// </summary>
        Task CleanupAudioFilesAsync(int poiId);

        /// <summary>
        /// Remove image files from disk for a given POI.
        /// </summary>
        Task CleanupImageFilesAsync(int poiId);
    }

    public class PoiCleanupService : IPoiCleanupService
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<PoiCleanupService> _logger;

        public PoiCleanupService(AppDbContext db, IWebHostEnvironment env, ILogger<PoiCleanupService> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        public async Task CleanupPoiAsync(int poiId)
        {
            try
            {
                // Clean up audio files from disk
                await CleanupAudioFilesAsync(poiId);
                await CleanupImageFilesAsync(poiId);

                // Note: Contents and AudioFiles records are auto-deleted by EF Core cascade delete
                // No need to manually delete from DB here

                _logger.LogInformation($"[PoiCleanupService] Cleanup completed for POI {poiId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[PoiCleanupService] Error cleaning up POI {poiId}");
            }
        }

        public async Task CleanupAudioFilesAsync(int poiId)
        {
            try
            {
                // Get all audio files associated with this POI
                var audioFiles = await _db.AudioFiles
                    .Where(a => a.PoiId == poiId)
                    .ToListAsync();

                var contentAudioUrls = await _db.PointContents
                    .Where(c => c.PoiId == poiId && !string.IsNullOrWhiteSpace(c.AudioUrl))
                    .Select(c => c.AudioUrl!)
                    .ToListAsync();

                if (!audioFiles.Any() && !contentAudioUrls.Any())
                {
                    _logger.LogInformation($"[PoiCleanupService] No audio files to clean for POI {poiId}");
                    return;
                }

                var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var audio in audioFiles)
                {
                    if (!string.IsNullOrWhiteSpace(audio.Url)) urls.Add(audio.Url);
                }
                foreach (var url in contentAudioUrls)
                {
                    if (!string.IsNullOrWhiteSpace(url)) urls.Add(url);
                }

                // Delete audio files from disk
                foreach (var url in urls)
                {
                    try
                    {
                        TryDeleteLocalFileByUrl(url, "audio");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"[PoiCleanupService] Failed to delete audio url for POI {poiId}: {url}");
                    }
                }

                _logger.LogInformation($"[PoiCleanupService] Audio cleanup completed for POI {poiId}: {urls.Count} url entries");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[PoiCleanupService] Error cleaning up audio files for POI {poiId}");
            }
        }

        public async Task CleanupImageFilesAsync(int poiId)
        {
            try
            {
                var poi = await _db.PointsOfInterest.AsNoTracking().FirstOrDefaultAsync(p => p.Id == poiId);
                if (poi == null || string.IsNullOrWhiteSpace(poi.ImageUrl)) return;

                var urls = poi.ImageUrl
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var imageUrl in urls)
                {
                    TryDeleteLocalFileByUrl(imageUrl, "image");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[PoiCleanupService] Error cleaning up image files for POI {poiId}");
            }
        }

        private void TryDeleteLocalFileByUrl(string url, string fileType)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var sanitized = url.Split('?', '#')[0].Trim();
            if (string.IsNullOrWhiteSpace(sanitized)) return;

            string filePath;
            if (Path.IsPathRooted(sanitized))
            {
                filePath = sanitized;
            }
            else
            {
                var relative = sanitized.TrimStart('~').TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                // Ưu tiên đường dẫn vật lý trong wwwroot vì URL public thường dạng /uploads/... hoặc /tts-cache/...
                var inWwwRoot = Path.Combine(_env.ContentRootPath, "wwwroot", relative);
                var inContentRoot = Path.Combine(_env.ContentRootPath, relative);
                filePath = File.Exists(inWwwRoot) ? inWwwRoot : inContentRoot;
            }

            if (!File.Exists(filePath)) return;

            File.Delete(filePath);
            _logger.LogInformation("[PoiCleanupService] Deleted {FileType} file: {FilePath}", fileType, filePath);
        }
    }
}
