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

        // Constructor accepts dependencies so caller can provide DI-resolved services
        public ScanPage(string language = "vi", int autoPoiId = 0, DatabaseService dbService = null, AudioQueueService audioQueue = null, NarrationService narrationService = null, IAudioGenerator audioGenerator = null)
        {
            InitializeComponent();
            _language = language ?? "vi";
            _autoPoiId = autoPoiId;
            _dbService = dbService ?? new DatabaseService();
            _audioQueue = audioQueue ?? new AudioQueueService(new Services.NoOpAudioService(), new NarrationService(), null, new System.Net.Http.HttpClient());
            _narrationService = narrationService ?? new NarrationService();
            _audioGenerator = audioGenerator;
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
                        // try to find an audio file first
                        var audio = await _dbService.GetAudioByPoiAndLangAsync(poiId, _language);

                        string choice = null;
                        if (audio != null && !string.IsNullOrEmpty(audio.Url) && System.IO.File.Exists(audio.Url))
                        {
                            // Ask user whether to play audio or TTS
                            choice = await DisplayActionSheet("Chọn cách nghe", "Hủy", null, "Phát audio", "Phát TTS");
                            if (choice == "Phát audio")
                            {
                                // Play the audio file via queue
                                var item = new VinhKhanh.Services.AudioItem
                                {
                                    IsTts = false,
                                    FilePath = audio.Url,
                                    Language = _language,
                                    PoiId = poiId,
                                    Priority = 5
                                };
                                _audioQueue.Enqueue(item);
                            }
                            else if (choice == "Phát TTS")
                            {
                                // Play TTS immediately
                                await _narrationService.SpeakAsync(content?.Description ?? content?.Title ?? "", _language);
                            }
                        }
                        else
                        {
                            // No audio file: offer TTS and option to generate audio file then play
                            choice = await DisplayActionSheet("Không tìm thấy file audio. Chọn cách nghe", "Hủy", null, "Phát TTS", "Tạo file audio & phát");
                            if (choice == "Phát TTS")
                            {
                                await _narration_service_fallback(content?.Description ?? content?.Title ?? "");
                            }
                            else if (choice == "Tạo file audio & phát")
                            {
                                if (_audioGenerator != null)
                                {
                                    try
                                    {
                                        var text = content?.Description ?? content?.Title ?? "";
                                        var filename = $"poi_{poiId}_{_language}.mp3";
                                        var outPath = System.IO.Path.Combine(FileSystem.AppDataDirectory, filename);
                                        var ok = await _audioGenerator.GenerateTtsToFileAsync(text, _language, outPath);
                                        if (ok)
                                        {
                                            // save metadata
                                            var model = new VinhKhanh.Shared.AudioModel { PoiId = poiId, Url = outPath, LanguageCode = _language, IsTts = true, IsProcessed = true };
                                            await _dbService.SaveAudioAsync(model);
                                            var item = new VinhKhanh.Services.AudioItem { IsTts = false, FilePath = outPath, PoiId = poiId, Priority = 5 };
                                            _audioQueue.Enqueue(item);
                                        }
                                        else
                                        {
                                            await DisplayAlert("Lỗi", "Không tạo được file audio trên thiết bị này.", "Đóng");
                                        }
                                    }
                                    catch { }
                                }
                                else
                                {
                                    await DisplayAlert("Lỗi", "Tính năng tạo file audio chưa được hỗ trợ trên nền tảng này.", "Đóng");
                                }
                            }
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