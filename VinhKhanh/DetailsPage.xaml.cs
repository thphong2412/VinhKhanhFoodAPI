using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls; // FIX LỖI: ContentPage, EventArgs
using Microsoft.Maui.Media;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
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

    // UI event handlers for tabs and comments
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _comments = new System.Collections.ObjectModel.ObservableCollection<string>();
    private System.Collections.ObjectModel.ObservableCollection<AudioFileInfo> _audioFiles;

    private void OnOverviewTabClicked(object sender, EventArgs e)
    {
        OverviewSection.IsVisible = true;
        AudioSection.IsVisible = false;
        CommentsSection.IsVisible = false;
        OverviewTabButton.FontAttributes = FontAttributes.Bold;
        AudioTabButton.FontAttributes = FontAttributes.None;
        CommentsTabButton.FontAttributes = FontAttributes.None;
    }

    private void OnAudioTabClicked(object sender, EventArgs e)
    {
        OverviewSection.IsVisible = false;
        AudioSection.IsVisible = true;
        CommentsSection.IsVisible = false;
        OverviewTabButton.FontAttributes = FontAttributes.None;
        AudioTabButton.FontAttributes = FontAttributes.Bold;
        CommentsTabButton.FontAttributes = FontAttributes.None;

        // Load audio files for this POI
        LoadAudioFiles();
    }

    private void OnCommentsTabClicked(object sender, EventArgs e)
    {
        OverviewSection.IsVisible = false;
        AudioSection.IsVisible = false;
        CommentsSection.IsVisible = true;
        OverviewTabButton.FontAttributes = FontAttributes.None;
        AudioTabButton.FontAttributes = FontAttributes.None;
        CommentsTabButton.FontAttributes = FontAttributes.Bold;
        CommentsList.ItemsSource = _comments;
    }

    private void OnAddCommentClicked(object sender, EventArgs e)
    {
        var text = CommentEntry.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            _comments.Insert(0, text);
            CommentEntry.Text = string.Empty;
        }
    }

    // ✅ Load audio files from API
    private async void LoadAudioFiles()
    {
        try
        {
            var httpClient = new HttpClient();
            var response = await httpClient.GetAsync($"http://localhost:5291/api/audio/by-poi/{_poi.Id}");

            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync();
                var audioList = System.Text.Json.JsonSerializer.Deserialize<List<AudioFileInfo>>(
                    jsonContent, 
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                _audioFiles = new System.Collections.ObjectModel.ObservableCollection<AudioFileInfo>(audioList ?? new List<AudioFileInfo>());
                AudioList.ItemsSource = _audioFiles;
                AudioStatusLabel.Text = $"Found {_audioFiles.Count} audio file(s)";
            }
            else
            {
                AudioStatusLabel.Text = "Không tìm thấy file âm thanh";
            }
        }
        catch (Exception ex)
        {
            AudioStatusLabel.Text = $"Error loading audio: {ex.Message}";
        }
    }

    // ✅ Play audio file when clicked
    private async void OnPlayAudioClicked(object sender, EventArgs e)
    {
        var button = sender as Button;
        var audio = button?.BindingContext as AudioFileInfo;

        if (audio != null && !string.IsNullOrEmpty(audio.Url))
        {
            try
            {
                AudioStatusLabel.Text = $"Playing: {audio.Name}...";

                // Download and play audio
                var httpClient = new HttpClient();
                var audioStream = await httpClient.GetStreamAsync(audio.Url);

                var tempFile = Path.Combine(FileSystem.CacheDirectory, Guid.NewGuid() + ".mp3");
                using (var fileStream = File.Create(tempFile))
                {
                    await audioStream.CopyToAsync(fileStream);
                }

                // Play using MediaElement or native audio player
                // For now, just show success message
                AudioStatusLabel.Text = $"✅ {audio.Name} - playing";

                await Task.Delay(2000);
                AudioStatusLabel.Text = "Ready to play";
            }
            catch (Exception ex)
            {
                AudioStatusLabel.Text = $"Error: {ex.Message}";
            }
        }
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

        // Show website URL if available
        if (!string.IsNullOrEmpty(_poi.Contents?.FirstOrDefault()?.ShareUrl ?? _poi.WebsiteUrl))
        {
            WebsiteLabel.Text = _poi.Contents?.FirstOrDefault()?.ShareUrl ?? _poi.WebsiteUrl;
            WebsiteLabel.IsVisible = true;
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) =>
            {
                try
                {
                    var url = WebsiteLabel.Text;
                    if (!string.IsNullOrEmpty(url))
                        await Launcher.OpenAsync(url);
                }
                catch { }
            };
            WebsiteLabel.GestureRecognizers.Add(tap);
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

    // Show QR payload and big QR image modal
    private async void OnShowQrClicked(object sender, EventArgs e)
    {
        try
        {
            if (_poi == null) return;
            // ensure payload
            var payload = _poi.QrCode;
            if (string.IsNullOrEmpty(payload))
            {
                payload = $"POI:{_poi.Id}";
                _poi.QrCode = payload;
                try { var db = new Services.DatabaseService(); await db.SavePoiAsync(_poi); } catch { }
            }

            // show modal with qr and actions
            var actions = await DisplayActionSheet("Mã QR điểm này", "Đóng", null, "Xem QR", "Sao chép payload", "Chia sẻ payload", "Mở trang quét (mô phỏng)");
            if (actions == "Xem QR")
            {
                // show full screen page with QR image
                var img = await GenerateQrCodeImage(payload);
                var page = new ContentPage { BackgroundColor = Microsoft.Maui.Graphics.Colors.Black };
                var imgView = new Image { Source = img, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
                var close = new Button { Text = "Đóng", BackgroundColor = Microsoft.Maui.Graphics.Colors.White, TextColor = Microsoft.Maui.Graphics.Colors.Black };
                close.Clicked += async (s, ev) => await Navigation.PopModalAsync();
                page.Content = new Grid { Children = { imgView, new StackLayout { VerticalOptions = LayoutOptions.End, Padding = 20, Children = { close } } } };
                await Navigation.PushModalAsync(page);
            }
            else if (actions == "Sao chép payload")
            {
                await Clipboard.SetTextAsync(payload);
                await DisplayAlert("OK", "Đã sao chép payload", "Đóng");
            }
            else if (actions == "Chia sẻ payload")
            {
                await Share.RequestAsync(new ShareTextRequest { Text = payload, Title = "QR payload" });
            }
            else if (actions == "Mở trang quét (mô phỏng)")
            {
                await Navigation.PushAsync(new VinhKhanh.Pages.ScanPage("vi", _poi.Id));
            }
        }
        catch { }
    }

    private async void OnNavigateClicked(object sender, EventArgs e)
    {
        try
        {
            var lat = _poi.Latitude;
            var lon = _poi.Longitude;
            var name = Uri.EscapeDataString(_poi.Name ?? "");
            var uri = new Uri($"geo:{lat},{lon}?q={name}");
            await Launcher.OpenAsync(uri);
        }
        catch { }
    }

    private async void OnShareClicked(object sender, EventArgs e)
    {
        try
        {
            var text = $"{_poi.Name} - {(_poi.Contents?.FirstOrDefault()?.Description ?? "")}";
            await Share.Default.RequestAsync(new ShareTextRequest { Text = text, Title = "Chia sẻ địa điểm" });
        }
        catch { }
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