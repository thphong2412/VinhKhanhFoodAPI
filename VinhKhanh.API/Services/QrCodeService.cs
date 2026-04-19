using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

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
                var configuredBase = _config["QrPublicBaseUrl"] ?? _config["PublicBaseUrl"];
                var baseUrl = NormalizePublicBaseUrl(configuredBase);
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

        private static string NormalizePublicBaseUrl(string? raw)
        {
            var candidate = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return BuildLanFallbackBaseUrl();
            }

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                return BuildLanFallbackBaseUrl();
            }

            if (uri.Host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase))
            {
                var lanIp = GetPreferredLanIp();
                if (!string.IsNullOrWhiteSpace(lanIp))
                {
                    var builder = new UriBuilder(uri)
                    {
                        Host = lanIp
                    };
                    return builder.Uri.ToString().TrimEnd('/');
                }
            }

            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                var lanIp = GetPreferredLanIp();
                if (!string.IsNullOrWhiteSpace(lanIp))
                {
                    var builder = new UriBuilder(uri)
                    {
                        Host = lanIp
                    };
                    return builder.Uri.ToString().TrimEnd('/');
                }
            }

            if (IPAddress.TryParse(uri.Host, out var parsedIp)
                && parsedIp.AddressFamily == AddressFamily.InterNetwork
                && IsPrivateIpv4(parsedIp)
                && !IsLocalIpv4Address(parsedIp))
            {
                var lanIp = GetPreferredLanIp();
                if (!string.IsNullOrWhiteSpace(lanIp))
                {
                    var builder = new UriBuilder(uri)
                    {
                        Host = lanIp
                    };
                    return builder.Uri.ToString().TrimEnd('/');
                }
            }

            return uri.ToString().TrimEnd('/');
        }

        private static string BuildLanFallbackBaseUrl()
        {
            var lanIp = GetPreferredLanIp();
            if (!string.IsNullOrWhiteSpace(lanIp))
            {
                return $"http://{lanIp}:5291";
            }

            return "http://127.0.0.1:5291";
        }

        private static string? GetPreferredLanIp()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(i => i.OperationalStatus == OperationalStatus.Up)
                    .Where(i => i.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Where(i => i.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                                || i.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                    .Where(i =>
                    {
                        var text = $"{i.Name} {i.Description}";
                        return !text.Contains("virtual", StringComparison.OrdinalIgnoreCase)
                               && !text.Contains("hyper-v", StringComparison.OrdinalIgnoreCase)
                               && !text.Contains("vmware", StringComparison.OrdinalIgnoreCase)
                               && !text.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
                               && !text.Contains("virtualbox", StringComparison.OrdinalIgnoreCase)
                               && !text.Contains("wsl", StringComparison.OrdinalIgnoreCase)
                               && !text.Contains("tap", StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();

                foreach (var nic in interfaces)
                {
                    var hasGateway = nic.GetIPProperties().GatewayAddresses
                        .Any(g => g?.Address != null
                                  && g.Address.AddressFamily == AddressFamily.InterNetwork
                                  && !IPAddress.Any.Equals(g.Address)
                                  && !IPAddress.None.Equals(g.Address));
                    if (!hasGateway)
                    {
                        continue;
                    }

                    var addresses = nic.GetIPProperties().UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(a => a.Address)
                        .Where(a => !IPAddress.IsLoopback(a))
                        .Select(a => a.ToString())
                        .Where(ip => ip.StartsWith("192.168.", StringComparison.Ordinal)
                                     || ip.StartsWith("10.", StringComparison.Ordinal)
                                     || ip.StartsWith("172.", StringComparison.Ordinal))
                        .ToList();

                    var selected = addresses.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        return selected;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsLocalIpv4Address(IPAddress address)
        {
            try
            {
                var allIps = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(i => i.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(i => i.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address)
                    .ToList();

                return allIps.Any(ip => ip.Equals(address));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPrivateIpv4(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            if (bytes.Length != 4) return false;

            return bytes[0] == 10
                   || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168);
        }
    }
}
