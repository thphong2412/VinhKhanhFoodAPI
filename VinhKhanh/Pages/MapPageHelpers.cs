using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace VinhKhanh.Pages
{
    public class MapPageHelpers
    {
        private static readonly ConcurrentDictionary<string, string> _qrFileCache = new(StringComparer.Ordinal);
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        // Support both stored base64 QR image (SVG/PNG) and raw payload text.
        public async Task<ImageSource?> GenerateQrImageSourceAsync(string payload, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(payload))
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(payload))
                {
                    try
                    {
                        var bytes = Convert.FromBase64String(payload);
                        return ImageSource.FromStream(() => new System.IO.MemoryStream(bytes));
                    }
                    catch
                    {
                        // Not a base64 image, continue with payload URL generation fallback.
                    }
                }

                var cacheKey = payload.Trim();
                if (_qrFileCache.TryGetValue(cacheKey, out var cachedPath) && File.Exists(cachedPath))
                {
                    return ImageSource.FromFile(cachedPath);
                }

                var qrDir = Path.Combine(FileSystem.CacheDirectory, "qr_cache");
                Directory.CreateDirectory(qrDir);
                var fileName = $"qr_{Math.Abs(cacheKey.GetHashCode())}.png";
                var filePath = Path.Combine(qrDir, fileName);

                if (!File.Exists(filePath))
                {
                    var url = $"https://quickchart.io/qr?size=360&margin=1&text={Uri.EscapeDataString(payload)}";
                    var bytes = await _httpClient.GetByteArrayAsync(url, cancellationToken);
                    if (bytes != null && bytes.Length > 0)
                    {
                        await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
                    }
                }

                if (File.Exists(filePath))
                {
                    _qrFileCache[cacheKey] = filePath;
                    return ImageSource.FromFile(filePath);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
