using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using VinhKhanh.Data;
using VinhKhanh.Services;
using VinhKhanh.Shared;

namespace VinhKhanh.Pages
{
    public partial class MapPage
    {
        // Item ViewModel cho danh sách MP3
        public class AudioListItem
        {
            public AudioModel Source { get; set; } = new();
            public string DisplayName { get; set; } = string.Empty;
            public string LanguageLabel { get; set; } = string.Empty;
        }

        public ObservableCollection<AudioListItem> AudioListItems { get; } = new();

        // ===== "Nghe ngay" (TTS) → mở popup armed =====
        private async void OnNgheNgayClicked(object sender, EventArgs e)
        {
            try
            {
                var poi = _selectedPoi;
                if (poi == null) return;

                var content = await GetContentForLanguageAsync(poi.Id, _currentLanguage);
                var text = content?.Description;
                if (string.IsNullOrWhiteSpace(text))
                {
                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["audio"], t.TryGetValue("no_audio_for_lang", out var msg) ? msg : "Không có thuyết minh cho ngôn ngữ này.", t["ok"]);
                    return;
                }

                HideAudioListPopup();
                var title = string.IsNullOrWhiteSpace(poi.Name) ? "Nghe ngay" : poi.Name;
                await ShowMiniPlayerArmedAsync(title, isTts: true, playAction: async () =>
                {
                    try
                    {
                        await PlayNarration(text);
                    }
                    catch { }
                });
            }
            catch { }
        }

        // ===== "Audio" (MP3 list) → mở danh sách file =====
        private async void OnAudioListClicked(object sender, EventArgs e)
        {
            try
            {
                var poi = _selectedPoi;
                if (poi == null) return;

                var preferredLang = NormalizeLanguageCode(_currentLanguage);
                var audios = await _apiService.GetAudiosByPoiIdAsync(poi.Id) ?? new List<AudioModel>();
                await TrackPoiEventAsync("audio_list_open", poi.Id, $"\"trigger\":\"audio_button\",\"lang\":\"{preferredLang}\"");

                // Chỉ lấy file MP3 (IsTts=false) theo ngôn ngữ ưu tiên với fallback
                var uploaded = SelectAudioListByLanguage(audios, preferredLang, isTts: false);
                if (uploaded == null || uploaded.Count == 0)
                {
                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["audio"], t.TryGetValue("no_audio_for_lang", out var msg) ? msg : "Không có file audio cho ngôn ngữ này.", t["ok"]);
                    return;
                }

                AudioListItems.Clear();
                foreach (var a in uploaded)
                {
                    AudioListItems.Add(new AudioListItem
                    {
                        Source = a,
                        DisplayName = BuildAudioDisplayName(a),
                        LanguageLabel = BuildAudioLanguageLabel(a)
                    });
                }

                if (AudioListView != null && AudioListView.ItemsSource == null)
                {
                    AudioListView.ItemsSource = AudioListItems;
                }

                HideMiniPlayer();
                if (AudioListPopup != null) AudioListPopup.IsVisible = true;
                if (AudioListTitleLabel != null)
                {
                    AudioListTitleLabel.Text = string.IsNullOrWhiteSpace(poi.Name) ? "🎵 Chọn file audio" : $"🎵 {poi.Name}";
                }
                if (AudioListSubtitleLabel != null)
                {
                    AudioListSubtitleLabel.Text = $"Ngôn ngữ: {DescribeLanguage(preferredLang)} • {AudioListItems.Count} file";
                }
            }
            catch
            {
                try { var t = await GetDialogTextsAsync(); await DisplayAlert(t["audio"], t.TryGetValue("cannot_load_audio_list", out var msg) ? msg : "Không tải được danh sách audio.", t["ok"]); } catch { }
            }
        }

        private void OnAudioListCloseClicked(object sender, EventArgs e)
        {
            HideAudioListPopup();
        }

        private async void OnAudioListItemPlayClicked(object sender, EventArgs e)
        {
            try
            {
                AudioListItem? item = null;
                if (sender is Button btn && btn.CommandParameter is AudioListItem cp) item = cp;
                if (item == null && sender is BindableObject bo && bo.BindingContext is AudioListItem ctx) item = ctx;
                if (item == null) return;

                var poi = _selectedPoi;
                if (poi == null) return;

                var audio = item.Source;
                if (audio == null || string.IsNullOrWhiteSpace(audio.Url)) return;

                var playUrl = ToAbsoluteApiUrl(audio.Url);
                if (string.IsNullOrWhiteSpace(playUrl))
                {
                    var t = await GetDialogTextsAsync();
                    await DisplayAlert(t["audio"], t.TryGetValue("invalid_audio_file", out var msg) ? msg : "File audio không hợp lệ.", t["ok"]);
                    return;
                }

                HideAudioListPopup();

                var preferredLang = NormalizeLanguageCode(_currentLanguage);
                var title = string.IsNullOrWhiteSpace(poi.Name) ? item.DisplayName : poi.Name;

                await ShowMiniPlayerArmedAsync(title, isTts: false, playAction: () =>
                {
                    try
                    {
                        var queueItem = new AudioItem
                        {
                            Key = $"mp3:{poi.Id}:{preferredLang}:{audio.Id}",
                            IsTts = false,
                            FilePath = playUrl,
                            Language = NormalizeLanguageCode(audio.LanguageCode),
                            PoiId = poi.Id,
                            Priority = poi.Priority
                        };
                        _audioQueue?.Enqueue(queueItem);
                        _ = TrackPoiEventAsync("audio_play", poi.Id, $"\"mode\":\"mp3\",\"trigger\":\"audio_picker\",\"lang\":\"{NormalizeLanguageCode(audio.LanguageCode)}\"");
                    }
                    catch { }
                    return Task.CompletedTask;
                });
            }
            catch { }
        }

        private void HideAudioListPopup()
        {
            try
            {
                if (AudioListPopup != null) AudioListPopup.IsVisible = false;
            }
            catch { }
        }

        private static string BuildAudioLanguageLabel(AudioModel audio)
        {
            if (audio == null) return string.Empty;
            var lang = NarrationService.NormalizeLanguageCode(audio.LanguageCode ?? string.Empty);
            return DescribeLanguage(lang);
        }

        private static string DescribeLanguage(string normalizedLang)
        {
            if (string.IsNullOrWhiteSpace(normalizedLang)) return "Mặc định";
            return normalizedLang switch
            {
                "vi" => "Tiếng Việt",
                "en" => "English",
                "zh" => "中文",
                "ja" => "日本語",
                "ko" => "한국어",
                "fr" => "Français",
                "de" => "Deutsch",
                "es" => "Español",
                "ru" => "Русский",
                _ => normalizedLang.ToUpperInvariant()
            };
        }
    }
}
