using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace VinhKhanh.Pages
{
    public partial class MapPage
    {
        private IDispatcherTimer? _miniPlayerTimer;
        private bool _isMiniPlayerDragging;
        private bool _miniPlayerInternalUpdate;
        private string _miniPlayerCurrentSource = string.Empty;
        // Coi như đang phát TTS (browser/OS speech) khi không có audio file
        // → IAudioService không biết về TTS, ta tự ẩn slider trong trường hợp đó.
        private bool _miniPlayerIsTtsOnly;
        private DateTime _miniPlayerOpenedAtUtc = DateTime.MinValue;

        // Armed mode: popup mở sẵn nhưng audio chưa phát; bấm nút Nghe/Play mới chạy.
        private bool _miniPlayerArmed;
        private Func<Task>? _miniPlayerPendingPlayAction;

        private async Task<Dictionary<string, string>> GetMiniPlayerUiAsync()
        {
            try
            {
                return await BuildDynamicUiTextAsync(_currentLanguage);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        // Hiện popup mini player. Gọi sau khi đã enqueue audio/TTS vào AudioQueueService.
        private async Task ShowMiniPlayerAsync(string sourceName, bool isTts)
        {
            try
            {
                var ui = await GetMiniPlayerUiAsync();
                string GetText(string key, string fallback)
                {
                    return ui.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                        ? value
                        : fallback;
                }

                _miniPlayerCurrentSource = string.IsNullOrWhiteSpace(sourceName)
                    ? (isTts ? GetText("mini_source_tts", "Nghe ngay") : GetText("mini_source_audio", "Audio"))
                    : sourceName.Trim();
                _miniPlayerIsTtsOnly = isTts;
                _miniPlayerOpenedAtUtc = DateTime.UtcNow;

                if (MiniPlayerPopup != null) MiniPlayerPopup.IsVisible = true;
                UpdateAudioOverlayVisibility();
                if (MiniPlayerTitleLabel != null) MiniPlayerTitleLabel.Text = _miniPlayerCurrentSource;
                if (MiniPlayerIconLabel != null) MiniPlayerIconLabel.Text = isTts ? "🗣️" : "🎧";

                if (MiniPlayerProgressSlider != null) MiniPlayerProgressSlider.IsVisible = true;
                if (MiniPlayerCurrentTimeLabel != null) MiniPlayerCurrentTimeLabel.IsVisible = true;
                if (MiniPlayerDurationLabel != null) MiniPlayerDurationLabel.IsVisible = true;

                if (MiniPlayerPlayPauseButton != null)
                {
                    MiniPlayerPlayPauseButton.Text = GetText("mini_button_pause", "⏸  Tạm dừng");
                    MiniPlayerPlayPauseButton.IsEnabled = !isTts;
                    MiniPlayerPlayPauseButton.Opacity = isTts ? 0.55 : 1.0;
                }

                if (MiniPlayerStateLabel != null)
                {
                    MiniPlayerStateLabel.Text = isTts
                        ? GetText("mini_state_playing_tts", "Đang phát thuyết minh")
                        : GetText("mini_state_playing_audio", "Đang phát audio");
                }

                _miniPlayerArmed = false;
                _miniPlayerPendingPlayAction = null;

                StartMiniPlayerTimer();
                await RefreshMiniPlayerUiAsync(forceSliderUpdate: true);
            }
            catch { }
        }

        // Mở popup ở chế độ "sẵn sàng phát": chưa start audio. Bấm nút Nghe mới chạy playAction.
        private async Task ShowMiniPlayerArmedAsync(string sourceName, bool isTts, Func<Task> playAction)
        {
            try
            {
                if (playAction == null) return;

                var ui = await GetMiniPlayerUiAsync();
                string GetText(string key, string fallback)
                {
                    return ui.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                        ? value
                        : fallback;
                }

                _miniPlayerCurrentSource = string.IsNullOrWhiteSpace(sourceName)
                    ? (isTts ? GetText("mini_source_tts", "Nghe ngay") : GetText("mini_source_audio", "Audio"))
                    : sourceName.Trim();
                _miniPlayerIsTtsOnly = isTts;
                _miniPlayerOpenedAtUtc = DateTime.UtcNow;
                _miniPlayerArmed = true;
                _miniPlayerPendingPlayAction = playAction;

                if (MiniPlayerPopup != null) MiniPlayerPopup.IsVisible = true;
                UpdateAudioOverlayVisibility();
                if (MiniPlayerTitleLabel != null) MiniPlayerTitleLabel.Text = _miniPlayerCurrentSource;
                if (MiniPlayerIconLabel != null) MiniPlayerIconLabel.Text = isTts ? "🗣️" : "🎧";

                if (MiniPlayerProgressSlider != null)
                {
                    _miniPlayerInternalUpdate = true;
                    MiniPlayerProgressSlider.Minimum = 0;
                    MiniPlayerProgressSlider.Maximum = 1;
                    MiniPlayerProgressSlider.Value = 0;
                    _miniPlayerInternalUpdate = false;
                    MiniPlayerProgressSlider.IsVisible = true;
                }
                if (MiniPlayerCurrentTimeLabel != null)
                {
                    MiniPlayerCurrentTimeLabel.IsVisible = true;
                    MiniPlayerCurrentTimeLabel.Text = "00:00";
                }
                if (MiniPlayerDurationLabel != null)
                {
                    MiniPlayerDurationLabel.IsVisible = true;
                    MiniPlayerDurationLabel.Text = "--:--";
                }

                if (MiniPlayerPlayPauseButton != null)
                {
                    MiniPlayerPlayPauseButton.Text = isTts
                        ? GetText("mini_button_play_tts", "▶  Nghe")
                        : GetText("mini_button_play_audio", "▶  Phát");
                    MiniPlayerPlayPauseButton.IsEnabled = true;
                    MiniPlayerPlayPauseButton.Opacity = 1.0;
                    MiniPlayerPlayPauseButton.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#1E88E5");
                }

                if (MiniPlayerStateLabel != null)
                {
                    MiniPlayerStateLabel.Text = isTts
                        ? GetText("mini_state_ready_tts", "Sẵn sàng phát thuyết minh")
                        : GetText("mini_state_ready_audio", "Sẵn sàng phát audio");
                }

                StopMiniPlayerTimer();
            }
            catch { }
            return;
        }

        private void HideMiniPlayer()
        {
            try
            {
                StopMiniPlayerTimer();

                if (MiniPlayerPopup != null) MiniPlayerPopup.IsVisible = false;
                UpdateAudioOverlayVisibility();

                if (MiniPlayerProgressSlider != null)
                {
                    _miniPlayerInternalUpdate = true;
                    MiniPlayerProgressSlider.Minimum = 0;
                    MiniPlayerProgressSlider.Maximum = 1;
                    MiniPlayerProgressSlider.Value = 0;
                    _miniPlayerInternalUpdate = false;
                }

                if (MiniPlayerCurrentTimeLabel != null) MiniPlayerCurrentTimeLabel.Text = "00:00";
                if (MiniPlayerDurationLabel != null) MiniPlayerDurationLabel.Text = "00:00";
                if (MiniPlayerStateLabel != null) MiniPlayerStateLabel.Text = string.Empty;

                _miniPlayerArmed = false;
                _miniPlayerPendingPlayAction = null;
            }
            catch { }
        }

        private void StartMiniPlayerTimer()
        {
            try
            {
                if (_miniPlayerTimer != null)
                {
                    _miniPlayerTimer.Stop();
                    _miniPlayerTimer = null;
                }

                _miniPlayerTimer = Dispatcher.CreateTimer();
                _miniPlayerTimer.Interval = TimeSpan.FromMilliseconds(350);
                _miniPlayerTimer.Tick += async (_, __) => await RefreshMiniPlayerUiAsync(forceSliderUpdate: false);
                _miniPlayerTimer.Start();
            }
            catch { }
        }

        private void StopMiniPlayerTimer()
        {
            try
            {
                if (_miniPlayerTimer == null) return;
                _miniPlayerTimer.Stop();
                _miniPlayerTimer = null;
            }
            catch { }
        }

        private async Task RefreshMiniPlayerUiAsync(bool forceSliderUpdate)
        {
            try
            {
                if (_audioService == null) return;
                if (_miniPlayerArmed) return; // Đang chờ user bấm Nghe — không auto refresh/đóng

                var ui = await GetMiniPlayerUiAsync();
                string GetText(string key, string fallback)
                {
                    return ui.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                        ? value
                        : fallback;
                }

                var duration = _audioService.Duration;
                var position = _audioService.Position;
                if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
                if (position < TimeSpan.Zero) position = TimeSpan.Zero;
                if (duration > TimeSpan.Zero && position > duration) position = duration;

                if (MiniPlayerCurrentTimeLabel != null)
                    MiniPlayerCurrentTimeLabel.Text = FormatDuration(position);

                if (MiniPlayerDurationLabel != null)
                    MiniPlayerDurationLabel.Text = FormatDuration(duration);

                if (MiniPlayerPlayPauseButton != null)
                {
                    MiniPlayerPlayPauseButton.Text = _audioService.IsPaused
                        ? GetText("mini_button_resume", "▶  Nghe tiếp")
                        : GetText("mini_button_pause", "⏸  Tạm dừng");
                    if (_miniPlayerIsTtsOnly)
                    {
                        var allowControl = _audioService.IsPlaying || _audioService.IsPaused;
                        MiniPlayerPlayPauseButton.IsEnabled = allowControl;
                        MiniPlayerPlayPauseButton.Opacity = allowControl ? 1.0 : 0.55;
                    }
                }

                if (MiniPlayerStateLabel != null)
                {
                    if (_audioService.IsPlaying)
                    {
                        MiniPlayerStateLabel.Text = _miniPlayerIsTtsOnly
                            ? GetText("mini_state_playing_tts", "Đang phát thuyết minh")
                            : GetText("mini_state_playing", "Đang phát");
                    }
                    else if (_audioService.IsPaused)
                    {
                        MiniPlayerStateLabel.Text = GetText("mini_state_paused", "Tạm dừng");
                    }
                    else
                    {
                        MiniPlayerStateLabel.Text = _miniPlayerIsTtsOnly
                            ? GetText("mini_state_playing_tts", "Đang phát thuyết minh")
                            : string.Empty;
                    }
                }

                if (MiniPlayerProgressSlider != null && (!_isMiniPlayerDragging || forceSliderUpdate))
                {
                    _miniPlayerInternalUpdate = true;
                    var max = Math.Max(1d, duration.TotalSeconds > 0 ? duration.TotalSeconds : 1d);
                    MiniPlayerProgressSlider.Minimum = 0;
                    MiniPlayerProgressSlider.Maximum = max;
                    MiniPlayerProgressSlider.Value = Math.Max(0d, Math.Min(max, position.TotalSeconds));
                    _miniPlayerInternalUpdate = false;
                }

                // Audio đã kết thúc (không playing, không paused) → tự ẩn popup
                if (!_audioService.IsPlaying && !_audioService.IsPaused
                    && MiniPlayerPopup?.IsVisible == true
                    && (DateTime.UtcNow - _miniPlayerOpenedAtUtc) > TimeSpan.FromSeconds(1))
                {
                    await Task.Delay(400);
                    if (!_audioService.IsPlaying && !_audioService.IsPaused)
                    {
                        HideMiniPlayer();
                    }
                }

                if (_miniPlayerIsTtsOnly)
                {
                    var elapsed = DateTime.UtcNow - _miniPlayerOpenedAtUtc;
                    if (elapsed > TimeSpan.FromSeconds(2)
                        && !_audioService.IsPlaying
                        && !_audioService.IsPaused
                        && (_narrationService == null || !IsNarrationActive()))
                    {
                        await Task.Delay(400);
                        if (!_audioService.IsPlaying && !_audioService.IsPaused && !IsNarrationActive())
                        {
                            HideMiniPlayer();
                        }
                    }
                }
            }
            catch { }
        }

        private bool IsNarrationActive()
        {
            // NarrationService không expose trạng thái — coi như không active.
            // Hàm này dành cho các ngôn ngữ TTS không xác định được trạng thái.
            return false;
        }

        private static string FormatDuration(TimeSpan value)
        {
            if (value < TimeSpan.Zero) value = TimeSpan.Zero;
            if (value.TotalHours >= 1) return value.ToString(@"hh\:mm\:ss");
            return value.ToString(@"mm\:ss");
        }

        private async void OnMiniPlayerPlayPauseClicked(object sender, EventArgs e)
        {
            try
            {
                var ui = await GetMiniPlayerUiAsync();
                string GetText(string key, string fallback)
                {
                    return ui.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                        ? value
                        : fallback;
                }

                // Armed: lần bấm đầu tiên = chạy playAction đã được preload
                if (_miniPlayerArmed && _miniPlayerPendingPlayAction != null)
                {
                    var action = _miniPlayerPendingPlayAction;
                    _miniPlayerPendingPlayAction = null;
                    _miniPlayerArmed = false;
                    _miniPlayerOpenedAtUtc = DateTime.UtcNow;

                    if (MiniPlayerStateLabel != null)
                    {
                        MiniPlayerStateLabel.Text = _miniPlayerIsTtsOnly
                            ? GetText("mini_state_playing_tts", "Đang phát thuyết minh")
                            : GetText("mini_state_playing_audio", "Đang phát audio");
                    }
                    if (MiniPlayerPlayPauseButton != null)
                    {
                        MiniPlayerPlayPauseButton.Text = _miniPlayerIsTtsOnly
                            ? GetText("mini_button_stop", "⏹  Dừng")
                            : GetText("mini_button_pause", "⏸  Tạm dừng");
                        MiniPlayerPlayPauseButton.IsEnabled = !_miniPlayerIsTtsOnly;
                        MiniPlayerPlayPauseButton.Opacity = _miniPlayerIsTtsOnly ? 0.55 : 1.0;
                    }

                    StartMiniPlayerTimer();
                    try { await action(); } catch { }
                    await RefreshMiniPlayerUiAsync(forceSliderUpdate: true);
                    return;
                }

                if (_audioService == null) return;
                if (_miniPlayerIsTtsOnly && !_audioService.IsPlaying && !_audioService.IsPaused) return;

                if (_audioService.IsPlaying)
                {
                    await _audioService.PauseAsync();
                }
                else if (_audioService.IsPaused)
                {
                    await _audioService.ResumeAsync();
                }

                await RefreshMiniPlayerUiAsync(forceSliderUpdate: true);
            }
            catch { }
        }

        private async void OnMiniPlayerStopClicked(object sender, EventArgs e)
        {
            try
            {
                try { _ = _audioQueue?.StopAsync(); } catch { }
                try { _narrationService?.Stop(); } catch { }
                if (_audioService != null)
                {
                    try { await _audioService.StopAsync(); } catch { }
                }
                HideMiniPlayer();
            }
            catch { }
        }

        private void OnMiniPlayerCloseClicked(object sender, EventArgs e)
        {
            // Đóng popup nhưng vẫn cho audio chạy nền (giống mini player chuẩn)
            HideMiniPlayer();
        }

        private void OnAudioOverlayTapped(object sender, EventArgs e)
        {
            try
            {
                HideAudioListPopup();

                if (MiniPlayerPopup?.IsVisible == true)
                {
                    try { _ = _audioQueue?.StopAsync(); } catch { }
                    try { _narrationService?.Stop(); } catch { }
                    try { _ = _audioService?.StopAsync(); } catch { }
                }

                HideMiniPlayer();
            }
            catch { }
        }

        private void UpdateAudioOverlayVisibility()
        {
            try
            {
                if (AudioOverlay == null) return;
                AudioOverlay.IsVisible = (AudioListPopup?.IsVisible == true) || (MiniPlayerPopup?.IsVisible == true);
            }
            catch { }
        }

        private void OnMiniPlayerProgressValueChanged(object sender, ValueChangedEventArgs e)
        {
            if (_miniPlayerInternalUpdate) return;
            if (_miniPlayerIsTtsOnly && (_audioService == null || (!_audioService.IsPlaying && !_audioService.IsPaused))) return;

            _isMiniPlayerDragging = true;
            try
            {
                if (MiniPlayerCurrentTimeLabel != null)
                {
                    MiniPlayerCurrentTimeLabel.Text = FormatDuration(TimeSpan.FromSeconds(Math.Max(0, e.NewValue)));
                }
            }
            catch { }
        }

        private async void OnMiniPlayerProgressDragCompleted(object sender, EventArgs e)
        {
            try
            {
                if (MiniPlayerProgressSlider == null) return;
                if (_miniPlayerIsTtsOnly && (_audioService == null || (!_audioService.IsPlaying && !_audioService.IsPaused))) return;

                _isMiniPlayerDragging = false;
                if (_audioService == null) return;

                var target = TimeSpan.FromSeconds(Math.Max(0, MiniPlayerProgressSlider.Value));
                await _audioService.SeekAsync(target);
                await RefreshMiniPlayerUiAsync(forceSliderUpdate: true);
            }
            catch { }
        }
    }
}
