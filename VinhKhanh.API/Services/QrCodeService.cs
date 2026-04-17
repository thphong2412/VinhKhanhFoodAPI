using System;

namespace VinhKhanh.API.Services
{
    /// <summary>
    /// Service để generate QR Code cho POI
    /// Sử dụng QRCoder (NuGet package)
    /// </summary>
    public interface IQrCodeService
    {
        string GenerateQrCode(int poiId, string poiName);
    }

    public class QrCodeService : IQrCodeService
    {
        private readonly IConfiguration _config;

        public QrCodeService(IConfiguration config)
        {
            _config = config;
        }

        /// <summary>
        /// Generate payload URL public cho QR code
        /// </summary>
        public string GenerateQrCode(int poiId, string poiName)
        {
            try
            {
                // QR payload public: khách chưa cài app vẫn quét và nghe ngay trên web
                // Có thể override bằng appsettings: PublicBaseUrl hoặc QrPublicBaseUrl
                var configuredBase = _config["QrPublicBaseUrl"] ?? _config["PublicBaseUrl"] ?? "http://localhost:5291";
                var baseUrl = configuredBase.Trim().TrimEnd('/');
                var lang = (_config["DefaultLanguage"] ?? "vi").Trim().ToLowerInvariant();

                var qrUrl = $"{baseUrl}/qr/{poiId}?lang={Uri.EscapeDataString(lang)}";
                return qrUrl;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"❌ QR Code Generation Error: {ex.Message}");
                // Return empty nếu lỗi
                return string.Empty;
            }
        }
    }
}
