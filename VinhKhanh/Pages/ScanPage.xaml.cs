using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;
using Microsoft.Maui.ApplicationModel;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace VinhKhanh.Pages
{
    public partial class ScanPage : ContentPage
    {
        private bool _isSpeaking = false;
        private string _language = "vi";
        private int _autoPoiId = 0;
        private VinhKhanh.Services.NarrationService _narrationService = new VinhKhanh.Services.NarrationService();
        private VinhKhanh.Services.DatabaseService _dbService = new VinhKhanh.Services.DatabaseService();

        public ScanPage(string language = "vi", int autoPoiId = 0)
        {
            InitializeComponent();
            _language = language ?? "vi";
            _autoPoiId = autoPoiId;
            // ZXing barcode event
            cameraView.BarcodesDetected += OnBarcodeDetected;
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
                    var content = await _dbService.GetContentByPoiIdAsync(_autoPoiId, _language);
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
                    // If QR encodes POI:id or numeric id, try to play the POI narration
                    int poiId = 0;
                    if (!string.IsNullOrEmpty(detectedText))
                    {
                        if (detectedText.StartsWith("POI:", StringComparison.OrdinalIgnoreCase))
                        {
                            var part = detectedText.Substring(4);
                            int.TryParse(part, out poiId);
                        }
                        else
                        {
                            int.TryParse(detectedText, out poiId);
                        }
                    }

                    if (poiId > 0)
                    {
                        var content = await _dbService.GetContentByPoiIdAsync(poiId, _language);
                        if (content != null && !string.IsNullOrEmpty(content.Description))
                        {
                            await _narrationService.SpeakAsync(content.Description, _language);
                        }
                    }
                    else
                    {
                        // Fallback: speak a generic message
                        string speechText = $"Bạn đang ở {detectedText}. Chào mừng đến phố ẩm thực Vĩnh Khánh.";
                        await _narration_service_fallback(speechText);
                    }
                }
                catch { }
                finally { _isSpeaking = false; }
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