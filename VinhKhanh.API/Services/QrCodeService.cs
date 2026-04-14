using System;
using System.IO;
using QRCoder;

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
        /// Generate QR Code dạng Base64 SVG/PNG
        /// </summary>
        public string GenerateQrCode(int poiId, string poiName)
        {
            try
            {
                // Tạo URL deeplink để quét (app sẽ nhận URI này)
                // Format: vinhkhanh://poi/{id}?name={name}&action=viewDetail
                var qrUrl = $"vinhkhanh://poi/{poiId}?name={Uri.EscapeDataString(poiName)}&action=viewDetail";

                // Generate QR Code SVG (nhẹ hơn PNG, dễ scale)
                using (var qr = new QRCodeGenerator())
                {
                    var qrCodeData = qr.CreateQrCode(qrUrl, QRCodeGenerator.ECCLevel.Q);
                    using (var qrCode = new SvgQRCode(qrCodeData))
                    {
                        // Trả về SVG string
                        var svgString = qrCode.GetGraphic(10); // 10px per module
                        // Encode to Base64 để lưu vào DB
                        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svgString));
                        return base64;
                    }
                }
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
