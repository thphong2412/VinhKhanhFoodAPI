using System;
using System.Threading.Tasks;

namespace VinhKhanh.Services
{
    public class NoOpAudioService : IAudioService
    {
        public bool IsPlaying => false;
        public bool IsPaused => false;
        public TimeSpan Position => TimeSpan.Zero;
        public TimeSpan Duration => TimeSpan.Zero;
        public Task PlayAsync(string filePath) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task PauseAsync() => Task.CompletedTask;
        public Task ResumeAsync() => Task.CompletedTask;
        public Task SeekAsync(TimeSpan position) => Task.CompletedTask;
    }
}
