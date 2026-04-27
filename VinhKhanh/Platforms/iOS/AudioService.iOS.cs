using System;
using System.Threading.Tasks;
using AVFoundation;
using Foundation;
using VinhKhanh.Services;

namespace VinhKhanh.Platforms.iOS
{
    public class iOSAudioService : IAudioService
    {
        private AVAudioPlayer _player;
        private bool _isPaused;

        public bool IsPlaying => _player != null && _player.Playing;
        public bool IsPaused => _player != null && _isPaused;
        public TimeSpan Position => _player == null ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Max(0, _player.CurrentTime));
        public TimeSpan Duration => _player == null ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Max(0, _player.Duration));

        public Task PlayAsync(string filePath)
        {
            StopInternal();
            try
            {
                NSUrl url;
                if (Uri.TryCreate(filePath, UriKind.Absolute, out var abs)
                    && (abs.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                        || abs.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
                {
                    url = NSUrl.FromString(filePath);
                }
                else
                {
                    url = NSUrl.FromFilename(filePath);
                }

                _player = AVAudioPlayer.FromUrl(url);
                _player?.PrepareToPlay();
                _player.FinishedPlaying += (s, e) =>
                {
                    _isPaused = false;
                    StopInternal();
                };
                _player.Play();
                _isPaused = false;
            }
            catch (Exception) { }
            return Task.CompletedTask;
        }

        public Task PauseAsync()
        {
            try
            {
                if (_player != null && _player.Playing)
                {
                    _player.Pause();
                    _isPaused = true;
                }
            }
            catch { }

            return Task.CompletedTask;
        }

        public Task ResumeAsync()
        {
            try
            {
                if (_player != null && _isPaused)
                {
                    _player.Play();
                    _isPaused = false;
                }
            }
            catch { }

            return Task.CompletedTask;
        }

        public Task SeekAsync(TimeSpan position)
        {
            try
            {
                if (_player == null) return Task.CompletedTask;
                var target = Math.Max(0, Math.Min(position.TotalSeconds, _player.Duration > 0 ? _player.Duration : double.MaxValue));
                _player.CurrentTime = target;
            }
            catch { }

            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopInternal();
            return Task.CompletedTask;
        }

        private void StopInternal()
        {
            try
            {
                if (_player != null)
                {
                    if (_player.Playing) _player.Stop();
                    _player.Dispose();
                    _player = null;
                    _isPaused = false;
                }
            }
            catch { }
        }
    }
}
