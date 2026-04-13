using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace VinhKhanh.Pages
{
    public class MapPageHelpers
    {
        // Temporary QR generation using Google Chart API (simple, works offline fallback requires ZXing lib)
        public Task<ImageSource> GenerateQrImageSourceAsync(string payload)
        {
            try
            {
                var url = $"https://chart.googleapis.com/chart?cht=qr&chs=600x600&chl={Uri.EscapeDataString(payload)}";
                return Task.FromResult(ImageSource.FromUri(new Uri(url)));
            }
            catch
            {
                return Task.FromResult<ImageSource>(null);
            }
        }
    }
}
