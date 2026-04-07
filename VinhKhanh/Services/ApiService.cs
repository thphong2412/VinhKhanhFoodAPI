using System; // FIX LỖI: Exception, TimeSpan, Console
using System.Collections.Generic;
using System.Net.Http; // FIX LỖI: HttpClient
using System.Net.Http.Json; // FIX LỖI: GetFromJsonAsync
using System.Threading.Tasks;
using Microsoft.Maui.Devices; // FIX LỖI: DeviceInfo, DevicePlatform
using VinhKhanh.Shared; // Đảm bảo đúng namespace của Model bên Shared

namespace VinhKhanh.Services
{
    public class ApiService
    {
        private readonly HttpClient _httpClient;

        // LƯU Ý: Nếu dùng máy ảo Android, thay localhost bằng 10.0.2.2
        // Nếu dùng iPhone/Máy thật, dùng IP của máy tính (ví dụ: 192.168.1.x)
        // Dùng đúng số 7174 bạn vừa tìm thấy
        private string BaseUrl = DeviceInfo.Platform == DevicePlatform.Android
    ? "http://10.0.2.2:5291/api/"
    : "http://localhost:5291/api/";

        public ApiService()
        {
            _httpClient = new HttpClient();
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        public async Task<List<PoiModel>> GetPoisAsync()    
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<PoiModel>>($"{BaseUrl}Poi");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi gọi API: {ex.Message}");
                return new List<PoiModel>();
            }
        }
    }
}