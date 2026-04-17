using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http.Json;
using System.Net.Http;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using VinhKhanh.Shared;
using Microsoft.Maui.Devices;

namespace VinhKhanh.Services
{
    public class AudioQueueService
    {
        private readonly IAudioService _player;
        private readonly NarrationService _tts;
        private readonly ILogger<AudioQueueService> _logger;
        private readonly HttpClient _http;
        private readonly string _analyticsBaseUrl;
        private readonly ConcurrentQueue<AudioItem> _queue = new();
        private bool _isProcessing = false;
        // current playing key and priority for interruption/duplicate checks
        private string _currentKey = null;
        private int _currentPriority = 0;
        private readonly object _stateLock = new();

        public AudioQueueService(IAudioService player, NarrationService tts, ILogger<AudioQueueService> logger, HttpClient http)
        {
            _player = player;
            _tts = tts;
            _logger = logger;
            _http = http;
            _analyticsBaseUrl = DeviceInfo.Platform == DevicePlatform.Android
                ? "http://10.0.2.2:5291/api/"
                : "http://localhost:5291/api/";
        }

        public void Enqueue(AudioItem item)
        {
            // Prevent duplicates by simple key
            if (string.IsNullOrEmpty(item?.Key)) item.Key = Guid.NewGuid().ToString();

            // if already processing same key, skip
            if (string.Equals(_currentKey, item.Key, StringComparison.OrdinalIgnoreCase)) return;

            if (_queue.ToArray().Any(q => q.Key == item.Key)) return;

            // If incoming item has higher priority than current, interrupt
            try
            {
                lock (_stateLock)
                {
                    if (item.Priority > _currentPriority)
                    {
                        // clear queued items
                        while (_queue.TryDequeue(out _)) { }
                        // request stop of current playback
                        try { _ = _player.StopAsync(); } catch { }
                        try { _tts?.Stop(); } catch { }
                        _currentKey = null;
                        _currentPriority = 0;
                    }
                }
            }
            catch { }

            _queue.Enqueue(item);
            _ = ProcessQueueAsync();
        }

        public async Task ProcessQueueAsync()
        {
            if (_isProcessing) return;
            _isProcessing = true;

            while (_queue.TryDequeue(out var item))
            {
                try
                {
                    // set current playing key/priority
                    lock (_stateLock)
                    {
                        _currentKey = item.Key;
                        _currentPriority = item.Priority;
                    }

                    // send analytics trace when playback starts (best-effort)
                    try
                    {
                        var trace = new TraceLog
                        {
                            PoiId = item.PoiId,
                            DeviceId = Environment.MachineName,
                            Latitude = 0,
                            Longitude = 0,
                            ExtraJson = "{\"source\":\"audio_queue\"}",
                            TimestampUtc = DateTime.UtcNow
                        };

                        // fire-and-forget
                        // Use current development API endpoint (same as app API service defaults)
                        try { _ = _http.PostAsJsonAsync($"{_analyticsBaseUrl}analytics", trace); } catch { }
                    }
                    catch { }

                    if (item.IsTts)
                    {
                        await _tts.SpeakAsync(item.Text ?? string.Empty, item.Language);
                    }
                    else
                    {
                        var path = item.FilePath ?? string.Empty;
                        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var uri = new Uri(path);
                                var filename = Path.GetFileName(uri.LocalPath);
                                if (string.IsNullOrEmpty(filename)) filename = Guid.NewGuid().ToString() + ".mp3";
                                var cacheFolder = FileSystem.AppDataDirectory;
                                var localPath = Path.Combine(cacheFolder, filename);

                                if (!File.Exists(localPath))
                                {
                                    using var resp = await _http.GetAsync(uri);
                                    resp.EnsureSuccessStatusCode();
                                    await using var fs = File.Create(localPath);
                                    await using var rs = await resp.Content.ReadAsStreamAsync();
                                    await rs.CopyToAsync(fs);

                                    // Cleanup cache after download
                                    try { CleanCache(); } catch { }
                                }

                                await _player.PlayAsync(localPath);
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogWarning(ex, "Failed to download/play remote audio");
                            }
                        }
                        else
                        {
                            await _player.PlayAsync(path);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Audio playback failed");
                }
                finally
                {
                    // clear current key/priority when finished with this item
                    lock (_stateLock)
                    {
                        _currentKey = null;
                        _currentPriority = 0;
                    }
                }
            }

            _isProcessing = false;
        }

        public async Task StopAsync()
        {
            _queue.Clear();
            await _player.StopAsync();
        }

        private void CleanCache()
        {
            try
            {
                var cacheFolder = FileSystem.AppDataDirectory;
                var files = Directory.GetFiles(cacheFolder);
                // remove files older than 7 days
                var threshold = DateTime.UtcNow.AddDays(-7);
                foreach (var f in files)
                {
                    try
                    {
                        var info = new FileInfo(f);
                        if (info.LastWriteTimeUtc < threshold)
                        {
                            info.Delete();
                        }
                    }
                    catch { }
                }

                // if cache too large, remove oldest until under limit (200MB)
                const long maxBytes = 200L * 1024 * 1024;
                var total = files.Sum(fn => { try { return new FileInfo(fn).Length; } catch { return 0L; } });
                if (total > maxBytes)
                {
                    var ordered = files.OrderBy(fn => new FileInfo(fn).LastWriteTimeUtc).ToList();
                    foreach (var f in ordered)
                    {
                        try
                        {
                            var info = new FileInfo(f);
                            total -= info.Length;
                            info.Delete();
                            if (total <= maxBytes) break;
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
    }

    public class AudioItem
    {
        public string Key { get; set; }
        public bool IsTts { get; set; }
        public string Language { get; set; } = "vi";
        public string Text { get; set; }
        public string FilePath { get; set; }
        // Optional: priority to allow interruption of lower-priority items
        public int Priority { get; set; } = 0;
        // Associated POI id (if applicable) used for analytics
        public int PoiId { get; set; } = 0;
    }
}
