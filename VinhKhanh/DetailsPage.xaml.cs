using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls; // FIX LỖI: ContentPage, EventArgs
using Microsoft.Maui.Media;
using VinhKhanh.Shared; // Đảm bảo đúng namespace PoiModel của ông
using System.IO;
using System.Net.Http;

namespace VinhKhanh;

public partial class DetailsPage : ContentPage
{
    private readonly PoiModel _poi;
    private bool _hasSpoken = false;
    private CancellationTokenSource _cts;
    private string _qrCodeValue;

    public DetailsPage(PoiModel poi)
    {
        InitializeComponent();
        _poi = poi;
        _qrCodeValue = _poi.QrCode ?? $"POI:{_poi.Id}";

        var vnContent = _poi.Contents?.FirstOrDefault(c => c.LanguageCode == "vi");

        BindingContext = new
        {
            _poi.Name,
            _poi.ImageUrl,
            Description = vnContent?.Description ?? "Không có mô tả tiếng Việt.",
            Contents = _poi.Contents
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // QR code UI setup
        if (!string.IsNullOrEmpty(_qrCodeValue))
        {
            QrCodeTextLabel.Text = _qrCodeValue;
            QrCodeImage.Source = await GenerateQrCodeImage(_qrCodeValue);
        }
        else
        {
            QrCodeTextLabel.Text = "Không có mã QR";
            QrCodeImage.Source = null;
        }

        if (!_hasSpoken)
        {
            await Task.Delay(1000);
            await AutoSpeakVietnamese();
            _hasSpoken = true;
        }
    }

    private async Task<ImageSource> GenerateQrCodeImage(string text)
    {
        // Sử dụng API miễn phí hoặc thư viện QR code (ZXing.Net.MAUI hoặc Google Chart API)
        // Ở đây dùng Google Chart API cho đơn giản demo
        var url = $"https://chart.googleapis.com/chart?cht=qr&chs=300x300&chl={Uri.EscapeDataString(text)}";
        return ImageSource.FromUri(new Uri(url));
    }

    private async Task AutoSpeakVietnamese()
    {
        try
        {
            var vnContent = _poi.Contents?.FirstOrDefault(c => c.LanguageCode == "vi");

            if (vnContent != null && !string.IsNullOrEmpty(vnContent.Description))
            {
                // Khởi tạo token hủy
                _cts = new CancellationTokenSource();

                var locales = await TextToSpeech.Default.GetLocalesAsync();
                var locale = locales.FirstOrDefault(l => l.Language == "vi" || l.Name.Contains("Vietnam"));

                // Truyền _cts.Token vào để có thể dừng khi cần
                await TextToSpeech.Default.SpeakAsync(vnContent.Description, new SpeechOptions
                {
                    Locale = locale,
                    Pitch = 1.0f,
                    Volume = 1.0f
                }, _cts.Token);
            }
        }
        catch (OperationCanceledException) { /* Bỏ qua khi người dùng chủ động hủy */ }
        catch (Exception) { }
    }

    private async void OnSpeakVietnameseClicked(object sender, EventArgs e)
    {
        // Nếu đang nói thì hủy cái cũ trước khi nói cái mới
        _cts?.Cancel();
        await AutoSpeakVietnamese();
    }

    private async void OnCopyQrCodeClicked(object sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_qrCodeValue))
        {
            await Clipboard.SetTextAsync(_qrCodeValue);
            await DisplayAlert("Đã sao chép", "Mã QR đã được sao chép vào clipboard!", "OK");
        }
    }

    private async void OnOpenScanPageClicked(object sender, EventArgs e)
    {
        // Mở trang ScanPage và truyền autoPoiId để phát thuyết minh luôn
        if (_poi.Id > 0)
        {
            await Navigation.PushAsync(new VinhKhanh.Pages.ScanPage("vi", _poi.Id));
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // CÁCH SỬA LỖI ĐỎ: 
        // Gọi Cancel() trên CancellationTokenSource thay vì TextToSpeech
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
    }
}