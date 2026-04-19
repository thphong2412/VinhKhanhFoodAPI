using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Media;
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
        private readonly ConcurrentDictionary<string, DateTime> _recentlyPlayed = new(StringComparer.OrdinalIgnoreCase);
        private readonly TimeSpan _recentlyPlayedTtl = TimeSpan.FromSeconds(12);
        private CancellationTokenSource? _playbackCts;

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
            if (item == null) return;
            // Prevent duplicates by simple key
            if (string.IsNullOrEmpty(item?.Key)) item.Key = Guid.NewGuid().ToString();
            CleanupRecentKeys();

            // if already processing same key, skip
            if (string.Equals(_currentKey, item.Key, StringComparison.OrdinalIgnoreCase)) return;

            if (_queue.ToArray().Any(q => q.Key == item.Key)) return;
            if (_recentlyPlayed.TryGetValue(item.Key, out var playedAt) && DateTime.UtcNow - playedAt < _recentlyPlayedTtl) return;

            // If incoming item has higher priority than current, interrupt
            try
            {
                lock (_stateLock)
                {
                    if (item.Priority > _currentPriority)
                    {
                        // clear queued items
                        while (_queue.TryDequeue(out _)) { }
                        try
                        {
                            _playbackCts?.Cancel();
                        }
                        catch { }
                        finally
                        {
                            _playbackCts = null;
                        }
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
                if (item == null) continue;
                try
                {
                    // set current playing key/priority
                    lock (_stateLock)
                    {
                        _currentKey = item.Key;
                        _currentPriority = item.Priority;
                    }

                    if (_recentlyPlayed.TryGetValue(item.Key, out var lastPlayed) && DateTime.UtcNow - lastPlayed < _recentlyPlayedTtl)
                    {
                        continue;
                    }

                    CancellationToken token;
                    lock (_stateLock)
                    {
                        _playbackCts?.Dispose();
                        _playbackCts = new CancellationTokenSource();
                        token = _playbackCts.Token;
                    }
                    if (token.IsCancellationRequested) continue;

                    // send analytics trace when playback starts (best-effort)
                    try
                    {
                        // Include explicit event name so backend analytics recognizes the play event
                        var eventName = item.IsTts ? "tts_play" : "audio_play";
                        var meta = new
                        {
                            @event = eventName,
                            source = "app_audio_queue",
                            lang = item.Language ?? string.Empty,
                            mode = item.IsTts ? "tts" : "audio"
                        };

                        var trace = new TraceLog
                        {
                            PoiId = item.PoiId,
                            DeviceId = Environment.MachineName,
                            Latitude = 0,
                            Longitude = 0,
                            ExtraJson = JsonSerializer.Serialize(meta),
                            TimestampUtc = DateTime.UtcNow
                        };

                        // fire-and-forget post to analytics endpoint
                        try { _ = _http.PostAsJsonAsync($"{_analyticsBaseUrl}analytics", trace); } catch { }
                    }
                    catch { }

                    if (item.IsTts)
                    {
                        if (token.IsCancellationRequested) continue;
                        var spoken = await TryPlayRemoteTtsAsync(item.Text ?? string.Empty, item.Language, token);
                        if (!spoken)
                        {
                            await _tts.SpeakAsync(item.Text ?? string.Empty, item.Language);
                        }
                    }
                    else
                    {
                        if (token.IsCancellationRequested) continue;
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

                                if (token.IsCancellationRequested) continue;
                                await _player.PlayAsync(localPath);
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogWarning(ex, "Failed to download/play remote audio");
                            }
                        }
                        else
                        {
                            if (token.IsCancellationRequested) continue;
                            await _player.PlayAsync(path);
                        }
                    }

                    _recentlyPlayed[item.Key] = DateTime.UtcNow;
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
                        try
                        {
                            _playbackCts?.Dispose();
                        }
                        catch { }
                        _playbackCts = null;
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
            try
            {
                lock (_stateLock)
                {
                    _playbackCts?.Cancel();
                    _playbackCts?.Dispose();
                    _playbackCts = null;
                    _currentKey = null;
                    _currentPriority = 0;
                }
            }
            catch { }
            try { _tts?.Stop(); } catch { }
            await _player.StopAsync();
        }

        private async Task<bool> TryPlayRemoteTtsAsync(string text, string language, CancellationToken token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return false;
                var lang = NormalizeLanguageCode(language);
                if (string.IsNullOrWhiteSpace(lang)) lang = "en";

                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{lang}:{text}"))).ToLowerInvariant()[..16];
                var localPath = Path.Combine(FileSystem.AppDataDirectory, $"remote_tts_{lang}_{hash}.mp3");

                if (!File.Exists(localPath))
                {
                    var requestUrl = $"https://translate.google.com/translate_tts?ie=UTF-8&client=tw-ob&tl={Uri.EscapeDataString(lang)}&q={Uri.EscapeDataString(text)}";
                    using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                    req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Android 14; Mobile)");

                    using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
                    if (!response.IsSuccessStatusCode) return false;

                    await using var fs = File.Create(localPath);
                    await response.Content.CopyToAsync(fs, token);
                }

                if (!File.Exists(localPath)) return false;
                if (token.IsCancellationRequested) return false;

                await _player.PlayAsync(localPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeLanguageCode(string? language)
        {
            var normalized = (language ?? "en").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized)) return "en";
            if (normalized.Contains('-')) normalized = normalized.Split('-')[0];
            if (normalized.Contains('_')) normalized = normalized.Split('_')[0];

            return normalized switch
            {
                "vn" => "vi",
                "eng" => "en",
                "jp" => "ja",
                "kr" => "ko",
                "cn" => "zh",
                _ => normalized
            };
        }

        private void CleanupRecentKeys()
        {
            try
            {
                var threshold = DateTime.UtcNow - _recentlyPlayedTtl;
                foreach (var item in _recentlyPlayed.ToArray())
                {
                    if (item.Value < threshold)
                    {
                        _recentlyPlayed.TryRemove(item.Key, out _);
                    }
                }
            }
            catch { }
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
