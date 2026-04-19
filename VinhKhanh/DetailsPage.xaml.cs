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
using System.Net.Http.Json;
using System.Collections.Generic;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using VinhKhanh.Services;

namespace VinhKhanh;

public partial class DetailsPage : ContentPage
{
    private readonly PoiModel _poi;
    private readonly string _languageCode;
    private bool _hasSpoken = false;
    private CancellationTokenSource _cts;
    private TapGestureRecognizer? _websiteTapRecognizer;
    private bool _isAppearingInitialized;
    private bool _isLoadingAudioFiles;
    private bool _isPlayingAudioFile;
    private string _qrCodeValue;
    private readonly Dictionary<string, string> _uiTextCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAudioService _audioService;
    private readonly ApiService? _apiService;
    private string _activeApiAuthority;

    public DetailsPage(PoiModel poi, string languageCode = "vi")
    {
        InitializeComponent();
        _poi = poi;
        _languageCode = NormalizeLanguageCode(languageCode);
        _qrCodeValue = _poi.QrCode ?? $"POI:{_poi.Id}";
        _audioService = ResolveAudioService();
        _apiService = ResolveApiService();
        _activeApiAuthority = ResolveApiAuthority();

        var selectedContent = _poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, _languageCode, StringComparison.OrdinalIgnoreCase))
                            ?? _poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, "en", StringComparison.OrdinalIgnoreCase))
                            ?? _poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));

        BindingContext = new
        {
            _poi.Name,
            _poi.ImageUrl,
            Description = selectedContent?.Description ?? "Nội dung đang được cập nhật.",
            PriceRange = selectedContent?.GetNormalizedPriceRangeDisplay() ?? string.Empty,
            OpenStatus = BuildOpenStatusText(selectedContent?.OpeningHours, _languageCode),
            OpenStatusColor = BuildOpenStatusColor(selectedContent?.OpeningHours),
            Contents = _poi.Contents
        };
    }

    private static string BuildOpenStatusText(string? openingHours, string languageCode)
    {
        var lang = NormalizeLanguageCode(languageCode);
        if (string.IsNullOrWhiteSpace(openingHours))
        {
            return "Status unavailable";
        }

        var parts = openingHours.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !TimeSpan.TryParse(parts[0], out var start)
            || !TimeSpan.TryParse(parts[1], out var end))
        {
            return "Status unavailable";
        }

        var now = DateTime.Now.TimeOfDay;
        var isOpen = start <= end
            ? now >= start && now <= end
            : now >= start || now <= end;

        if (lang == "vi")
        {
            return isOpen ? "Đang mở cửa" : "Đang đóng cửa";
        }

        return isOpen ? "Open now" : "Closed now";
    }

    private static Microsoft.Maui.Graphics.Color BuildOpenStatusColor(string? openingHours)
    {
        if (string.IsNullOrWhiteSpace(openingHours))
        {
            return Microsoft.Maui.Graphics.Color.FromArgb("#9E9E9E");
        }

        var parts = openingHours.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !TimeSpan.TryParse(parts[0], out var start)
            || !TimeSpan.TryParse(parts[1], out var end))
        {
            return Microsoft.Maui.Graphics.Color.FromArgb("#9E9E9E");
        }

        var now = DateTime.Now.TimeOfDay;
        var isOpen = start <= end
            ? now >= start && now <= end
            : now >= start || now <= end;

        return isOpen
            ? Microsoft.Maui.Graphics.Color.FromArgb("#388E3C")
            : Microsoft.Maui.Graphics.Color.FromArgb("#D32F2F");
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

    private ApiService? ResolveApiService()
    {
        try
        {
            return Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(ApiService)) as ApiService;
        }
        catch
        {
            return null;
        }
    }

    // ✅ Load audio files from API
    private async void LoadAudioFiles()
    {
        if (_isLoadingAudioFiles) return;
        _isLoadingAudioFiles = true;
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _activeApiAuthority = ResolveApiAuthority();
            var response = await httpClient.GetAsync($"{_activeApiAuthority}/api/audio/by-poi/{_poi.Id}");

            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync();
                var audioList = System.Text.Json.JsonSerializer.Deserialize<List<AudioFileInfo>>(
                    jsonContent, 
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (audioList != null)
                {
                    foreach (var a in audioList)
                    {
                        if (string.IsNullOrWhiteSpace(a.Name))
                        {
                            a.Name = !string.IsNullOrWhiteSpace(a.Url)
                                ? Path.GetFileName((a.Url ?? string.Empty).Split('?')[0])
                                : $"audio_{a.Id}.mp3";
                        }
                        if (!string.IsNullOrWhiteSpace(a.Url) && !Uri.TryCreate(a.Url, UriKind.Absolute, out _))
                        {
                            a.Url = new Uri(new Uri(_activeApiAuthority + "/"), a.Url.TrimStart('/')).ToString();
                        }
                    }

                    audioList = audioList
                        .OrderBy(x => x.LanguageCode)
                        .ThenBy(x => x.IsTts ? 0 : 1)
                        .ThenByDescending(x => x.CreatedAtUtc)
                        .ToList();
                }

                var preferredLang = NormalizeLanguageCode(_languageCode);
                var filtered = (audioList ?? new List<AudioFileInfo>())
                    .Where(x => x != null && string.Equals(NormalizeLanguageCode(x.LanguageCode), preferredLang, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!filtered.Any() && !string.Equals(preferredLang, "en", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = (audioList ?? new List<AudioFileInfo>())
                        .Where(x => x != null && string.Equals(NormalizeLanguageCode(x.LanguageCode), "en", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                if (!filtered.Any() && !string.Equals(preferredLang, "vi", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = (audioList ?? new List<AudioFileInfo>())
                        .Where(x => x != null && string.Equals(NormalizeLanguageCode(x.LanguageCode), "vi", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                if (!filtered.Any())
                {
                    filtered = (audioList ?? new List<AudioFileInfo>()).ToList();
                }

                _audioFiles = new System.Collections.ObjectModel.ObservableCollection<AudioFileInfo>(filtered);
                AudioList.ItemsSource = _audioFiles;
                AudioStatusLabel.Text = await LocalizeAsync($"{_audioFiles.Count} audio file(s)", _languageCode);
                await TrackAnalyticsAsync("audio_list_open", "audio_tab");
            }
            else
            {
                AudioStatusLabel.Text = await LocalizeAsync("No audio files found", _languageCode);
            }
        }
        catch (Exception ex)
        {
            var errorPrefix = await LocalizeAsync("Error loading audio", _languageCode);
            AudioStatusLabel.Text = $"{errorPrefix}: {ex.Message}";
        }
        finally
        {
            _isLoadingAudioFiles = false;
        }
    }

    // ✅ Play audio file when clicked
    private async void OnPlayAudioClicked(object sender, EventArgs e)
    {
        if (_isPlayingAudioFile) return;
        var button = sender as Button;
        var audio = button?.BindingContext as AudioFileInfo;

        if (audio != null && !string.IsNullOrEmpty(audio.Url))
        {
            _isPlayingAudioFile = true;
            try
            {
                var playingText = await LocalizeAsync("Playing", _languageCode);
                AudioStatusLabel.Text = $"✅ {playingText}: {audio.Name}";

                var playableUrl = ResolveAbsoluteApiUrl(audio.Url);
                await _audioService.StopAsync();
                await _audioService.PlayAsync(playableUrl);
                await TrackAnalyticsAsync("audio_play", "audio_file", audio.Name, audio.Url, audio.LanguageCode);
            }
            catch (Exception ex)
            {
                var errorText = await LocalizeAsync("Error", _languageCode);
                AudioStatusLabel.Text = $"{errorText}: {ex.Message}";
            }
            finally
            {
                _isPlayingAudioFile = false;
            }
        }
        else
        {
            _isPlayingAudioFile = false;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_isAppearingInitialized) return;
        _isAppearingInitialized = true;

        await ApplyLocalizedUiAsync();

        // QR code UI setup
        if (!string.IsNullOrEmpty(_qrCodeValue))
        {
            QrCodeTextLabel.Text = _qrCodeValue;
            QrCodeImage.Source = await GenerateQrCodeImage(_qrCodeValue);
        }
        else
        {
            QrCodeTextLabel.Text = await LocalizeAsync("No QR code", _languageCode);
            QrCodeImage.Source = null;
        }

        if (!_hasSpoken)
        {
            await Task.Delay(1000);
            await AutoSpeakVietnamese();
            _hasSpoken = true;
        }

        try
        {
            var selectedContent = _poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, _languageCode, StringComparison.OrdinalIgnoreCase))
                               ?? _poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, "en", StringComparison.OrdinalIgnoreCase))
                               ?? _poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
            var openStatusText = BuildOpenStatusText(selectedContent?.OpeningHours, _languageCode);
            if (LblOpenStatus != null)
            {
                var localizedOpen = await LocalizeAsync(openStatusText, _languageCode);
                LblOpenStatus.Text = localizedOpen;
            }
        }
        catch { }

        // Show website URL if available
        if (!string.IsNullOrEmpty(_poi.Contents?.FirstOrDefault()?.ShareUrl ?? _poi.WebsiteUrl))
        {
            WebsiteLabel.Text = _poi.Contents?.FirstOrDefault()?.ShareUrl ?? _poi.WebsiteUrl;
            WebsiteLabel.IsVisible = true;
            if (_websiteTapRecognizer != null)
            {
                WebsiteLabel.GestureRecognizers.Remove(_websiteTapRecognizer);
            }

            _websiteTapRecognizer = new TapGestureRecognizer();
            _websiteTapRecognizer.Tapped += async (s, e) =>
            {
                try
                {
                    var url = WebsiteLabel.Text;
                    if (!string.IsNullOrEmpty(url))
                        await Launcher.OpenAsync(url);
                }
                catch { }
            };
            WebsiteLabel.GestureRecognizers.Add(_websiteTapRecognizer);
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
            var selectedContent = _poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, _languageCode, StringComparison.OrdinalIgnoreCase))
                               ?? _poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, "en", StringComparison.OrdinalIgnoreCase))
                               ?? _poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));

            if (selectedContent != null && !string.IsNullOrEmpty(selectedContent.Description))
            {
                // Khởi tạo token hủy
                _cts = new CancellationTokenSource();

                var locales = await TextToSpeech.Default.GetLocalesAsync();
                var locale = locales.FirstOrDefault(l => string.Equals(l.Language, _languageCode, StringComparison.OrdinalIgnoreCase))
                          ?? locales.FirstOrDefault(l => l.Language == "vi" || l.Name.Contains("Vietnam"));

                // Truyền _cts.Token vào để có thể dừng khi cần
                await TextToSpeech.Default.SpeakAsync(selectedContent.Description, new SpeechOptions
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
        await TrackAnalyticsAsync("tts_play", "quick_listen", languageCode: _languageCode);
        await AutoSpeakVietnamese();
    }

    private void OnStopAudioClicked(object sender, EventArgs e)
    {
        try
        {
            _cts?.Cancel();
            _ = _audioService.StopAsync();
            AudioStatusLabel.Text = LocalizeAsync("Audio stopped", _languageCode).GetAwaiter().GetResult();
        }
        catch
        {
            AudioStatusLabel.Text = LocalizeAsync("Audio stopped", _languageCode).GetAwaiter().GetResult();
        }
    }

    private async void OnCopyQrCodeClicked(object sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_qrCodeValue))
        {
            await Clipboard.SetTextAsync(_qrCodeValue);
            await DisplayAlert(
                await LocalizeAsync("Copied", _languageCode),
                await LocalizeAsync("QR code copied to clipboard", _languageCode),
                await LocalizeAsync("OK", _languageCode));
        }
    }

    private async void OnOpenScanPageClicked(object sender, EventArgs e)
    {
        // Mở trang ScanPage và truyền autoPoiId để phát thuyết minh luôn
        if (_poi.Id > 0)
        {
            await Navigation.PushAsync(new VinhKhanh.Pages.ScanPage(_languageCode, _poi.Id));
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
            var qrTitle = await LocalizeAsync("QR code for this place", _languageCode);
            var closeText = await LocalizeAsync("Close", _languageCode);
            var viewQrText = await LocalizeAsync("View QR", _languageCode);
            var copyPayloadText = await LocalizeAsync("Copy payload", _languageCode);
            var sharePayloadText = await LocalizeAsync("Share payload", _languageCode);
            var openScanText = await LocalizeAsync("Open scan page (simulation)", _languageCode);

            var actions = await DisplayActionSheet(qrTitle, closeText, null, viewQrText, copyPayloadText, sharePayloadText, openScanText);
            if (actions == viewQrText)
            {
                // show full screen page with QR image
                var img = await GenerateQrCodeImage(payload);
                var page = new ContentPage { BackgroundColor = Microsoft.Maui.Graphics.Colors.Black };
                var imgView = new Image { Source = img, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
                var close = new Button { Text = closeText, BackgroundColor = Microsoft.Maui.Graphics.Colors.White, TextColor = Microsoft.Maui.Graphics.Colors.Black };
                close.Clicked += async (s, ev) => await Navigation.PopModalAsync();
                page.Content = new Grid { Children = { imgView, new StackLayout { VerticalOptions = LayoutOptions.End, Padding = 20, Children = { close } } } };
                await Navigation.PushModalAsync(page);
            }
            else if (actions == copyPayloadText)
            {
                await Clipboard.SetTextAsync(payload);
                await DisplayAlert(await LocalizeAsync("OK", _languageCode), await LocalizeAsync("Payload copied", _languageCode), closeText);
            }
            else if (actions == sharePayloadText)
            {
                await Share.RequestAsync(new ShareTextRequest { Text = payload, Title = await LocalizeAsync("QR payload", _languageCode) });
            }
            else if (actions == openScanText)
            {
                await Navigation.PushAsync(new VinhKhanh.Pages.ScanPage(_languageCode, _poi.Id));
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

    private async Task ApplyLocalizedUiAsync()
    {
        try
        {
            Title = await LocalizeAsync("Place details", _languageCode);
            if (BtnNavigate != null) BtnNavigate.Text = await LocalizeAsync("Directions", _languageCode);
            if (BtnAudioQuick != null) BtnAudioQuick.Text = await LocalizeAsync("Audio", _languageCode);
            if (BtnAudioTab != null) BtnAudioTab.Text = "🎵 " + await LocalizeAsync("Audio", _languageCode);
            if (BtnShare != null) BtnShare.Text = await LocalizeAsync("Share", _languageCode);
            if (BtnShowQr != null) BtnShowQr.Text = await LocalizeAsync("QR code", _languageCode);
            if (OverviewTabButton != null) OverviewTabButton.Text = await LocalizeAsync("Overview", _languageCode);
            if (AudioTabButton != null) AudioTabButton.Text = "🎵 " + await LocalizeAsync("Audio", _languageCode);
            if (CommentsTabButton != null) CommentsTabButton.Text = await LocalizeAsync("Comments", _languageCode);
            if (LblCommentsTitle != null) LblCommentsTitle.Text = await LocalizeAsync("Comments", _languageCode);
            if (CommentEntry != null) CommentEntry.Placeholder = await LocalizeAsync("Write a comment...", _languageCode);
            if (BtnSendComment != null) BtnSendComment.Text = await LocalizeAsync("Send", _languageCode);
            if (LblAudioTitle != null) LblAudioTitle.Text = "🎵 " + await LocalizeAsync("Audio files", _languageCode);
            if (SpeakButton != null) SpeakButton.Text = "🔊 " + await LocalizeAsync("Listen audio", _languageCode);
            if (StopAudioButton != null) StopAudioButton.Text = "⏹ " + await LocalizeAsync("Stop audio", _languageCode);
            if (LblAudioHint != null) LblAudioHint.Text = await LocalizeAsync("Tap listen for quick TTS; Audio tab lets you choose available files", _languageCode);
            if (LblQrTitle != null) LblQrTitle.Text = await LocalizeAsync("QR code of this place", _languageCode);
            if (BtnCopyQr != null) BtnCopyQr.Text = "📋 " + await LocalizeAsync("Copy code", _languageCode);
            if (BtnScanThisQr != null) BtnScanThisQr.Text = "🔍 " + await LocalizeAsync("Scan this code", _languageCode);
            if (LblOpenStatus != null)
            {
                var selectedContent = _poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, _languageCode, StringComparison.OrdinalIgnoreCase))
                                   ?? _poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, "en", StringComparison.OrdinalIgnoreCase))
                                   ?? _poi.Contents?.FirstOrDefault(c => string.Equals(c.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
                var statusText = BuildOpenStatusText(selectedContent?.OpeningHours, _languageCode);
                LblOpenStatus.Text = await LocalizeAsync(statusText, _languageCode);
            }
        }
        catch { }
    }

    private async Task<string> LocalizeAsync(string source, string language)
    {
        if (string.IsNullOrWhiteSpace(source)) return string.Empty;
        var lang = NormalizeLanguageCode(language);
        if (lang == "en") return source;

        var cacheKey = $"{lang}:{source}";
        if (_uiTextCache.TryGetValue(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl={Uri.EscapeDataString(lang)}&dt=t&q={Uri.EscapeDataString(source)}";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return source;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return source;

            var segments = doc.RootElement[0];
            if (segments.ValueKind != System.Text.Json.JsonValueKind.Array)
                return source;

            var sb = new System.Text.StringBuilder();
            foreach (var segment in segments.EnumerateArray())
            {
                if (segment.ValueKind != System.Text.Json.JsonValueKind.Array || segment.GetArrayLength() == 0) continue;
                var part = segment[0].GetString();
                if (!string.IsNullOrWhiteSpace(part)) sb.Append(part);
            }

            var translated = sb.ToString().Trim();
            var value = string.IsNullOrWhiteSpace(translated) ? source : translated;
            _uiTextCache[cacheKey] = value;
            return value;
        }
        catch
        {
            return source;
        }
    }

    private static string NormalizeLanguageCode(string? language)
    {
        var normalized = (language ?? "en").Trim().ToLowerInvariant();
        if (normalized.Contains('-')) normalized = normalized.Split('-')[0];
        if (normalized.Contains('_')) normalized = normalized.Split('_')[0];
        if (normalized == "vn") return "vi";
        if (normalized == "eng") return "en";
        return string.IsNullOrWhiteSpace(normalized) ? "en" : normalized;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isAppearingInitialized = false;

        // CÁCH SỬA LỖI ĐỎ: 
        // Gọi Cancel() trên CancellationTokenSource thay vì TextToSpeech
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }

        try { _ = _audioService.StopAsync(); } catch { }

        try
        {
            if (_websiteTapRecognizer != null && WebsiteLabel != null)
            {
                WebsiteLabel.GestureRecognizers.Remove(_websiteTapRecognizer);
            }
        }
        catch { }
    }

    private IAudioService ResolveAudioService()
    {
        try
        {
            var service = Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(IAudioService)) as IAudioService;
            return service ?? new NoOpAudioService();
        }
        catch
        {
            return new NoOpAudioService();
        }
    }

    private static string ResolveApiAuthority()
    {
        try
        {
            if (DeviceInfo.Platform == DevicePlatform.Android && DeviceInfo.DeviceType == DeviceType.Virtual)
            {
                return "http://10.0.2.2:5291";
            }

            var preferred = Preferences.Default.Get("ApiBaseUrl", string.Empty);
            if (string.IsNullOrWhiteSpace(preferred))
            {
                preferred = Preferences.Default.Get("VinhKhanh_ApiBaseUrl", string.Empty);
            }

            if (!string.IsNullOrWhiteSpace(preferred) && Uri.TryCreate(preferred, UriKind.Absolute, out var u))
            {
                return u.GetLeftPart(UriPartial.Authority);
            }
        }
        catch { }

        return "http://localhost:5291";
    }

    private string ResolveAbsoluteApiUrl(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return rawUrl;
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        var authority = string.IsNullOrWhiteSpace(_activeApiAuthority) ? ResolveApiAuthority() : _activeApiAuthority;
        return $"{authority}/{rawUrl.TrimStart('/')}";
    }

    private async Task TrackAnalyticsAsync(string eventName, string trigger, string? audioName = null, string? audioUrl = null, string? languageCode = null)
    {
        try
        {
            if (_poi == null || _poi.Id <= 0 || string.IsNullOrWhiteSpace(eventName)) return;

            var authority = string.IsNullOrWhiteSpace(_activeApiAuthority) ? ResolveApiAuthority() : _activeApiAuthority;
            var trace = new TraceLog
            {
                PoiId = _poi.Id,
                DeviceId = BuildDeviceAnalyticsId(),
                Latitude = _poi.Latitude,
                Longitude = _poi.Longitude,
                TimestampUtc = DateTime.UtcNow,
                ExtraJson = BuildAnalyticsExtraJson(eventName, trigger, audioName, audioUrl, languageCode)
            };

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            if (_apiService != null)
            {
                await _apiService.PostTraceAsync(trace);
            }
            else
            {
                await client.PostAsJsonAsync($"{authority}/api/analytics", trace);
            }
        }
        catch { }
    }

    private string BuildAnalyticsExtraJson(string eventName, string trigger, string? audioName, string? audioUrl, string? languageCode)
    {
        var lang = NormalizeLanguageCode(languageCode ?? _languageCode);
        var escapedName = (audioName ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        var escapedUrl = (audioUrl ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

        return $"{{\"event\":\"{eventName}\",\"source\":\"mobile_app\",\"trigger\":\"{trigger}\",\"lang\":\"{lang}\",\"audioName\":\"{escapedName}\",\"audioUrl\":\"{escapedUrl}\"}}";
    }

    private static string BuildDeviceAnalyticsId()
    {
        try
        {
            var platform = DeviceInfo.Platform.ToString();
            var model = DeviceInfo.Model?.Trim();
            var manufacturer = DeviceInfo.Manufacturer?.Trim();
            var version = DeviceInfo.VersionString?.Trim();
            return $"{platform}|{manufacturer}|{model}|{version}";
        }
        catch
        {
            return Environment.MachineName;
        }
    }
}