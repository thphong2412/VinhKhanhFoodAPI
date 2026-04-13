using System; // FIX LỖI: Exception, TimeSpan, Console
using System.Collections.Generic;
using System.Net.Http; // FIX LỖI: HttpClient
using System.Net.Http.Json; // FIX LỖI: GetFromJsonAsync
using System.Threading.Tasks;
using Microsoft.Maui.Devices; // FIX LỖI: DeviceInfo, DevicePlatform
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using VinhKhanh.Shared; // Đảm bảo đúng namespace của Model bên Shared

namespace VinhKhanh.Services
{
    public class ApiService
    {
        private HttpClient _httpClient;
        private readonly Microsoft.Extensions.Logging.ILogger<ApiService> _logger;
        private readonly string _deviceId;

        // LƯU Ý: Nếu dùng máy ảo Android, thay localhost bằng 10.0.2.2
        // Nếu dùng iPhone/Máy thật, dùng IP của máy tính (ví dụ: 192.168.1.x)
        private string BaseUrl;

        public ApiService(Microsoft.Extensions.Logging.ILogger<ApiService>? logger = null)
        {
            _logger = logger;

            // Default base by platform (prefer HTTP on development port 5291)
            BaseUrl = DeviceInfo.Platform == DevicePlatform.Android
                ? "http://10.0.2.2:5291/api/"
                : "http://localhost:5291/api/";

            // Create default client
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            _logger?.LogInformation("ApiService initial BaseUrl = {BaseUrl}", BaseUrl);

            // Quick connectivity check and fallback for development scenarios
            TryDetectApiAsync().ConfigureAwait(false);

            // Device identity for anonymous analytics
            try
            {
                _deviceId = Preferences.Get("VinhKhanh_DeviceId", null);
                if (string.IsNullOrEmpty(_deviceId))
                {
                    _deviceId = Guid.NewGuid().ToString();
                    Preferences.Set("VinhKhanh_DeviceId", _deviceId);
                }
            }
            catch { _deviceId = Guid.NewGuid().ToString(); }
        }

        private async Task TryDetectApiAsync()
        {
            try
            {
                // Try primary URL
                var ping = await _httpClient.GetAsync(BaseUrl);
                if (ping.IsSuccessStatusCode) return;
            }
            catch { }

            // If primary failed on Android emulator, try HTTPS port 7174 with insecure handler (dev only)
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                var httpsBase = "https://10.0.2.2:7174/api/";
                try
                {
                    var handler = new HttpClientHandler();
                    handler.ServerCertificateCustomValidationCallback = (msg, cert, chain, errs) => true;
                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
                    var r = await client.GetAsync(httpsBase);
                    if (r.IsSuccessStatusCode)
                    {
                        BaseUrl = httpsBase;
                        // Replace primary http client with handler that accepts dev certs
                        _httpClient.Dispose();
                        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                        return;
                    }
                }
                catch { }
            }
        }

        public async Task<List<PoiModel>> GetPoisAsync()    
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<PoiModel>>($"{BaseUrl}Poi");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lỗi gọi API to {BaseUrl}", BaseUrl);
                return new List<PoiModel>();
            }
        }

        public async Task<List<TourModel>> GetToursAsync()
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<TourModel>>($"{BaseUrl}tour");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lỗi gọi API tour {BaseUrl}", BaseUrl);
                return new List<TourModel>();
            }
        }

        // Post a trace log to analytics endpoint
        public async Task<bool> PostTraceAsync(TraceLog trace)
        {
            try
            {
                if (trace == null) return false;
                // ensure device id set
                if (string.IsNullOrEmpty(trace.DeviceId)) trace.DeviceId = _deviceId;
                var res = await _httpClient.PostAsJsonAsync($"{BaseUrl}analytics", trace);
                return res.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to post trace to API");
                return false;
            }
        }
    }
    }
