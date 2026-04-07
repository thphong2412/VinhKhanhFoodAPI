using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls; // FIX LỖI: ContentPage, EventArgs
using Microsoft.Maui.Media;
using VinhKhanh.Shared; // Đảm bảo đúng namespace PoiModel của ông

namespace VinhKhanh;

public partial class DetailsPage : ContentPage
{
    private readonly PoiModel _poi;
    private bool _hasSpoken = false;
    // Dùng cái này để hủy giọng nói khi thoát trang
    private CancellationTokenSource _cts;

    public DetailsPage(PoiModel poi)
    {
        InitializeComponent();
        _poi = poi;

        var vnContent = _poi.Contents?.FirstOrDefault(c => c.LanguageCode == "vi");

        BindingContext = new
        {
            _poi.Name,
            _poi.ImageUrl,
            Description = vnContent?.Description ?? "Không có mô tả tiếng Việt."
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_hasSpoken)
        {
            await Task.Delay(1000);
            await AutoSpeakVietnamese();
            _hasSpoken = true;
        }
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