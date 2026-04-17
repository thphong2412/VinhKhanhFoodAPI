using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace VinhKhanh.Services
{
    /// <summary>
    /// Optimized Sync Batch Service
    /// Throttles and batches rapid updates to reduce network traffic and UI refresh storms.
    /// Particularly useful for location updates and POI broadcasts.
    /// </summary>
    public interface ISyncBatchService
    {
        /// <summary>
        /// Queue an update for batching. Updates are sent in batches after debounce period.
        /// </summary>
        void QueueUpdate<T>(string channel, T data);

        /// <summary>
        /// Manually flush pending batches (should be called periodically or on demand).
        /// </summary>
        Task FlushAsync();

        /// <summary>
        /// Clear all pending batches.
        /// </summary>
        void Clear();

        /// <summary>
        /// Get batch statistics for diagnostics.
        /// </summary>
        Dictionary<string, object> GetStats();
    }

    public class SyncBatchService : ISyncBatchService
    {
        private readonly ILogger<SyncBatchService> _logger;
        private readonly ConcurrentDictionary<string, List<object>> _pendingBatches = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastFlush = new();

        private const int BatchDebounceMs = 500; // Wait 500ms before sending batch
        private const int MaxBatchSize = 50; // Max items per batch
        private Timer _flushTimer;

        public SyncBatchService(ILogger<SyncBatchService> logger)
        {
            _logger = logger;
            // Start periodic flush timer
            _flushTimer = new Timer(async _ => await FlushAsync(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }

        public void QueueUpdate<T>(string channel, T data)
        {
            if (string.IsNullOrEmpty(channel) || data == null) return;

            try
            {
                var batch = _pendingBatches.GetOrAdd(channel, _ => new List<object>());
                lock (batch)
                {
                    batch.Add(data);

                    // Flush if batch size exceeded
                    if (batch.Count >= MaxBatchSize)
                    {
                        _logger?.LogInformation($"[SyncBatch] Force flush for {channel}: batch size {batch.Count} >= max {MaxBatchSize}");
                        // Will be handled by FlushAsync
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, $"[SyncBatch] Error queuing update for {channel}");
            }
        }

        public async Task FlushAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                var channelsToFlush = new List<string>();

                // Find channels ready to flush (debounce passed or batch full)
                foreach (var kvp in _pendingBatches)
                {
                    var channel = kvp.Key;
                    var batch = kvp.Value;

                    if (batch.Count == 0) continue;

                    var lastFlushTime = _lastFlush.TryGetValue(channel, out var time) ? time : DateTime.MinValue;
                    var timeSinceLastFlush = (now - lastFlushTime).TotalMilliseconds;

                    if (batch.Count >= MaxBatchSize || timeSinceLastFlush >= BatchDebounceMs)
                    {
                        channelsToFlush.Add(channel);
                    }
                }

                // Flush ready channels
                foreach (var channel in channelsToFlush)
                {
                    if (_pendingBatches.TryGetValue(channel, out var batch))
                    {
                        lock (batch)
                        {
                            if (batch.Count > 0)
                            {
                                var itemsToSend = new List<object>(batch);
                                batch.Clear();
                                _lastFlush[channel] = now;

                                _logger?.LogInformation($"[SyncBatch] Flushing {channel}: {itemsToSend.Count} items");

                                // TODO: Actually send via SignalR or event system
                                // Example: await _hubContext.Clients.All.SendAsync(channel, itemsToSend);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, $"[SyncBatch] Error during flush");
            }
        }

        public void Clear()
        {
            try
            {
                _pendingBatches.Clear();
                _lastFlush.Clear();
                _logger?.LogInformation($"[SyncBatch] All batches cleared");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, $"[SyncBatch] Error clearing batches");
            }
        }

        public Dictionary<string, object> GetStats()
        {
            var stats = new Dictionary<string, object>
            {
                { "TotalChannels", _pendingBatches.Count },
                { "TotalPendingItems", _pendingBatches.Values.Sum(b => b?.Count ?? 0) },
                { "MaxBatchSize", MaxBatchSize },
                { "BatchDebounceMs", BatchDebounceMs }
            };

            // Per-channel stats
            var channelStats = new Dictionary<string, int>();
            foreach (var kvp in _pendingBatches)
            {
                channelStats[kvp.Key] = kvp.Value?.Count ?? 0;
            }
            stats["ChannelStats"] = channelStats;

            return stats;
        }
    }
}
