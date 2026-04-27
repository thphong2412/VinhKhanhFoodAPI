using System;
using System.Threading.Tasks;
using Android.Media;
using VinhKhanh.Services;

namespace VinhKhanh.Platforms.Android
{
    public class AndroidAudioService : IAudioService
    {
        private MediaPlayer _player;
        private bool _isPaused;

        public AndroidAudioService() { }

        public bool IsPlaying => _player != null && _player.IsPlaying;
        public bool IsPaused => _player != null && _isPaused;

        public TimeSpan Position
        {
            get
            {
                try
                {
                    if (_player == null) return TimeSpan.Zero;
                    return TimeSpan.FromMilliseconds(Math.Max(0, _player.CurrentPosition));
                }
                catch
                {
                    return TimeSpan.Zero;
                }
            }
        }

        public TimeSpan Duration
        {
            get
            {
                try
                {
                    if (_player == null) return TimeSpan.Zero;
                    return TimeSpan.FromMilliseconds(Math.Max(0, _player.Duration));
                }
                catch
                {
                    return TimeSpan.Zero;
                }
            }
        }

        public Task PlayAsync(string filePath)
        {
            StopInternal();

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("audio_path_empty", nameof(filePath));
            }

            _player = new MediaPlayer();
            _isPaused = false;
            _player.SetAudioStreamType(global::Android.Media.Stream.Music);

            if (Uri.TryCreate(filePath, UriKind.Absolute, out var absoluteUri)
                && (absoluteUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                    || absoluteUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
            {
                _player.SetDataSource(filePath);
            }
            else
            {
                var normalizedPath = filePath;
                if (normalizedPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedPath = normalizedPath.Substring("file://".Length);
                }

                if (!System.IO.File.Exists(normalizedPath))
                {
                    throw new System.IO.FileNotFoundException("audio_file_not_found", normalizedPath);
                }

                _player.SetDataSource(normalizedPath);
            }

            _player.Prepare();
            _player.Start();
            _isPaused = false;

            _player.Completion += (s, e) =>
            {
                _isPaused = false;
                StopInternal();
            };
            return Task.CompletedTask;
        }

        public Task PauseAsync()
        {
            try
            {
                if (_player != null && _player.IsPlaying)
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
                    _player.Start();
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
                var ms = (int)Math.Max(0, Math.Min(position.TotalMilliseconds, _player.Duration > 0 ? _player.Duration : int.MaxValue));
                _player.SeekTo(ms);
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
                    if (_player.IsPlaying) _player.Stop();
                    _player.Reset();
                    _player.Release();
                    _player = null;
                    _isPaused = false;
                }
            }
            catch { }
        }
    }
}
