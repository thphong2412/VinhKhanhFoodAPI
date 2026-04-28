using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using VinhKhanh.Services;
using VinhKhanh.Shared;

namespace VinhKhanh.Pages
{
    public partial class MapPage
    {
        private static readonly HashSet<string> ShortcutLanguageCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            "vi", "en", "ja", "ko", "ru", "fr", "th", "zh", "es"
        };

        private bool IsShortcutLanguage(string? language)
        {
            var normalized = (language ?? string.Empty).Trim().ToLowerInvariant();
            return !string.IsNullOrWhiteSpace(normalized) && ShortcutLanguageCodes.Contains(normalized);
        }

        private async Task RefreshPoiReviewsAsync(int poiId, string language)
        {
            try
            {
                var reviews = await _apiService.GetPoiReviewsAsync(poiId) ?? new List<PoiReviewModel>();
                var normalized = NormalizeLanguageCode(language);

                var filtered = reviews
                    .Where(r => r != null)
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .ToList();

                var avg = filtered.Any() ? filtered.Average(r => r.Rating) : 0;
                if (LblReviewsSummary != null)
                {
                    LblReviewsSummary.Text = filtered.Any()
                        ? $"{avg:0.0}★ · {filtered.Count} đánh giá"
                        : "Chưa có đánh giá";
                }

                if (LblReviewHint != null)
                {
                    LblReviewHint.Text = filtered.Any() ? "Hãy chia sẻ cảm nhận của bạn" : "Hãy là người đầu tiên đánh giá";
                }

                var viewModels = filtered
                    .Select(r => new
                    {
                        RatingText = new string('★', Math.Clamp(r.Rating, 1, 5)) + new string('☆', Math.Max(0, 5 - Math.Clamp(r.Rating, 1, 5))),
                        Comment = string.IsNullOrWhiteSpace(r.Comment) ? "(Không có nhận xét)" : r.Comment,
                        CreatedAtText = r.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                    })
                    .ToList();

                if (CvReviews != null)
                {
                    CvReviews.ItemsSource = viewModels;
                }

                _selectedReviewRating = 0;
                UpdateStarButtons(0);
                if (ReviewCommentEditor != null)
                {
                    ReviewCommentEditor.Text = string.Empty;
                }
            }
            catch { }
        }

        private void UpdateStarButtons(int rating)
        {
            try
            {
                var stars = new[] { BtnStar1, BtnStar2, BtnStar3, BtnStar4, BtnStar5 };
                for (var i = 0; i < stars.Length; i++)
                {
                    if (stars[i] == null) continue;
                    stars[i].Text = i < rating ? "★" : "☆";
                    stars[i].TextColor = i < rating ? Microsoft.Maui.Graphics.Color.FromArgb("#F9A825") : Microsoft.Maui.Graphics.Color.FromArgb("#9AA0A6");
                }
            }
            catch { }
        }

        private void OnStarRatingClicked(object sender, EventArgs e)
        {
            try
            {
                if (sender == BtnStar1) _selectedReviewRating = 1;
                else if (sender == BtnStar2) _selectedReviewRating = 2;
                else if (sender == BtnStar3) _selectedReviewRating = 3;
                else if (sender == BtnStar4) _selectedReviewRating = 4;
                else if (sender == BtnStar5) _selectedReviewRating = 5;

                UpdateStarButtons(_selectedReviewRating);
            }
            catch { }
        }

        private async void OnSubmitReviewClicked(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPoi == null || _selectedPoi.Id <= 0) return;
                if (_selectedReviewRating <= 0)
                {
                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["notification"], "Vui lòng chọn số sao.", t["ok"]);
                    return;
                }

                var comment = ReviewCommentEditor?.Text?.Trim() ?? string.Empty;
                var review = new PoiReviewModel
                {
                    PoiId = _selectedPoi.Id,
                    Rating = _selectedReviewRating,
                    Comment = comment,
                    LanguageCode = NormalizeLanguageCode(_currentLanguage),
                    DeviceId = BuildDeviceAnalyticsId()
                };

                var created = await _apiService.PostPoiReviewAsync(review);
                if (created == null)
                {
                    var t2 = await GetDialogTextsAsync();
                    await DisplayAlert(t2["error"], "Không thể lưu đánh giá.", t2["ok"]);
                    return;
                }

                await RefreshPoiReviewsAsync(_selectedPoi.Id, _currentLanguage);
            }
            catch { }
        }

        // Handle geofence engine triggers
        private async void OnPoiTriggered(object sender, PoiTriggeredEventArgs e)
        {
            try
            {
                if (e?.Poi == null) return;

                var now = DateTime.UtcNow;
                if ((now - _lastNotificationOpenUtc).TotalSeconds < 3 && _selectedPoi?.Id == e.Poi.Id)
                {
                    return;
                }

                _ = TrackPoiEventAsync("poi_enter", e.Poi.Id, $"\"trigger\":\"geofence\",\"distance\":{Math.Round(e.DistanceMeters, 2).ToString(System.Globalization.CultureInfo.InvariantCulture)},\"lang\":\"{NormalizeLanguageCode(_currentLanguage)}\"");

                if (_pendingNavigationPoiId > 0 && _pendingNavigationPoiId == e.Poi.Id)
                {
                    _pendingNavigationPoiId = 0;
                    _ = TrackPoiEventAsync("navigation_arrived", e.Poi.Id, $"\"trigger\":\"geofence_arrive\",\"lang\":\"{NormalizeLanguageCode(_currentLanguage)}\"");
                }

                if (PoiDetailPanel != null && PoiDetailPanel.IsVisible && _selectedPoi != null && _selectedPoi.Id == e.Poi.Id)
                {
                    var c2 = await GetContentForLanguageAsync(e.Poi.Id, _currentLanguage);
                    if (c2 != null) await PlayNarration(c2.Description);
                    return;
                }

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    _selectedPoi = e.Poi;
                    await ShowPoiDetail(e.Poi);
                    var content = await GetContentForLanguageAsync(e.Poi.Id, _currentLanguage);
                    if (content != null)
                    {
                        await PlayNarration(content.Description);
                    }
                });
            }
            catch { }
        }

        private async void OnStartNarrationClicked(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPoi == null) return;
                var content = await GetContentForLanguageAsync(_selectedPoi.Id, _currentLanguage);
                if (content == null || string.IsNullOrWhiteSpace(content.Description)) return;
                await PlayNarration(content.Description);

                // Hiện popup mini player cho TTS (Audio button = TTS narration)
                var titleTts = string.IsNullOrWhiteSpace(_selectedPoi?.Name) ? "Nghe ngay" : _selectedPoi.Name;
                await ShowMiniPlayerAsync(titleTts, isTts: true);
            }
            catch { }
        }

        private async void OnPlayPoiAudioClicked(object sender, EventArgs e)
        {
            try
            {
                await ShowAudioListForCurrentLanguageAsync();
            }
            catch { }
        }

        private async Task ShowAudioListForCurrentLanguageAsync()
        {
            try
            {
                if (_selectedPoi == null) return;

                var preferredLang = NormalizeLanguageCode(_currentLanguage);
                var audios = await _apiService.GetAudiosByPoiIdAsync(_selectedPoi.Id) ?? new List<AudioModel>();
                await TrackPoiEventAsync("listen_start", _selectedPoi.Id, $"\"trigger\":\"audio_tab\",\"lang\":\"{preferredLang}\"");

                var uploadedByLang = SelectAudioListByLanguage(audios, preferredLang, isTts: false);

                if (!uploadedByLang.Any())
                {
                    var ttsByLang = SelectAudioListByLanguage(audios, preferredLang, isTts: true);

                    if (ttsByLang.Any())
                    {
                        var selectedTts = ttsByLang.First();
                        var ttsUrl = ToAbsoluteApiUrl(selectedTts.Url);
                        if (!string.IsNullOrWhiteSpace(ttsUrl))
                        {
                            var ttsItem = new AudioItem
                            {
                                Key = $"tts-fallback:{_selectedPoi.Id}:{preferredLang}:{selectedTts.Id}",
                                IsTts = false,
                                FilePath = ttsUrl,
                                Language = NormalizeLanguageCode(selectedTts.LanguageCode),
                                PoiId = _selectedPoi.Id,
                                Priority = _selectedPoi?.Priority ?? 0
                            };
                            _audioQueue.Enqueue(ttsItem);
                            await TrackPoiEventAsync("tts_play", _selectedPoi.Id, $"\"mode\":\"tts_fallback\",\"trigger\":\"audio_tab\",\"lang\":\"{NormalizeLanguageCode(selectedTts.LanguageCode)}\"");

                            // TTS fallback chạy qua AudioQueue → IAudioService nên có duration thật
                            var titleFallback = string.IsNullOrWhiteSpace(_selectedPoi?.Name) ? "Audio" : _selectedPoi.Name;
                            await ShowMiniPlayerAsync(titleFallback, isTts: false);
                            return;
                        }
                    }

                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["audio"], t["no_audio_for_lang"], t["ok"]);
                    return;
                }

                var selected = uploadedByLang.FirstOrDefault();
                if (uploadedByLang.Count > 1)
                {
                    var t2 = await GetDialogTextsAsync();
                    var options = uploadedByLang
                        .Select(a => BuildAudioDisplayName(a))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(10)
                        .ToArray();
                    var picked = await DisplayActionSheet("Chọn file audio", t2["cancel"], null, options);
                    if (string.IsNullOrWhiteSpace(picked) || picked == t2["cancel"]) return;
                    selected = uploadedByLang.FirstOrDefault(a => string.Equals(BuildAudioDisplayName(a), picked, StringComparison.OrdinalIgnoreCase));
                }

                if (selected == null) return;

                var playUrl = ToAbsoluteApiUrl(selected.Url);
                if (string.IsNullOrWhiteSpace(playUrl))
                {
                    var t3 = await GetDialogTextsAsync();
                    await DisplayAlert(t3["audio"], t3["invalid_audio_file"], t3["ok"]);
                    return;
                }
                var item = new AudioItem
                {
                    Key = $"mp3:{_selectedPoi.Id}:{preferredLang}:{selected.Id}",
                    IsTts = false,
                    FilePath = playUrl,
                    Language = NormalizeLanguageCode(selected.LanguageCode),
                    PoiId = _selectedPoi.Id,
                    Priority = _selectedPoi?.Priority ?? 0
                };
                _audioQueue.Enqueue(item);
                await TrackPoiEventAsync("audio_play", _selectedPoi.Id, $"\"mode\":\"mp3\",\"trigger\":\"audio_tab\",\"lang\":\"{NormalizeLanguageCode(selected.LanguageCode)}\"");

                // Hiện popup mini player cho audio MP3 (Nghe ngay button)
                var titleAudio = string.IsNullOrWhiteSpace(_selectedPoi?.Name) ? "Audio" : _selectedPoi.Name;
                await ShowMiniPlayerAsync(titleAudio, isTts: false);
            }
            catch
            {
                try { var t = await GetDialogTextsAsync(); await DisplayAlert(t["audio"], t["cannot_load_audio_list"], t["ok"]); } catch { }
            }
        }

        private static string BuildAudioDisplayName(AudioModel audio)
        {
            if (audio == null) return "Audio";
            var name = string.Empty;
            try
            {
                if (!string.IsNullOrWhiteSpace(audio.Url))
                {
                    var uri = new Uri(audio.Url, UriKind.RelativeOrAbsolute);
                    name = Path.GetFileName(uri.IsAbsoluteUri ? uri.LocalPath : audio.Url);
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Audio #{audio.Id}";
            }

            var lang = NarrationService.NormalizeLanguageCode(audio.LanguageCode ?? string.Empty);
            return string.IsNullOrWhiteSpace(lang) ? name : $"{name} ({lang})";
        }

        private List<AudioModel> SelectAudioListByLanguage(IEnumerable<AudioModel> source, string preferredLanguage, bool isTts)
        {
            var audios = source?
                .Where(a => a != null && a.IsTts == isTts && !string.IsNullOrWhiteSpace(a.Url))
                .OrderByDescending(a => a.IsProcessed)
                .ThenByDescending(a => a.CreatedAtUtc)
                .ThenByDescending(a => a.Id)
                .ToList() ?? new List<AudioModel>();

            foreach (var lang in GetLanguageFallbackChain(preferredLanguage, includeVi: false))
            {
                var matched = audios
                    .Where(a => NormalizeLanguageCode(a.LanguageCode) == lang)
                    .ToList();
                if (matched.Any()) return matched;
            }

            return audios;
        }

        private async Task<ContentModel?> GetStrictContentForLanguageAsync(int poiId, string language)
        {
            try
            {
                var preferredLang = NormalizeLanguageCode(language);

                foreach (var lang in GetLanguageFallbackChain(preferredLang, includeVi: false))
                {
                    var local = await _dbService.GetContentByPoiIdAsync(poiId, lang);
                    if (local != null && !string.IsNullOrWhiteSpace(local.Description))
                    {
                        return local;
                    }
                }

                var fromApi = await _apiService.GetContentsByPoiIdAsync(poiId) ?? new List<ContentModel>();
                foreach (var lang in GetLanguageFallbackChain(preferredLang, includeVi: false))
                {
                    var matched = fromApi.FirstOrDefault(c => c != null
                                                              && NormalizeLanguageCode(c.LanguageCode) == lang
                                                              && !string.IsNullOrWhiteSpace(c.Description));
                    if (matched != null)
                    {
                        try { await _dbService.SaveContentAsync(matched); } catch { }
                        return matched;
                    }
                }

                var source = await _dbService.GetContentByPoiIdAsync(poiId, "vi")
                             ?? await _dbService.GetContentByPoiIdAsync(poiId, "en");
                var translated = await BuildTranslatedContentAsync(source, poiId, preferredLang);
                if (translated != null && HasMeaningfulContent(translated))
                {
                    try { await _dbService.SaveContentAsync(translated); } catch { }
                    return translated;
                }
            }
            catch { }

            return null;
        }

        private async Task EnsureCustomLanguagePoiArtifactsAsync(PoiModel poi, string language)
        {
            try
            {
                if (poi == null) return;

                var normalized = NormalizeLanguageCode(language);
                if (string.IsNullOrWhiteSpace(normalized) || IsShortcutLanguage(normalized)) return;

                var content = await GetContentForLanguageAsync(poi.Id, normalized)
                              ?? await _dbService.GetContentByPoiIdAsync(poi.Id, "en");
                if (content == null) return;

                try { await _dbService.SaveContentAsync(content); } catch { }

                await GenerateTtsForPoiAsync(poi, content, normalized);
            }
            catch { }
        }

        private async Task<bool> GenerateTtsForPoiAsync(PoiModel poi, ContentModel? content, string language)
        {
            try
            {
                if (poi == null) return false;

                var lang = NormalizeLanguageCode(language);
                var text = content?.Description ?? poi.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text)) return false;

                var filename = $"poi_{poi.Id}_{lang}.wav";
                var outPath = System.IO.Path.Combine(FileSystem.AppDataDirectory, filename);
                var generated = false;

                try
                {
                    if (_audioGenerator != null)
                    {
                        generated = await _audioGenerator.GenerateTtsToFileAsync(text, lang, outPath);
                    }
                }
                catch { generated = false; }

                if (generated)
                {
                    var audio = new AudioModel
                    {
                        PoiId = poi.Id,
                        Url = outPath,
                        LanguageCode = lang,
                        IsTts = true,
                        IsProcessed = true
                    };
                    try { await _dbService.SaveAudioAsync(audio); } catch { }

                    var item = new AudioItem
                    {
                        IsTts = true,
                        FilePath = outPath,
                        Language = lang,
                        PoiId = poi.Id,
                        Priority = 5
                    };
                    _audioQueue.Enqueue(item);
                    return true;
                }

                var fallbackItem = new AudioItem
                {
                    IsTts = true,
                    Language = lang,
                    Text = text,
                    PoiId = poi.Id,
                    Priority = 5
                };
                _audioQueue.Enqueue(fallbackItem);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable<string> GetLanguageFallbackChain(string language, bool includeVi)
        {
            var preferred = NormalizeLanguageCode(language);

            if (string.IsNullOrWhiteSpace(preferred))
            {
                preferred = "en";
            }

            var ordered = new List<string>();

            void AddIfNeeded(string lang)
            {
                if (string.IsNullOrWhiteSpace(lang)) return;
                if (ordered.Any(x => string.Equals(x, lang, StringComparison.OrdinalIgnoreCase))) return;
                ordered.Add(lang);
            }

            AddIfNeeded(preferred);
            if (!string.Equals(preferred, "en", StringComparison.OrdinalIgnoreCase)) AddIfNeeded("en");
            if (includeVi && !string.Equals(preferred, "vi", StringComparison.OrdinalIgnoreCase)) AddIfNeeded("vi");

            return ordered;
        }

        // Map page QR tap handler (opens QR modal centered)
        private async void OnShowQrClicked_Map(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPoi == null)
                {
                    await TryRestoreSelectedPoiFromUiAsync();
                }

                if (_selectedPoi == null)
                {
                    var dialogText = await GetDialogTextsAsync();
                    await DisplayAlert(dialogText["error"], dialogText["no_selected_poi_qr"], dialogText["close"]);
                    return;
                }

                // Always refresh selected POI from API so QR payload stays synced with Admin source-of-truth.
                await HydratePoiDetailsFromApiAsync(_selectedPoi);

                var payload = _selectedPoi.QrCode?.Trim();
                if (string.IsNullOrWhiteSpace(payload))
                {
                    // Fallback only when admin has no payload yet.
                    payload = ToAbsoluteApiUrl($"/qr/{_selectedPoi.Id}?lang=vi");
                    _selectedPoi.QrCode = payload;
                    try { await _dbService.SavePoiAsync(_selectedPoi); } catch { }
                }
                else if (payload.StartsWith("/", StringComparison.Ordinal))
                {
                    payload = ToAbsoluteApiUrl(payload);
                }

                // create modal page with QR image and X close in corner
                var qrSrc = await new MapPageHelpers().GenerateQrImageSourceAsync(payload);
                var overlay = new Grid { BackgroundColor = Microsoft.Maui.Graphics.Colors.Black.WithAlpha(0.6f) };

                var box = new Frame { BackgroundColor = Microsoft.Maui.Graphics.Colors.White, CornerRadius = 16, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Padding = 16 };
                var img = new Image { Source = qrSrc, WidthRequest = 300, HeightRequest = 300, Aspect = Aspect.AspectFit };
                var closeX = new Button { Text = "✕", BackgroundColor = Microsoft.Maui.Graphics.Colors.Transparent, TextColor = Microsoft.Maui.Graphics.Colors.Black, FontSize = 20, WidthRequest = 44, HeightRequest = 44, CornerRadius = 22, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start };
                closeX.Clicked += async (s, ev) => await Navigation.PopModalAsync();

                var closeText = await GetDialogTextsAsync();
                var closeBtn = new Button { Text = closeText["close"], BackgroundColor = Microsoft.Maui.Graphics.Colors.Black, TextColor = Microsoft.Maui.Graphics.Colors.White, CornerRadius = 10, HeightRequest = 44 };
                closeBtn.Clicked += async (s, ev) => await Navigation.PopModalAsync();

                box.Content = new StackLayout { Spacing = 12, Children = { img, closeBtn } };
                overlay.Children.Add(box);
                overlay.Children.Add(closeX);

                var page = new ContentPage { Content = overlay, BackgroundColor = Microsoft.Maui.Graphics.Colors.Transparent };
                await Navigation.PushModalAsync(page);
            }
            catch { }
        }

        // Carousel navigation buttons
        private void OnImgPrevClicked(object sender, EventArgs e)
        {
            try
            {
                if (ImgCarousel == null) return;
                var pos = ImgCarousel.Position;
                if (pos > 0) ImgCarousel.Position = pos - 1;
            }
            catch { }
        }

        private void OnImgNextClicked(object sender, EventArgs e)
        {
            try
            {
                if (ImgCarousel == null) return;
                var pos = ImgCarousel.Position;
                var count = (ImgCarousel.ItemsSource as System.Collections.ICollection)?.Count ?? 0;
                if (pos < count - 1) ImgCarousel.Position = pos + 1;
            }
            catch { }
        }

        private async void OnSelectAudioClicked(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPoi == null) return;
                var result = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Chọn file audio" });
                if (result == null) return;

                var fileName = result.FileName;
                var dest = System.IO.Path.Combine(FileSystem.AppDataDirectory, fileName);
                using (var src = await result.OpenReadAsync())
                using (var dst = System.IO.File.Create(dest))
                {
                    await src.CopyToAsync(dst);
                }

                var audio = new VinhKhanh.Shared.AudioModel
                {
                    PoiId = _selectedPoi.Id,
                    Url = dest,
                    LanguageCode = _currentLanguage,
                    IsTts = false,
                    IsProcessed = true
                };

                try { await _dbService.SaveAudioAsync(audio); } catch { }

                var content = await _dbService.GetContentByPoiIdAsync(_selectedPoi.Id, _currentLanguage);
                if (content != null)
                {
                    content.AudioUrl = dest;
                    await _dbService.SaveContentAsync(content);
                }

                await DisplayAlert("OK", "File audio đã được lưu và gắn vào điểm này.", "Đóng");
            }
            catch (Exception ex)
            {
                AddLog($"Select audio failed: {ex.Message}");
                try { await DisplayAlert("Lỗi", "Không thể lưu file audio.", "Đóng"); } catch { }
            }
        }

        private async void OnGenerateTtsClicked(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPoi == null) return;
                var lang = NormalizeLanguageCode(_currentLanguage);
                var content = await GetContentForLanguageAsync(_selectedPoi.Id, lang) ?? await _dbService.GetContentByPoiIdAsync(_selectedPoi.Id, "en");
                if (await GenerateTtsForPoiAsync(_selectedPoi, content, lang))
                {
                    await DisplayAlert("TTS", "Đã tạo TTS tạm thời cho ngôn ngữ đã chọn và đưa vào hàng đợi phát.", "OK");
                    return;
                }

                var text = content?.Description ?? _selectedPoi.Name ?? string.Empty;
                if (!string.IsNullOrEmpty(text))
                {
                    _audioQueue.Enqueue(new VinhKhanh.Services.AudioItem
                    {
                        IsTts = true,
                        Language = lang,
                        Text = text,
                        PoiId = _selectedPoi.Id,
                        Priority = 5
                    });
                }
            }
            catch (Exception ex)
            {
                AddLog($"Generate TTS failed: {ex.Message}");
            }
        }

        private async void OnShowQrClicked(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPoi == null) return;
                if (string.IsNullOrEmpty(_selectedPoi.QrCode))
                {
                    _selectedPoi.QrCode = $"POI:{_selectedPoi.Id}";
                    try { await _dbService.SavePoiAsync(_selectedPoi); } catch { }
                }

                var payload = _selectedPoi.QrCode;
                if (!string.IsNullOrWhiteSpace(payload)
                    && (payload.StartsWith("http", StringComparison.OrdinalIgnoreCase) || payload.StartsWith("/")))
                {
                    payload = ToAbsoluteApiUrl(payload);
                }
                var t = await GetDialogTextsAsync();
                var action = await DisplayActionSheet(t["qr_for_this_poi"], t["close"], null, t["copy_payload"], t["share_payload"], t["open_scan_sim"]);
                if (string.Equals(action, t["copy_payload"], StringComparison.OrdinalIgnoreCase))
                {
                    try { await Clipboard.Default.SetTextAsync(payload); await DisplayAlert(t["ok"], t["payload_copied"], t["close"]); } catch { }
                }
                else if (string.Equals(action, t["share_payload"], StringComparison.OrdinalIgnoreCase))
                {
                    try { await Share.RequestAsync(new ShareTextRequest { Text = payload, Title = "QR payload" }); } catch { }
                }
                else if (string.Equals(action, t["open_scan_sim"], StringComparison.OrdinalIgnoreCase))
                {
                    try { await Navigation.PushAsync(new ScanPage(_currentLanguage, _selectedPoi.Id, _dbService, _audioQueue, _narrationService, _audioGenerator)); } catch { }
                }
            }
            catch (Exception ex)
            {
                AddLog($"Show QR failed: {ex.Message}");
            }
        }

        private async void OnStartTrackingClicked(object sender, EventArgs e)
        {
            try
            {
                BtnStartTracking.IsEnabled = false;
                AddLog("Requesting permissions...");
                var ok = await _permissionService.EnsureLocationPermissionsAsync();
                if (!ok)
                {
                    AddLog("Permissions denied");
                    LblTrackingStatus.Text = "Status: permission denied";
                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["permission_denied_title"], t["permission_denied_msg"], t["ok"]);
                    BtnStartTracking.IsEnabled = true;
                    return;
                }

                AddLog("Starting tracking service");
                var bgOk = await _permissionService.IsBackgroundLocationGrantedAsync();
                if (!bgOk)
                {
                    var t = await GetDialogTextsAsync();
                    var go = await DisplayAlert(t["background_permission_title"], t["background_permission_msg"], t["open_settings"], t["continue_without"]);
                    if (go)
                    {
                        try
                        {
                            AppInfo.ShowSettingsUI();
                        }
                        catch { }
                        BtnStartTracking.IsEnabled = true;
                        return;
                    }
                }

                await _locationPollingService.StartAsync();
                AddLog("Tracking service start requested");
                _isTrackingActive = true;
                RefreshGeofencePoisFromCurrentState();
                LblTrackingStatus.Text = await GetTrackingStatusTextAsync("tracking");
                BtnStartTracking.IsEnabled = false;
                BtnStopTracking.IsEnabled = true;
            }
            catch (Exception ex)
            {
                AddLog($"Start failed: {ex.Message}");
                BtnStartTracking.IsEnabled = true;
            }
        }

        private async void OnStopTrackingClicked(object sender, EventArgs e)
        {
            try
            {
                AddLog("Stopping tracking service");
                await _locationPollingService.StopAsync();
                _isTrackingActive = false;
                LblTrackingStatus.Text = await GetTrackingStatusTextAsync("stopped");
            }
            catch (Exception ex)
            {
                AddLog($"Stop failed: {ex.Message}");
            }
        }

        private async void OnGetDirectionsClicked(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPoi == null)
                {
                    await TryRestoreSelectedPoiFromUiAsync();
                }

                if (_selectedPoi == null)
                {
                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["error"], t["no_selected_poi_directions"], t["ok"]);
                    return;
                }

                var lat = _selectedPoi.Latitude;
                var lng = _selectedPoi.Longitude;
                var label = Uri.EscapeDataString(_selectedPoi.Name ?? "Destination");
                _pendingNavigationPoiId = _selectedPoi.Id;
                Location? currentLocation = null;
                try
                {
                    currentLocation = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)));
                }
                catch { }

                if (currentLocation == null)
                {
                    currentLocation = _lastLocation;
                }

                var originLat = currentLocation?.Latitude;
                var originLng = currentLocation?.Longitude;

                string uri = null;
                try
                {
                    if (DeviceInfo.Platform == DevicePlatform.iOS)
                    {
                        uri = originLat.HasValue && originLng.HasValue
                            ? $"http://maps.apple.com/?saddr={originLat.Value},{originLng.Value}&daddr={lat},{lng}"
                            : $"http://maps.apple.com/?daddr={lat},{lng}";
                    }
                    else if (DeviceInfo.Platform == DevicePlatform.Android)
                    {
                        uri = originLat.HasValue && originLng.HasValue
                            ? $"https://www.google.com/maps/dir/?api=1&origin={originLat.Value},{originLng.Value}&destination={lat},{lng}&travelmode=driving"
                            : $"geo:{lat},{lng}?q={label}";
                    }
                    else
                    {
                        uri = originLat.HasValue && originLng.HasValue
                            ? $"https://www.google.com/maps/dir/?api=1&origin={originLat.Value},{originLng.Value}&destination={lat},{lng}&travelmode=driving"
                            : $"https://www.google.com/maps/dir/?api=1&destination={lat},{lng}";
                    }

                    AddLog($"Điều hướng: mở bản đồ tới {_selectedPoi.Name}");
                    await Launcher.OpenAsync(new Uri(uri));
                    _ = TrackPoiEventAsync("navigation_start", _selectedPoi.Id, $"\"trigger\":\"map_directions\",\"lang\":\"{NormalizeLanguageCode(_currentLanguage)}\"");

                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["directions"], string.Format(t["opening_directions_to"], _selectedPoi.Name), t["ok"]);
                }
                catch (Exception ex)
                {
                    try
                    {
                        var web = originLat.HasValue && originLng.HasValue
                            ? $"https://www.google.com/maps/dir/?api=1&origin={originLat.Value},{originLng.Value}&destination={lat},{lng}&travelmode=driving"
                            : $"https://www.google.com/maps/dir/?api=1&destination={lat},{lng}";
                        AddLog($"Điều hướng fallback: {_selectedPoi.Name} - {ex.Message}");
                        await Launcher.OpenAsync(new Uri(web));
                        var t = await GetDialogTextsAsync();
                        await DisplayAlert(t["directions"], t["opened_web_directions"], t["ok"]);
                    }
                    catch
                    {
                        var t = await GetDialogTextsAsync();
                        await DisplayAlert(t["directions"], t["cannot_open_directions"], t["ok"]);
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"OnGetDirectionsClicked lỗi: {ex.Message}");
            }
        }

        private async void OnWebsiteTapped(object sender, EventArgs e)
        {
            try
            {
                var url = LblWebsite?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(url)) return;

                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }

                await Launcher.OpenAsync(new Uri(url));
            }
            catch { }
        }

        private async void OnPhoneTapped(object sender, EventArgs e)
        {
            try
            {
                var phone = LblPhone?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(phone)) return;
                await Launcher.OpenAsync(new Uri($"tel:{phone}"));
            }
            catch { }
        }

        private async void OnShareClicked(object sender, EventArgs e)
        {
            if (_selectedPoi == null) return;
            var content = await _dbService.GetContentByPoiIdAsync(_selectedPoi.Id, _currentLanguage);
            var shareText = content?.ShareUrl ?? content?.Description ?? _selectedPoi.Name;
            try
            {
                await Share.RequestAsync(new ShareTextRequest
                {
                    Title = content?.Title ?? _selectedPoi.Name,
                    Text = shareText,
                    Uri = content?.ShareUrl
                });
            }
            catch { }
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            var target = _selectedPoi;
            if (target == null) return;
            try
            {
                // Toggle: ấn Lưu → Đã lưu, ấn Đã lưu → Lưu (huỷ)
                var newSavedState = !target.IsSaved;
                target.IsSaved = newSavedState;
                await _dbService.SavePoiAsync(target);

                try
                {
                    _pois = await _dbService.GetPoisAsync() ?? new List<PoiModel>();
                    if (BtnShowSaved != null)
                        BtnShowSaved.IsVisible = _pois.Any(p => p.IsSaved);
                }
                catch { }

                // Cập nhật nhãn nút Lưu/Đã lưu trực tiếp — KHÔNG re-render toàn bộ
                // detail panel để tránh flicker và tránh _selectedPoi bị reset null.
                UpdateSaveActionLabel(newSavedState);
            }
            catch { }
        }

        private async void OnShowSavedClicked(object sender, EventArgs e)
        {
            try
            {
                await ShowSavedPoisInHighlightsAsync();
            }
            catch { }
        }

        private async Task ShowSavedPoisInHighlightsAsync()
        {
            try
            {
                _pois = await _dbService.GetPoisAsync();
                var saved = (_pois ?? new List<PoiModel>())
                    .Where(p => p != null && p.IsSaved)
                    .OrderByDescending(p => p.Priority)
                    .ThenBy(p => p.Name)
                    .ToList();

                BtnShowSaved.IsVisible = saved.Any();
                if (!saved.Any())
                {
                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["notification"], t["no_saved_poi"], t["ok"]);
                    return;
                }

                if (LblHighlightsTitle != null)
                {
                    LblHighlightsTitle.Text = "Địa điểm đã lưu";
                }

                if (BtnBackToHighlights != null)
                {
                    BtnBackToHighlights.IsVisible = true;
                }

                await RenderHighlightsAsync(saved);
                SetHighlightsExpandedState(true);

                if (PoiDetailPanel != null)
                {
                    PoiDetailPanel.IsVisible = false;
                }

                if (HighlightsPanel != null)
                {
                    HighlightsPanel.IsVisible = true;
                }
            }
            catch { }
        }

        private async void OnBackToHighlightsClicked(object sender, EventArgs e)
        {
            try
            {
                await ShowHighlightsDefaultAsync();
            }
            catch { }
        }

        private async Task ShowHighlightsDefaultAsync()
        {
            try
            {
                if (LblHighlightsTitle != null)
                {
                    LblHighlightsTitle.Text = "Nổi bật trong khu vực";
                }

                if (BtnBackToHighlights != null)
                {
                    BtnBackToHighlights.IsVisible = false;
                }

                var top = (_pois ?? new List<PoiModel>())
                    .Where(p => p != null)
                    .DistinctBy(p => p.Id)
                    .OrderByDescending(p => p.Priority)
                    .Take(12)
                    .ToList();

                await RenderHighlightsAsync(top);
                SetHighlightsExpandedState(true);
            }
            catch { }
        }

        private void UpdateSaveActionLabel(bool isSaved)
        {
            try
            {
                if (LblActSave != null)
                {
                    LblActSave.Text = isSaved ? "Đã lưu" : "Lưu";
                }

                if (FrameSave != null)
                {
                    FrameSave.BackgroundColor = isSaved
                        ? Microsoft.Maui.Graphics.Color.FromArgb("#BDBDBD")
                        : Microsoft.Maui.Graphics.Color.FromArgb("#E0F2F1");
                }
            }
            catch { }
        }

        private async void OnScanCameraClicked(object sender, EventArgs e) => await Navigation.PushAsync(new ScanPage());

        private async void OnSearchPoiTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _searchDebounceCts?.Cancel();
                _searchDebounceCts?.Dispose();
                _searchDebounceCts = new CancellationTokenSource();
                var token = _searchDebounceCts.Token;

                var keyword = (e.NewTextValue ?? string.Empty).Trim();
                _lastSearchKeyword = keyword;

                try
                {
                    await Task.Delay(280, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested) return;

                if (string.IsNullOrWhiteSpace(keyword))
                {
                    var defaultHighlights = _pois.OrderByDescending(p => p.Priority).Take(6).ToList();
                    await RenderHighlightsAsync(defaultHighlights);
                    return;
                }

                var results = await SearchPoisAsync(keyword);
                await RenderHighlightsAsync(results.Take(10));
            }
            catch { }
        }

        private async void OnSearchPoiSearchButtonPressed(object sender, EventArgs e)
        {
            try
            {
                var searchBar = this.FindByName<SearchBar>("SearchPoiBar");
                var keyword = searchBar?.Text?.Trim() ?? _lastSearchKeyword;
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    return;
                }

                var results = await SearchPoisAsync(keyword);
                if (!results.Any())
                {
                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["search"], t["search_not_found"], t["ok"]);
                    return;
                }

                if (results.Count == 1)
                {
                    _selectedPoi = results[0];
                    await ShowPoiDetail(results[0], true);
                    return;
                }

                var options = results.Take(8).Select(p => $"#{p.Id} - {p.Name}").ToArray();
                var t2 = await GetDialogTextsAsync();
                var picked = await DisplayActionSheet(t2["choose_poi"], t2["cancel"], null, options);
                if (string.IsNullOrWhiteSpace(picked) || picked == t2["cancel"]) return;

                var selected = results.FirstOrDefault(p => string.Equals($"#{p.Id} - {p.Name}", picked, StringComparison.Ordinal));
                if (selected == null) return;

                _selectedPoi = selected;
                await ShowPoiDetail(selected, true);
            }
            catch { }
        }

        private async Task<List<PoiModel>> SearchPoisAsync(string keyword)
        {
            var results = new List<PoiModel>();
            if (string.IsNullOrWhiteSpace(keyword) || _pois == null || !_pois.Any()) return results;

            var query = keyword.Trim();
            foreach (var poi in _pois)
            {
                if (ContainsIgnoreCase(poi.Name, query))
                {
                    results.Add(poi);
                    continue;
                }

                try
                {
                    var localized = await _dbService.GetContentByPoiIdAsync(poi.Id, _currentLanguage);
                    var en = await _dbService.GetContentByPoiIdAsync(poi.Id, "en");

                    if (ContainsIgnoreCase(localized?.Title, query) || ContainsIgnoreCase(en?.Title, query))
                    {
                        results.Add(poi);
                    }
                }
                catch { }
            }

            return results
                .DistinctBy(p => p.Id)
                .OrderByDescending(p => p.Priority)
                .ThenBy(p => p.Name)
                .ToList();
        }

        private static bool ContainsIgnoreCase(string? source, string keyword)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(keyword)) return false;
            return source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
        }

        private async void OnQrClicked(object sender, EventArgs e)
        {
            if (_selectedPoi == null) return;
            try
            {
                await Navigation.PushAsync(new ScanPage(_currentLanguage, _selectedPoi.Id));
            }
            catch { }
        }

        private Task PlayNarration(string text, int priority = -1)
        {
            try
            {
                var effectivePriority = priority >= 0 ? priority : (_selectedPoi?.Priority ?? 0);
                var normalizedLang = NarrationService.NormalizeLanguageCode(_currentLanguage);
                var key = _selectedPoi != null ? $"poi:{_selectedPoi.Id}:{normalizedLang}" : (text?.GetHashCode().ToString() ?? Guid.NewGuid().ToString());
                var item = new AudioItem
                {
                    Key = key,
                    IsTts = true,
                    Language = normalizedLang,
                    Text = text,
                    PoiId = _selectedPoi?.Id ?? 0,
                    Priority = effectivePriority
                };

                _audioQueue?.Enqueue(item);
                try
                {
                    _ = TrackPoiEventAsync("tts_play", item.PoiId, $"\"mode\":\"queue_tts\",\"lang\":\"{normalizedLang}\"");
                }
                catch { }
            }
            catch { }

            return Task.CompletedTask;
        }

        private async void OnPinClicked(object sender, Microsoft.Maui.Controls.Maps.PinClickedEventArgs e)
        {
            try
            {
                var pin = sender as Microsoft.Maui.Controls.Maps.Pin;
                if (pin == null || pin.Location == null || _pois == null || !_pois.Any()) return;

                var poi = _pois
                    .OrderBy(p => Math.Abs(p.Latitude - pin.Location.Latitude) + Math.Abs(p.Longitude - pin.Location.Longitude))
                    .FirstOrDefault(p => Math.Abs(p.Latitude - pin.Location.Latitude) < 0.0003 && Math.Abs(p.Longitude - pin.Location.Longitude) < 0.0003);

                if (poi != null)
                {
                    e.HideInfoWindow = true;
                    await OpenPoiDetailFromSelectionAsync(poi, "map_pin", userInitiated: true);
                }
            }
            catch (Exception ex)
            {
                AddLog($"OnPinClicked error: {ex.Message}");
            }
        }

        private async Task TryRestoreSelectedPoiFromUiAsync()
        {
            try
            {
                if (_selectedPoi != null) return;
                if (PoiDetailPanel == null || !PoiDetailPanel.IsVisible) return;

                var title = LblPoiName?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(title) || _pois == null || !_pois.Any()) return;

                var normalizedTitle = title.Trim();
                var matched = _pois.FirstOrDefault(p => string.Equals((p.Name ?? string.Empty).Trim(), normalizedTitle, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    _selectedPoi = matched;
                    return;
                }

                foreach (var poi in _pois)
                {
                    try
                    {
                    var content = await _dbService.GetContentByPoiIdAsync(poi.Id, NormalizeLanguageCode(_currentLanguage))
                                     ?? await _dbService.GetContentByPoiIdAsync(poi.Id, "en");
                        if (content == null) continue;

                        var contentTitle = content.Title?.Trim();
                        if (!string.IsNullOrWhiteSpace(contentTitle)
                            && string.Equals(contentTitle, normalizedTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            _selectedPoi = poi;
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private async Task TrackPoiEventAsync(string eventName, int poiId, string? extraFields = null)
        {
            try
            {
                if (_apiService == null || poiId <= 0 || string.IsNullOrWhiteSpace(eventName)) return;

                // debounce nhanh ở client để giảm spam khi GPS rung lắc hoặc người dùng bấm liên tục
                var normalizedEvent = eventName.Trim().ToLowerInvariant();
                var key = $"{poiId}:{normalizedEvent}";
                var now = DateTime.UtcNow;
                var minSeconds = normalizedEvent switch
                {
                    "poi_heartbeat" => 8,
                    "poi_enter" => 6,
                    "tts_play" => 4,
                    "audio_play" => 4,
                    "listen_start" => 3,
                    _ => 2
                };

                if (!_eventTraceGuard.TryAdd(key, now))
                {
                    if (_eventTraceGuard.TryGetValue(key, out var last)
                        && (now - last).TotalSeconds < minSeconds)
                    {
                        return;
                    }

                    _eventTraceGuard[key] = now;
                }

                var extra = string.IsNullOrWhiteSpace(extraFields)
                    ? $"{{\"event\":\"{eventName}\",\"source\":\"mobile_app\"}}"
                    : $"{{\"event\":\"{eventName}\",\"source\":\"mobile_app\",{extraFields}}}";

                var trace = new VinhKhanh.Shared.TraceLog
                {
                    PoiId = poiId,
                    DeviceId = BuildDeviceAnalyticsId(),
                    Latitude = _lastLocation?.Latitude ?? 0,
                    Longitude = _lastLocation?.Longitude ?? 0,
                    ExtraJson = extra,
                    DurationSeconds = null
                };

                await _apiService.PostTraceAsync(trace);
            }
            catch { }
        }
    }
}
