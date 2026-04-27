using System;
using System.Threading.Tasks;

namespace VinhKhanh.Services
{
    public interface IAudioService
    {
        Task PlayAsync(string filePath);
        Task StopAsync();
        Task PauseAsync();
        Task ResumeAsync();
        Task SeekAsync(TimeSpan position);
        bool IsPlaying { get; }
        bool IsPaused { get; }
        TimeSpan Position { get; }
        TimeSpan Duration { get; }
    }
}
