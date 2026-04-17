using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;
using Microsoft.Maui.ApplicationModel;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;
using Microsoft.Maui.Storage;
using VinhKhanh.Services;

namespace VinhKhanh.Pages
{
    public partial class ScanPage : ContentPage
    {
        private bool _isSpeaking = false;
        private string _language = "vi";
        private int _autoPoiId = 0;
        private readonly NarrationService _narrationService;
        private readonly DatabaseService _dbService;
        private readonly AudioQueueService _audioQueue;
        private readonly IAudioGenerator _audioGenerator;
        private readonly ApiService _apiService;

        // Constructor accepts dependencies so caller can provide DI-resolved services
        public ScanPage(string language = "vi", int autoPoiId = 0, DatabaseService dbService = null, AudioQueueService audioQueue = null, NarrationService narrationService = null, IAudioGenerator audioGenerator = null, ApiService apiService = null)
        {
            InitializeComponent();
            _language = language ?? "vi";
            _autoPoiId = autoPoiId;
            _dbService = dbService ?? new DatabaseService();
            _audioQueue = audioQueue ?? new AudioQueueService(new Services.NoOpAudioService(), new NarrationService(), null, new System.Net.Http.HttpClient());
            _narrationService = narrationService ?? new NarrationService();
            _audioGenerator = audioGenerator;
            _apiService = apiService ?? new ApiService();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (cameraView != null)
            {
                var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.Camera>();
                }

                if (status == PermissionStatus.Granted)
                {
                    await Task.Delay(500);
                    // SỬA LỆNH BẬT: ZXing dùng IsDetecting = true
                    cameraView.IsDetecting = true;
                }
                else
                {
                    await DisplayAlert("Quyền Camera", "Ông chưa cho phép dùng Camera thì sao quét mã quán ăn ở Vĩnh Khánh được!", "Để tui bật");
                }
            }

            // If this page was opened with an auto POI id, play its narration immediately
            if (_autoPoiId > 0)
            {
                try
                {
                    var content = await _dbService.GetContentByPoiIdAsync(_autoPoiId, _language)
                                  ?? await _dbService.GetContentByPoiIdAsync(_autoPoiId, "en")
                                  ?? await _dbService.GetContentByPoiIdAsync(_autoPoiId, "vi");
                    if (content != null && !string.IsNullOrEmpty(content.Description))
                    {
                        await _narrationService.SpeakAsync(content.Description, _language);
                    }
                }
                catch { }
            }
        }

        // SỬA CHỖ ĐỎ: BarcodeEventArgs đổi thành BarcodeDetectionEventArgs
        private void OnBarcodeDetected(object sender, BarcodeDetectionEventArgs e)
        {
            if (_isSpeaking || e.Results == null || !e.Results.Any())
                return;

            string detectedText = e.Results.First().Value;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                _isSpeaking = true;
                try
                {
                    int poiId = 0;

                    // ✅ Handle deeplink format: vinhkhanh://poi/{id}?name={name}&action=viewDetail
                    if (detectedText.StartsWith("vinhkhanh://poi/"))
                    {
                        var uriParts = detectedText.Replace("vinhkhanh://poi/", "").Split('?');
                        if (int.TryParse(uriParts[0], out var id))
                        {
                            poiId = id;
                        }
                    }
                    // New public QR format: https://host/qr/{id}?lang=vi
                    else if (Uri.TryCreate(detectedText, UriKind.Absolute, out var parsed)
                             && parsed.Segments.Length >= 3
                             && string.Equals(parsed.Segments[1].Trim('/'), "qr", StringComparison.OrdinalIgnoreCase))
                    {
                        var idSeg = parsed.Segments[2].Trim('/');
                        int.TryParse(idSeg, out poiId);
                    }
                    // Old format: POI:id
                    else if (detectedText.StartsWith("POI:", StringComparison.OrdinalIgnoreCase))
                    {
                        var part = detectedText.Substring(4);
                        int.TryParse(part, out poiId);
                    }
                    // Numeric id only
                    else
                    {
                        int.TryParse(detectedText, out poiId);
                    }

                    if (poiId > 0)
                    {
                        // ✅ Instant narration on successful QR scan
                        cameraView.IsDetecting = false;

                        // Track QR scan event for analytics/admin counter
                        try
                        {
                            var trace = new VinhKhanh.Shared.TraceLog
                            {
                                PoiId = poiId,
                                Latitude = 0,
                                Longitude = 0,
                                ExtraJson = "{\"event\":\"qr_scan\",\"source\":\"mobile_scan\"}",
                                DurationSeconds = null
                            };
                            _ = _apiService?.PostTraceAsync(trace);
                        }
                        catch { }

                        var content = await _dbService.GetContentByPoiIdAsync(poiId, _language)
                                      ?? await _dbService.GetContentByPoiIdAsync(poiId, "en")
                                      ?? await _dbService.GetContentByPoiIdAsync(poiId, "vi");

                        if (content != null && !string.IsNullOrWhiteSpace(content.Description))
                        {
                            await _narrationService.SpeakAsync(content.Description, _language);
                        }
                        else
                        {
                            var poi = (await _dbService.GetPoisAsync()).FirstOrDefault(p => p.Id == poiId);
                            if (!string.IsNullOrWhiteSpace(poi?.Name))
                            {
                                await _narrationService.SpeakAsync(poi.Name, _language);
                            }
                            else
                            {
                                await DisplayAlert("Thiếu dữ liệu", $"Không tìm thấy nội dung thuyết minh cho POI #{poiId}", "OK");
                            }
                        }

                        await Task.Delay(500);
                        cameraView.IsDetecting = true;
                        return;
                    }
                    else
                    {
                        await DisplayAlert("QR không hợp lệ", $"Không thể nhận diện POI từ mã QR: {detectedText}", "OK");
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Lỗi", $"Error: {ex.Message}", "OK");
                }
                finally
                {
                    _isSpeaking = false;
                    cameraView.IsDetecting = true;
                }
            });
        }

        private async Task _narration_service_fallback(string text)
        {
            try
            {
                var locales = await TextToSpeech.Default.GetLocalesAsync();
                var locale = locales.FirstOrDefault(l => l.Language.StartsWith(_language, StringComparison.OrdinalIgnoreCase));
                await TextToSpeech.Default.SpeakAsync(text, new SpeechOptions { Locale = locale });
            }
            catch { }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            if (cameraView != null)
            {
                // SỬA LỆNH TẮT: Tắt Detecting để rảnh tài nguyên
                cameraView.IsDetecting = false;
            }
        }
    }
}