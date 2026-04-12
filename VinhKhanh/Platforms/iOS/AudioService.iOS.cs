using System.Threading.Tasks;
using AVFoundation;
using Foundation;
using VinhKhanh.Services;
using System;

namespace VinhKhanh.Platforms.iOS
{
    public class iOSAudioService : IAudioService
    {
        private AVAudioPlayer _player;

        public bool IsPlaying => _player != null && _player.Playing;

        public Task PlayAsync(string filePath)
        {
            StopInternal();
            try
            {
                var url = NSUrl.FromFilename(filePath);
                _player = AVAudioPlayer.FromUrl(url);
                _player.FinishedPlaying += (s, e) => StopInternal();
                _player.Play();
            }
            catch (Exception) { }
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
                }
            }
            catch { }
        }
    }
}
