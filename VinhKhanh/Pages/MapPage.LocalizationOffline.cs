using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using VinhKhanh.Services;
using VinhKhanh.Shared;

namespace VinhKhanh.Pages
{
    public partial class MapPage
    {
        // Language selection handlers
        private async void OnSelectVietnameseClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("vi");
        }

        private async void OnSelectEnglishClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("en");
        }

        private async void OnSelectRussianClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("ru");
        }

        private async void OnSelectFrenchClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("fr");
        }

        private async void OnVoiceViClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("vi");
        }

        private async void OnVoiceEnClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("en");
        }

        private async void OnVoiceZhClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("zh");
        }

        private async void OnVoiceJaClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("ja");
        }

        private async void OnVoiceKoClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("ko");
        }

        private async void OnSelectThaiClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("th");
        }

        private async void OnSelectChineseClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("zh");
        }

        private async void OnSelectSpanishClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("es");
        }

        private async void OnSelectJapaneseClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("ja");
        }

        private async void OnSelectKoreanClicked(object sender, EventArgs e)
        {
            await ApplyLanguageSelectionAsync("ko");
        }

        private async Task ApplyLanguageSelectionAsync(string languageCode)
        {
            var normalized = NormalizeLanguageCode(languageCode);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            var supportedLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "vi", "en", "ja", "ko", "ru", "fr", "th", "zh", "es"
            };
            if (!supportedLanguages.Contains(normalized))
            {
                normalized = "en";
            }

            _languageRefreshCts?.Cancel();
            _languageRefreshCts?.Dispose();
            _languageRefreshCts = new CancellationTokenSource();
            var token = _languageRefreshCts.Token;

            _currentLanguage = normalized;
            try { Preferences.Default.Set("selected_language", _currentLanguage); } catch { }
            try { Preferences.Default.Set("IncludeUnpublishedPois", true); } catch { }
            try { await MainThread.InvokeOnMainThreadAsync(UpdateLanguageSelectionUI); } catch { }

            try
            {
                await _uiRefreshLock.WaitAsync(token);
                if (token.IsCancellationRequested) return;
                if (_isPageInitializing) return;

                await UpdateUiStringsAsync();
                if (token.IsCancellationRequested) return;

                try
                {
                    if (_realtimeSyncManager != null)
                    {
                        await RunSingleFullSyncAndApplyUiAsync();
                    }
                }
                catch { }

                if (token.IsCancellationRequested) return;

                var effectiveLanguage = await EnsureLanguageHasDataOrFallbackToEnglishAsync(_currentLanguage);
                if (!string.Equals(effectiveLanguage, _currentLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    _currentLanguage = effectiveLanguage;
                    try { Preferences.Default.Set("selected_language", _currentLanguage); } catch { }
                    await UpdateUiStringsAsync();
                }

                if (token.IsCancellationRequested) return;

                await DisplayAllPois(token);

                if (token.IsCancellationRequested) return;

                try
                {
                    if (_selectedPoi != null && PoiDetailPanel?.IsVisible == true)
                    {
                        await ShowPoiDetail(_selectedPoi);
                    }
                }
                catch { }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                try
                {
                    if (_uiRefreshLock.CurrentCount == 0)
                    {
                        _uiRefreshLock.Release();
                    }
                }
                catch { }
            }
        }

        private async Task<string> EnsureLanguageHasDataOrFallbackToEnglishAsync(string language)
        {
            try
            {
                var normalized = NormalizeLanguageCode(language);
                var supportedLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "vi", "en", "ja", "ko", "ru", "fr", "th", "zh", "es"
                };
                if (!supportedLanguages.Contains(normalized))
                {
                    return "en";
                }
                return normalized;
            }
            catch
            {
                return "en";
            }
        }

        private void UpdateLanguageSelectionUI()
        {
            try
            {
                if (BtnLangVI == null || BtnLangEN == null || BtnLangJA == null || BtnLangKO == null || BtnLangRU == null || BtnLangFR == null || BtnLangTH == null || BtnLangZH == null || BtnLangES == null) return;

                var lang = NormalizeLanguageCode(_currentLanguage);

                BtnLangVI.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangVI.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangVI.FontAttributes = FontAttributes.None;
                BtnLangEN.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangEN.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangEN.FontAttributes = FontAttributes.None;
                BtnLangJA.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangJA.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangJA.FontAttributes = FontAttributes.None;
                BtnLangKO.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangKO.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangKO.FontAttributes = FontAttributes.None;
                BtnLangRU.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangRU.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangRU.FontAttributes = FontAttributes.None;
                BtnLangFR.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangFR.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangFR.FontAttributes = FontAttributes.None;
                BtnLangTH.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangTH.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangTH.FontAttributes = FontAttributes.None;
                BtnLangZH.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangZH.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangZH.FontAttributes = FontAttributes.None;
                BtnLangES.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("Transparent"); BtnLangES.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("Gray"); BtnLangES.FontAttributes = FontAttributes.None;

                switch (lang)
                {
                    case "vi":
                        BtnLangVI.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangVI.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangVI.FontAttributes = FontAttributes.Bold;
                        break;
                    case "en":
                        BtnLangEN.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangEN.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangEN.FontAttributes = FontAttributes.Bold;
                        break;
                    case "ja":
                        BtnLangJA.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangJA.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangJA.FontAttributes = FontAttributes.Bold;
                        break;
                    case "ko":
                        BtnLangKO.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangKO.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangKO.FontAttributes = FontAttributes.Bold;
                        break;
                    case "ru":
                        BtnLangRU.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangRU.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangRU.FontAttributes = FontAttributes.Bold;
                        break;
                    case "fr":
                        BtnLangFR.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangFR.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangFR.FontAttributes = FontAttributes.Bold;
                        break;
                    case "th":
                        BtnLangTH.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangTH.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangTH.FontAttributes = FontAttributes.Bold;
                        break;
                    case "zh":
                        BtnLangZH.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangZH.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangZH.FontAttributes = FontAttributes.Bold;
                        break;
                    case "es":
                        BtnLangES.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A73E8"); BtnLangES.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#FFFFFF"); BtnLangES.FontAttributes = FontAttributes.Bold;
                        break;
                }
            }
            catch { }
        }

        private async void OnConfirmLanguageClicked(object sender, EventArgs e)
        {
            try
            {
                Preferences.Default.Set("lang_seen", true);
                _isLanguageModalOpen = false;
                LanguagePanel.IsVisible = false;

                if (GpsButtonFrame != null)
                    GpsButtonFrame.IsVisible = true;

                if (HighlightsPanel != null && _selectedPoi == null)
                    HighlightsPanel.IsVisible = true;

                if (_isPageInitializing)
                {
                    return;
                }

                _languageRefreshCts?.Cancel();
                _languageRefreshCts?.Dispose();
                _languageRefreshCts = new CancellationTokenSource();
                await DisplayAllPois(_languageRefreshCts.Token);
            }
            catch { }
        }

        private async void OnEnableOfflineMapClicked(object sender, EventArgs e)
        {
            try
            {
                if (_mapOfflinePackService == null)
                {
                    UpdateOfflineMapStatusUi(await GetOfflineMapStatusTextAsync("service_missing"));
                    return;
                }

                if (BtnEnableOfflineMap != null) BtnEnableOfflineMap.IsEnabled = false;
                UpdateOfflineMapStatusUi(await GetOfflineMapStatusTextAsync("downloading_pack"));
                UpdateOfflineMapProgressUi(0, await GetOfflineMapProgressTextAsync(0));

                var progress = new Progress<MapOfflineProgress>(p =>
                {
                    var status = FormatOfflineMapDownloadingStatus(p.Stage, p.DownloadedFiles, p.TotalFiles, p.Percent);
                    UpdateOfflineMapStatusUi(status);
                    UpdateOfflineMapProgressUi(p.Percent / 100d, FormatOfflineMapProgressText(p.Percent, p.DownloadedFiles, p.TotalFiles));
                });

                var result = await _mapOfflinePackService.DownloadPackAsync("q4-v1", progress);
                if (result == null || !result.Success)
                {
                    UpdateOfflineMapStatusUi(await GetOfflineMapStatusTextAsync("download_failed", result?.Error ?? "unknown"));
                    UpdateOfflineMapProgressUi(0, await GetOfflineMapProgressTextAsync(0, failed: true));
                    return;
                }

                _offlineMapEnabled = false;
                _offlineMapLocalEntry = string.Empty;
                try
                {
                    Preferences.Default.Set("offline_map_enabled", false);
                    Preferences.Default.Set("offline_map_local_entry", string.Empty);
                }
                catch { }

                UpdateOfflineMapStatusUi(await GetOfflineMapStatusTextAsync("online"));
                UpdateOfflineMapProgressUi(0, await GetOfflineMapProgressTextAsync(0));
            }
            catch
            {
                UpdateOfflineMapStatusUi(await GetOfflineMapStatusTextAsync("download_failed"));
                UpdateOfflineMapProgressUi(0, await GetOfflineMapProgressTextAsync(0, failed: true));
            }
            finally
            {
                if (BtnEnableOfflineMap != null) BtnEnableOfflineMap.IsEnabled = true;
            }
        }

        private void UpdateOfflineMapStatusUi(string text)
        {
            try
            {
                if (LblOfflineMapStatus != null)
                {
                    LblOfflineMapStatus.Text = text;
                }
            }
            catch { }
        }

        private void UpdateOfflineMapProgressUi(double progress, string text)
        {
            try
            {
                var normalized = Math.Clamp(progress, 0d, 1d);
                if (PbOfflineMapProgress != null)
                {
                    PbOfflineMapProgress.Progress = normalized;
                }

                if (LblOfflineMapProgress != null)
                {
                    LblOfflineMapProgress.Text = text;
                }
            }
            catch { }
        }

        private async Task TrySwitchToOfflineMapAsync()
        {
            try
            {
                if (MapboxOfflineWebView != null)
                {
                    MapboxOfflineWebView.IsVisible = false;
                    MapboxOfflineWebView.InputTransparent = true;
                    MapboxOfflineWebView.Source = null;
                }
                if (vinhKhanhMap != null)
                {
                    vinhKhanhMap.IsVisible = true;
                    vinhKhanhMap.InputTransparent = false;
                }
                return;
            }
            catch { }
        }

        private async Task EnsureMapboxOfflineSourceAsync()
        {
            try
            {
                return;
            }
            catch { }
        }

        private async Task PushPoisToOfflineMapAsync()
        {
            try
            {
                return;
            }
            catch { }
        }

        private async Task UpdateUiStringsAsync()
        {
            try
            {
                var dynamicUi = await BuildDynamicUiTextAsync(_currentLanguage);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    try
                    {
                        UpdateLanguageSelectionUI();

                        if (TabOverview != null && TabIntro != null)
                        {
                            var tabReview = this.FindByName<Button>("TabReview");
                            TabOverview.Text = dynamicUi["tab_overview"];
                            TabIntro.Text = dynamicUi["tab_intro"];
                            if (tabReview != null) tabReview.Text = dynamicUi["tab_review"];
                        }

                        var btnToggle = this.FindByName<Button>("BtnToggleDescription");
                        if (btnToggle != null)
                        {
                            btnToggle.Text = dynamicUi["read_more"];
                        }

                        var lbDir = this.FindByName<Label>("LblActDirections"); if (lbDir != null) lbDir.Text = dynamicUi["act_directions"];
                        var lbAudio = this.FindByName<Label>("LblActAudio"); if (lbAudio != null) lbAudio.Text = dynamicUi["act_listen_now"];
                        var lbNarr = this.FindByName<Label>("LblActNarration"); if (lbNarr != null) lbNarr.Text = dynamicUi["act_audio"];
                        var lbShare = this.FindByName<Label>("LblActShare"); if (lbShare != null) lbShare.Text = dynamicUi["act_share"];
                        var lbSave = this.FindByName<Label>("LblActSave"); if (lbSave != null) lbSave.Text = dynamicUi["act_save"];
                        var lbQr = this.FindByName<Label>("LblActQr"); if (lbQr != null) lbQr.Text = dynamicUi["act_qr"];

                        var lblAddressTitle = this.FindByName<Label>("LblAddressTitle"); if (lblAddressTitle != null) lblAddressTitle.Text = dynamicUi["field_address"];
                        var lblDistanceTitle = this.FindByName<Label>("LblDistanceTitle"); if (lblDistanceTitle != null) lblDistanceTitle.Text = dynamicUi["field_distance"];
                        var lblOpeningTitle = this.FindByName<Label>("LblOpeningHoursTitle"); if (lblOpeningTitle != null) lblOpeningTitle.Text = dynamicUi["field_opening_hours"];
                        var lblWebsiteTitle = this.FindByName<Label>("LblWebsiteTitle"); if (lblWebsiteTitle != null) lblWebsiteTitle.Text = dynamicUi["field_website"];
                        var lblPhoneTitle = this.FindByName<Label>("LblPhoneTitle"); if (lblPhoneTitle != null) lblPhoneTitle.Text = dynamicUi["field_phone"];

                        var lblHighlightsTitle = this.FindByName<Label>("LblHighlightsTitle"); if (lblHighlightsTitle != null) lblHighlightsTitle.Text = dynamicUi["highlights_title"];
                        var btnSavedShortcut = this.FindByName<Button>("BtnSavedShortcut"); if (btnSavedShortcut != null) btnSavedShortcut.Text = dynamicUi["saved"];
                        var btnViewAllHighlights = this.FindByName<Button>("BtnViewAllHighlights"); if (btnViewAllHighlights != null) btnViewAllHighlights.Text = dynamicUi["view_more"];
                        var btnForceSync = this.FindByName<Button>("BtnForceSyncNow"); if (btnForceSync != null) btnForceSync.Text = dynamicUi["force_sync_now"];
                        var btnCloseLangX = this.FindByName<Button>("BtnCloseLangPanelX"); if (btnCloseLangX != null) btnCloseLangX.Text = dynamicUi["close_x"];
                        var btnConfirmMenu = this.FindByName<Button>("BtnConfirmMenu"); if (btnConfirmMenu != null) btnConfirmMenu.Text = dynamicUi["ok"];
                        var btnEnableOfflineMap = this.FindByName<Button>("BtnEnableOfflineMap"); if (btnEnableOfflineMap != null) btnEnableOfflineMap.Text = dynamicUi["offline_map_download"];
                        var btnShowSaved = this.FindByName<Button>("BtnShowSaved"); if (btnShowSaved != null) btnShowSaved.Text = dynamicUi["show_saved"];
                        var lblOfflineMapTitle = this.FindByName<Label>("LblOfflineMapTitle"); if (lblOfflineMapTitle != null) lblOfflineMapTitle.Text = dynamicUi["offline_map_title"];
                        var btnStartTracking = this.FindByName<Button>("BtnStartTracking"); if (btnStartTracking != null) btnStartTracking.Text = dynamicUi["tracking_start"];
                        var btnStopTracking = this.FindByName<Button>("BtnStopTracking"); if (btnStopTracking != null) btnStopTracking.Text = dynamicUi["tracking_stop"];
                        var lblMapLoading = this.FindByName<Label>("LblMapLoading"); if (lblMapLoading != null) lblMapLoading.Text = dynamicUi["map_loading"];

                        if (SearchPoiBar != null) SearchPoiBar.Placeholder = dynamicUi["search_placeholder"];
                        if (SearchPoiBar != null) SearchPoiBar.CancelButtonColor = Microsoft.Maui.Graphics.Color.FromArgb("#9AA0A6");
                        var langTitle = this.FindByName<Label>("LblLangTitle"); if (langTitle != null) langTitle.Text = dynamicUi["select_language"];
                        var btnClose = this.FindByName<Button>("BtnCloseMenu"); if (btnClose != null) btnClose.Text = dynamicUi["close"];

                        var reviewHint = this.FindByName<Label>("LblReviewHint"); if (reviewHint != null) reviewHint.Text = dynamicUi["review_hint"];

                        if (BtnLangVI != null) BtnLangVI.Text = "🇻🇳 " + dynamicUi["lang_vi"];
                        if (BtnLangEN != null) BtnLangEN.Text = "🇺🇸 " + dynamicUi["lang_en"];
                        if (BtnLangJA != null) BtnLangJA.Text = "🇯🇵 " + dynamicUi["lang_ja"];
                        if (BtnLangKO != null) BtnLangKO.Text = "🇰🇷 " + dynamicUi["lang_ko"];
                        if (BtnLangRU != null) BtnLangRU.Text = "🇷🇺 " + dynamicUi["lang_ru"];
                        if (BtnLangFR != null) BtnLangFR.Text = "🇫🇷 " + dynamicUi["lang_fr"];
                        if (BtnLangTH != null) BtnLangTH.Text = "🇹🇭 " + dynamicUi["lang_th"];
                        if (BtnLangZH != null) BtnLangZH.Text = "🇨🇳 " + dynamicUi["lang_zh"];
                        if (BtnLangES != null) BtnLangES.Text = "🇪🇸 " + dynamicUi["lang_es"];

                        if (LblOfflineMapStatus != null && (LblOfflineMapStatus.Text?.Contains(":") == true))
                        {
                            var statusBody = LblOfflineMapStatus.Text[(LblOfflineMapStatus.Text.IndexOf(':') + 1)..].Trim();
                            LblOfflineMapStatus.Text = $"{dynamicUi["offline_status_prefix"]}: {statusBody}";
                        }

                        if (LblOfflineMapProgress != null && (LblOfflineMapProgress.Text?.Contains(":") == true))
                        {
                            var progressBody = LblOfflineMapProgress.Text[(LblOfflineMapProgress.Text.IndexOf(':') + 1)..].Trim();
                            LblOfflineMapProgress.Text = $"{dynamicUi["offline_progress_prefix"]}: {progressBody}";
                        }

                        _ = UpdateOfflineMapUiLocalizedAsync();
                    }
                    catch { }
                });
            }
            catch { }
        }

        private async Task<Dictionary<string, string>> BuildDynamicUiTextAsync(string language)
        {
            var lang = NormalizeLanguageCode(language);
            var viTexts = new Dictionary<string, string>
            {
                ["tab_overview"] = "Tổng quan",
                ["tab_intro"] = "Giới thiệu",
                ["tab_review"] = "Đánh giá",
                ["read_more"] = "Xem thêm",
                ["act_directions"] = "Dẫn đường",
                ["act_listen_now"] = "Nghe ngay",
                ["act_audio"] = "Audio",
                ["act_share"] = "Chia sẻ",
                ["act_save"] = "Lưu",
                ["act_qr"] = "Mã QR",
                ["field_address"] = "Địa chỉ",
                ["field_distance"] = "Khoảng cách",
                ["field_opening_hours"] = "Giờ mở cửa",
                ["field_website"] = "Website",
                ["field_phone"] = "Điện thoại",
                ["language"] = "Language",
                ["select_language"] = "Select language",
                ["change_language"] = "Change language",
                ["field_address_en"] = "Address",
                ["field_opening_hours_en"] = "Opening hours",
                ["field_price_en"] = "Price",
                ["listen_narration"] = "Listen narration",
                ["stop_narration"] = "Stop",
                ["priority_chip"] = "Ưu tiên {value}",
                ["search_placeholder"] = "Tìm kiếm...",
                ["lang_settings"] = "⚙️ Cài đặt ngôn ngữ",
                ["highlights_title"] = "Nổi bật trong khu vực",
                ["saved"] = "Đã lưu",
                ["view_more"] = "Xem thêm",
                ["force_sync_now"] = "Force Sync now",
                ["close"] = "Đóng",
                ["close_x"] = "✕",
                ["apply"] = "Áp dụng",
                ["ok"] = "OK",
                ["cancel"] = "Hủy",
                ["offline_map_download"] = "Tải map offline",
                ["offline_map_title"] = "🗺️ Bản đồ offline",
                ["custom_language_title"] = "Ngôn ngữ khác (custom)",
                ["custom_language_placeholder"] = "Ví dụ: de, it, ar, hi...",
                ["map_loading"] = "Đang tải bản đồ...",
                ["show_saved"] = "Hiện những địa điểm đã lưu",
                ["review_hint"] = "Nội dung đánh giá đang được cập nhật",
                ["no_description"] = "Chưa có mô tả cho địa điểm này.",
                ["open_now"] = "Đang mở cửa",
                ["closed"] = "Đóng cửa",
                ["no_rating"] = "Chưa có đánh giá",
                ["rating_label"] = "Đánh giá",
                ["read_less"] = "Rút gọn",
                ["new_badge"] = "Mới",
                ["popular_hint_hot"] = "Địa điểm có nhiều khách ghé qua",
                ["tracking_start"] = "Bắt đầu",
                ["tracking_stop"] = "Dừng",
                ["tracking_status_tracking"] = "Trạng thái: đang theo dõi",
                ["tracking_status_stopped"] = "Trạng thái: đã dừng",
                ["offline_status_prefix"] = "Trạng thái",
                ["offline_progress_prefix"] = "Tiến độ",
                ["offline_files_suffix"] = "tệp",
                ["offline_status_service_missing"] = "Thiếu dịch vụ bản đồ offline",
                ["offline_status_downloading_pack"] = "Đang tải gói bản đồ offline...",
                ["offline_status_download_failed"] = "Tải bản đồ offline thất bại",
                ["offline_status_ready"] = "Bản đồ offline đã sẵn sàng",
                ["offline_status_online"] = "Có mạng, bản đồ online đang hoạt động",
                ["offline_status_online_with_offline_ready"] = "Có mạng, bản đồ online đang hoạt động (offline đã sẵn sàng)",
                ["offline_status_offline_using"] = "Mất mạng, đang dùng bản đồ offline",
                ["offline_status_offline_no_pack"] = "Mất mạng, chưa có bản đồ offline (vẫn dùng Google Map)",
                ["offline_status_downloading_template"] = "{stage} {downloaded}/{total} ({percent}%)",
                ["progress_done"] = "hoàn tất",
                ["progress_failed"] = "thất bại",
                ["lang_vi"] = "Tiếng Việt",
                ["lang_en"] = "English",
                ["lang_ja"] = "日本語",
                ["lang_ko"] = "한국어",
                ["lang_ru"] = "Русский",
                ["lang_fr"] = "Français",
                ["lang_th"] = "ไทย",
                ["lang_zh"] = "中文",
                ["lang_es"] = "Español"
            };

            if (lang == "vi") return viTexts;

            var enTexts = new Dictionary<string, string>
            {
                ["tab_overview"] = "Overview",
                ["tab_intro"] = "Introduction",
                ["tab_review"] = "Reviews",
                ["read_more"] = "Read more",
                ["act_directions"] = "Directions",
                ["act_listen_now"] = "Listen now",
                ["act_audio"] = "Audio",
                ["act_share"] = "Share",
                ["act_save"] = "Save",
                ["act_qr"] = "QR code",
                ["field_address"] = "Address",
                ["field_distance"] = "Distance",
                ["field_opening_hours"] = "Opening hours",
                ["field_website"] = "Website",
                ["field_phone"] = "Phone",
                ["language"] = "Language",
                ["select_language"] = "Select language",
                ["change_language"] = "Change language",
                ["field_address_en"] = "Address",
                ["field_opening_hours_en"] = "Opening hours",
                ["field_price_en"] = "Price",
                ["listen_narration"] = "Listen narration",
                ["stop_narration"] = "Stop",
                ["priority_chip"] = "Priority {value}",
                ["search_placeholder"] = "Search...",
                ["lang_settings"] = "⚙️ Language settings",
                ["highlights_title"] = "Highlights in this area",
                ["saved"] = "Saved",
                ["view_more"] = "View more",
                ["force_sync_now"] = "Force Sync now",
                ["close"] = "Close",
                ["close_x"] = "✕",
                ["apply"] = "Apply",
                ["ok"] = "OK",
                ["cancel"] = "Cancel",
                ["offline_map_download"] = "Download offline map",
                ["offline_map_title"] = "🗺️ Offline map",
                ["custom_language_title"] = "Other language (custom)",
                ["custom_language_placeholder"] = "Example: de, it, ar, hi...",
                ["map_loading"] = "Loading map...",
                ["show_saved"] = "Show saved places",
                ["review_hint"] = "Review content is being updated",
                ["no_description"] = "No description available.",
                ["open_now"] = "Open now",
                ["closed"] = "Closed",
                ["no_rating"] = "No rating",
                ["rating_label"] = "Rating",
                ["read_less"] = "Show less",
                ["new_badge"] = "New",
                ["popular_hint_hot"] = "Popular place with high live traffic",
                ["tracking_start"] = "Start",
                ["tracking_stop"] = "Stop",
                ["tracking_status_tracking"] = "Status: tracking",
                ["tracking_status_stopped"] = "Status: stopped",
                ["offline_status_prefix"] = "Status",
                ["offline_progress_prefix"] = "Progress",
                ["offline_files_suffix"] = "files",
                ["offline_status_service_missing"] = "Offline map service missing",
                ["offline_status_downloading_pack"] = "Downloading offline map pack...",
                ["offline_status_download_failed"] = "Offline map download failed",
                ["offline_status_ready"] = "Offline map is ready",
                ["offline_status_online"] = "Internet available, online map is active",
                ["offline_status_online_with_offline_ready"] = "Internet available, online map is active (offline ready)",
                ["offline_status_offline_using"] = "No internet, using offline map",
                ["offline_status_offline_no_pack"] = "No internet, offline map is not downloaded yet (still using Google Map)",
                ["offline_status_downloading_template"] = "{stage} {downloaded}/{total} ({percent}%)",
                ["progress_done"] = "done",
                ["progress_failed"] = "failed",
                ["lang_vi"] = "Vietnamese",
                ["lang_en"] = "English",
                ["lang_ja"] = "Japanese",
                ["lang_ko"] = "Korean",
                ["lang_ru"] = "Russian",
                ["lang_fr"] = "French",
                ["lang_th"] = "Thai",
                ["lang_zh"] = "Chinese",
                ["lang_es"] = "Spanish"
            };

            if (lang == "en") return enTexts;

            if (lang == "ja")
            {
                return MergeLocalizedMap(enTexts, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tab_overview"] = "概要",
                    ["tab_intro"] = "紹介",
                    ["tab_review"] = "レビュー",
                    ["read_more"] = "もっと見る",
                    ["act_directions"] = "ルート案内",
                    ["act_listen_now"] = "今すぐ再生",
                    ["act_share"] = "共有",
                    ["act_save"] = "保存",
                    ["field_address"] = "住所",
                    ["field_distance"] = "距離",
                    ["field_opening_hours"] = "営業時間",
                    ["field_phone"] = "電話",
                    ["search_placeholder"] = "検索...",
                    ["highlights_title"] = "このエリアの注目スポット",
                    ["saved"] = "保存済み",
                    ["view_more"] = "もっと見る",
                    ["close"] = "閉じる",
                    ["apply"] = "適用",
                    ["cancel"] = "キャンセル",
                    ["offline_map_download"] = "オフラインマップをダウンロード",
                    ["custom_language_title"] = "その他の言語（カスタム）",
                    ["map_loading"] = "地図を読み込み中...",
                    ["show_saved"] = "保存済みスポットを表示",
                    ["no_description"] = "このスポットの説明はまだありません。",
                    ["open_now"] = "営業中",
                    ["closed"] = "営業時間外",
                    ["no_rating"] = "評価なし",
                    ["rating_label"] = "評価",
                    ["read_less"] = "折りたたむ",
                    ["new_badge"] = "新着",
                    ["tracking_start"] = "開始",
                    ["tracking_stop"] = "停止",
                    ["offline_status_prefix"] = "ステータス",
                    ["offline_progress_prefix"] = "進捗"
                });
            }

            if (lang == "ko")
            {
                return MergeLocalizedMap(enTexts, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tab_overview"] = "개요",
                    ["tab_intro"] = "소개",
                    ["tab_review"] = "리뷰",
                    ["read_more"] = "더보기",
                    ["act_directions"] = "길찾기",
                    ["act_listen_now"] = "지금 듣기",
                    ["act_share"] = "공유",
                    ["act_save"] = "저장",
                    ["field_address"] = "주소",
                    ["field_distance"] = "거리",
                    ["field_opening_hours"] = "영업시간",
                    ["field_phone"] = "전화",
                    ["search_placeholder"] = "검색...",
                    ["highlights_title"] = "이 지역 하이라이트",
                    ["saved"] = "저장됨",
                    ["view_more"] = "더보기",
                    ["close"] = "닫기",
                    ["apply"] = "적용",
                    ["cancel"] = "취소",
                    ["offline_map_download"] = "오프라인 지도 다운로드",
                    ["custom_language_title"] = "기타 언어(사용자 지정)",
                    ["map_loading"] = "지도를 불러오는 중...",
                    ["show_saved"] = "저장된 장소 보기",
                    ["no_description"] = "설명이 없습니다.",
                    ["open_now"] = "영업 중",
                    ["closed"] = "영업 종료",
                    ["no_rating"] = "평점 없음",
                    ["rating_label"] = "평점",
                    ["read_less"] = "간단히 보기",
                    ["new_badge"] = "신규",
                    ["tracking_start"] = "시작",
                    ["tracking_stop"] = "중지",
                    ["offline_status_prefix"] = "상태",
                    ["offline_progress_prefix"] = "진행률"
                });
            }

            if (lang == "ru")
            {
                return MergeLocalizedMap(enTexts, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tab_overview"] = "Обзор",
                    ["tab_intro"] = "Описание",
                    ["tab_review"] = "Отзывы",
                    ["read_more"] = "Подробнее",
                    ["act_directions"] = "Маршрут",
                    ["act_listen_now"] = "Слушать",
                    ["act_share"] = "Поделиться",
                    ["act_save"] = "Сохранить",
                    ["field_address"] = "Адрес",
                    ["field_distance"] = "Расстояние",
                    ["field_opening_hours"] = "Часы работы",
                    ["field_phone"] = "Телефон",
                    ["search_placeholder"] = "Поиск...",
                    ["highlights_title"] = "Популярные места рядом",
                    ["saved"] = "Сохранено",
                    ["view_more"] = "Показать еще",
                    ["close"] = "Закрыть",
                    ["apply"] = "Применить",
                    ["cancel"] = "Отмена",
                    ["offline_map_download"] = "Скачать офлайн-карту",
                    ["custom_language_title"] = "Другой язык (пользовательский)",
                    ["map_loading"] = "Загрузка карты...",
                    ["show_saved"] = "Показать сохраненные места",
                    ["no_description"] = "Описание отсутствует.",
                    ["open_now"] = "Открыто",
                    ["closed"] = "Закрыто",
                    ["no_rating"] = "Нет рейтинга",
                    ["rating_label"] = "Рейтинг",
                    ["read_less"] = "Свернуть",
                    ["new_badge"] = "Новое",
                    ["tracking_start"] = "Старт",
                    ["tracking_stop"] = "Стоп",
                    ["offline_status_prefix"] = "Статус",
                    ["offline_progress_prefix"] = "Прогресс"
                });
            }

            if (lang == "fr")
            {
                return MergeLocalizedMap(enTexts, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tab_overview"] = "Aperçu",
                    ["tab_intro"] = "Présentation",
                    ["tab_review"] = "Avis",
                    ["read_more"] = "Voir plus",
                    ["act_directions"] = "Itinéraire",
                    ["act_listen_now"] = "Écouter",
                    ["act_share"] = "Partager",
                    ["act_save"] = "Enregistrer",
                    ["field_address"] = "Adresse",
                    ["field_distance"] = "Distance",
                    ["field_opening_hours"] = "Heures d'ouverture",
                    ["field_phone"] = "Téléphone",
                    ["search_placeholder"] = "Rechercher...",
                    ["highlights_title"] = "Lieux remarquables dans cette zone",
                    ["saved"] = "Enregistré",
                    ["view_more"] = "Voir plus",
                    ["close"] = "Fermer",
                    ["apply"] = "Appliquer",
                    ["cancel"] = "Annuler",
                    ["offline_map_download"] = "Télécharger la carte hors ligne",
                    ["custom_language_title"] = "Autre langue (personnalisée)",
                    ["map_loading"] = "Chargement de la carte...",
                    ["show_saved"] = "Afficher les lieux enregistrés",
                    ["no_description"] = "Aucune description disponible.",
                    ["open_now"] = "Ouvert",
                    ["closed"] = "Fermé",
                    ["no_rating"] = "Pas d'évaluation",
                    ["rating_label"] = "Note",
                    ["read_less"] = "Réduire",
                    ["new_badge"] = "Nouveau",
                    ["tracking_start"] = "Démarrer",
                    ["tracking_stop"] = "Arrêter",
                    ["offline_status_prefix"] = "Statut",
                    ["offline_progress_prefix"] = "Progression"
                });
            }

            if (lang == "th")
            {
                return MergeLocalizedMap(enTexts, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tab_overview"] = "ภาพรวม",
                    ["tab_intro"] = "แนะนำ",
                    ["tab_review"] = "รีวิว",
                    ["read_more"] = "ดูเพิ่มเติม",
                    ["act_directions"] = "นำทาง",
                    ["act_listen_now"] = "ฟังตอนนี้",
                    ["act_share"] = "แชร์",
                    ["act_save"] = "บันทึก",
                    ["field_address"] = "ที่อยู่",
                    ["field_distance"] = "ระยะทาง",
                    ["field_opening_hours"] = "เวลาเปิดทำการ",
                    ["field_phone"] = "โทรศัพท์",
                    ["search_placeholder"] = "ค้นหา...",
                    ["highlights_title"] = "จุดเด่นในพื้นที่นี้",
                    ["saved"] = "บันทึกแล้ว",
                    ["view_more"] = "ดูเพิ่มเติม",
                    ["close"] = "ปิด",
                    ["apply"] = "ใช้",
                    ["cancel"] = "ยกเลิก",
                    ["offline_map_download"] = "ดาวน์โหลดแผนที่ออฟไลน์",
                    ["custom_language_title"] = "ภาษาอื่น (กำหนดเอง)",
                    ["map_loading"] = "กำลังโหลดแผนที่...",
                    ["show_saved"] = "แสดงสถานที่ที่บันทึกไว้",
                    ["no_description"] = "ยังไม่มีคำอธิบาย",
                    ["open_now"] = "เปิดอยู่",
                    ["closed"] = "ปิด",
                    ["no_rating"] = "ยังไม่มีคะแนน",
                    ["rating_label"] = "คะแนน",
                    ["read_less"] = "ย่อ",
                    ["new_badge"] = "ใหม่",
                    ["tracking_start"] = "เริ่ม",
                    ["tracking_stop"] = "หยุด",
                    ["offline_status_prefix"] = "สถานะ",
                    ["offline_progress_prefix"] = "ความคืบหน้า"
                });
            }

            if (lang == "zh")
            {
                return MergeLocalizedMap(enTexts, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tab_overview"] = "概览",
                    ["tab_intro"] = "介绍",
                    ["tab_review"] = "评价",
                    ["read_more"] = "查看更多",
                    ["act_directions"] = "导航",
                    ["act_listen_now"] = "立即收听",
                    ["act_share"] = "分享",
                    ["act_save"] = "保存",
                    ["field_address"] = "地址",
                    ["field_distance"] = "距离",
                    ["field_opening_hours"] = "营业时间",
                    ["field_phone"] = "电话",
                    ["search_placeholder"] = "搜索...",
                    ["highlights_title"] = "此区域热门地点",
                    ["saved"] = "已保存",
                    ["view_more"] = "查看更多",
                    ["close"] = "关闭",
                    ["apply"] = "应用",
                    ["cancel"] = "取消",
                    ["offline_map_download"] = "下载离线地图",
                    ["custom_language_title"] = "其他语言（自定义）",
                    ["map_loading"] = "地图加载中...",
                    ["show_saved"] = "显示已保存地点",
                    ["no_description"] = "暂无描述。",
                    ["open_now"] = "营业中",
                    ["closed"] = "已关闭",
                    ["no_rating"] = "暂无评分",
                    ["rating_label"] = "评分",
                    ["read_less"] = "收起",
                    ["new_badge"] = "新",
                    ["tracking_start"] = "开始",
                    ["tracking_stop"] = "停止",
                    ["offline_status_prefix"] = "状态",
                    ["offline_progress_prefix"] = "进度"
                });
            }

            if (lang == "es")
            {
                return MergeLocalizedMap(enTexts, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tab_overview"] = "Resumen",
                    ["tab_intro"] = "Introducción",
                    ["tab_review"] = "Reseñas",
                    ["read_more"] = "Ver más",
                    ["act_directions"] = "Cómo llegar",
                    ["act_listen_now"] = "Escuchar ahora",
                    ["act_share"] = "Compartir",
                    ["act_save"] = "Guardar",
                    ["field_address"] = "Dirección",
                    ["field_distance"] = "Distancia",
                    ["field_opening_hours"] = "Horario",
                    ["field_phone"] = "Teléfono",
                    ["search_placeholder"] = "Buscar...",
                    ["highlights_title"] = "Destacados en esta zona",
                    ["saved"] = "Guardado",
                    ["view_more"] = "Ver más",
                    ["close"] = "Cerrar",
                    ["apply"] = "Aplicar",
                    ["cancel"] = "Cancelar",
                    ["offline_map_download"] = "Descargar mapa sin conexión",
                    ["custom_language_title"] = "Otro idioma (personalizado)",
                    ["map_loading"] = "Cargando mapa...",
                    ["show_saved"] = "Mostrar lugares guardados",
                    ["no_description"] = "No hay descripción disponible.",
                    ["open_now"] = "Abierto ahora",
                    ["closed"] = "Cerrado",
                    ["no_rating"] = "Sin calificación",
                    ["rating_label"] = "Calificación",
                    ["read_less"] = "Mostrar menos",
                    ["new_badge"] = "Nuevo",
                    ["tracking_start"] = "Iniciar",
                    ["tracking_stop"] = "Detener",
                    ["offline_status_prefix"] = "Estado",
                    ["offline_progress_prefix"] = "Progreso"
                });
            }

            return enTexts;
        }

        private async Task<string> GetTrackingStatusTextAsync(string state)
        {
            var ui = await BuildDynamicUiTextAsync(_currentLanguage);
            return string.Equals(state, "tracking", StringComparison.OrdinalIgnoreCase)
                ? ui["tracking_status_tracking"]
                : ui["tracking_status_stopped"];
        }

        private async Task<string> GetOfflineMapStatusTextAsync(string key, string? arg = null)
        {
            var ui = await BuildDynamicUiTextAsync(_currentLanguage);
            var prefix = ui["offline_status_prefix"];
            var body = key switch
            {
                "service_missing" => ui["offline_status_service_missing"],
                "downloading_pack" => ui["offline_status_downloading_pack"],
                "download_failed" => string.IsNullOrWhiteSpace(arg)
                    ? ui["offline_status_download_failed"]
                    : $"{ui["offline_status_download_failed"]} ({arg})",
                "ready" => string.IsNullOrWhiteSpace(arg)
                    ? ui["offline_status_ready"]
                    : $"{ui["offline_status_ready"]} ({arg} {ui["offline_files_suffix"]})",
                "online" => ui["offline_status_online"],
                "online_with_offline_ready" => ui["offline_status_online_with_offline_ready"],
                "offline_using" => ui["offline_status_offline_using"],
                "offline_no_pack" => ui["offline_status_offline_no_pack"],
                _ => ui["offline_status_online"]
            };

            return $"{prefix}: {body}";
        }

        private string FormatOfflineMapDownloadingStatus(string stage, int downloadedFiles, int totalFiles, double percent)
        {
            var lang = NormalizeLanguageCode(_currentLanguage);
            var prefix = _dynamicUiTextCache.TryGetValue($"ui:{lang}:offline_status_prefix", out var p)
                ? p
                : "Status";
            var template = _dynamicUiTextCache.TryGetValue($"ui:{lang}:offline_status_downloading_template", out var t)
                ? t
                : "{stage} {downloaded}/{total} ({percent}%)";

            var body = template
                .Replace("{stage}", stage ?? string.Empty)
                .Replace("{downloaded}", downloadedFiles.ToString())
                .Replace("{total}", totalFiles.ToString())
                .Replace("{percent}", percent.ToString("0.0"));

            return $"{prefix}: {body}";
        }

        private async Task<string> GetOfflineMapProgressTextAsync(double percent, bool completed = false, bool failed = false)
        {
            var ui = await BuildDynamicUiTextAsync(_currentLanguage);
            var prefix = ui["offline_progress_prefix"];
            var value = percent.ToString("0.0");

            if (completed)
            {
                return $"{prefix}: {value}% ({ui["progress_done"]})";
            }

            if (failed)
            {
                return $"{prefix}: {value}% ({ui["progress_failed"]})";
            }

            return $"{prefix}: {value}%";
        }

        private string FormatOfflineMapProgressText(double percent, int downloadedFiles, int totalFiles)
        {
            var lang = NormalizeLanguageCode(_currentLanguage);
            var prefix = _dynamicUiTextCache.TryGetValue($"ui:{lang}:offline_progress_prefix", out var p)
                ? p
                : "Progress";
            var filesSuffix = _dynamicUiTextCache.TryGetValue($"ui:{lang}:offline_files_suffix", out var f)
                ? f
                : "files";
            return $"{prefix}: {percent:0.0}% ({downloadedFiles}/{totalFiles} {filesSuffix})";
        }

        private async Task UpdateOfflineMapUiLocalizedAsync()
        {
            try
            {
                var progressValue = Math.Clamp(PbOfflineMapProgress?.Progress ?? 0d, 0d, 1d) * 100d;
                var isFailed = LblOfflineMapProgress?.Text?.Contains("failed", StringComparison.OrdinalIgnoreCase) == true
                               || LblOfflineMapProgress?.Text?.Contains("thất bại", StringComparison.OrdinalIgnoreCase) == true;
                var progressText = await GetOfflineMapProgressTextAsync(progressValue,
                    completed: progressValue >= 99.9,
                    failed: isFailed);
                UpdateOfflineMapProgressUi(progressValue / 100d, progressText);

                if (LblTrackingStatus != null)
                {
                    var isTracking = LblTrackingStatus.Text?.Contains("tracking", StringComparison.OrdinalIgnoreCase) == true
                                     || LblTrackingStatus.Text?.Contains("đang theo dõi", StringComparison.OrdinalIgnoreCase) == true;
                    LblTrackingStatus.Text = await GetTrackingStatusTextAsync(isTracking ? "tracking" : "stopped");
                }
            }
            catch { }
        }
    }
}
