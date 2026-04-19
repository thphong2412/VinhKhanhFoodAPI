using System.Threading.Tasks;
using Android.Media;
using VinhKhanh.Services;
using Android.Content;
using System;

namespace VinhKhanh.Platforms.Android
{
    public class AndroidAudioService : IAudioService
    {
        private MediaPlayer _player;
        public AndroidAudioService() { }

        public bool IsPlaying => _player != null && _player.IsPlaying;

        public Task PlayAsync(string filePath)
        {
            StopInternal();

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("audio_path_empty", nameof(filePath));
            }

            _player = new MediaPlayer();
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

            _player.Completion += (s, e) => StopInternal();
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
                }
            }
            catch { }
        }
    }
}
