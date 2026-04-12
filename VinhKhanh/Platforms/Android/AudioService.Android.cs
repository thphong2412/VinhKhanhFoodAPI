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

            _player = new MediaPlayer();
            _player.SetAudioStreamType(global::Android.Media.Stream.Music);
            _player.SetDataSource(filePath);
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
