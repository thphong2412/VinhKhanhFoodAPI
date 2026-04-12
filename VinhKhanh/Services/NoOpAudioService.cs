using System.Threading.Tasks;

namespace VinhKhanh.Services
{
    public class NoOpAudioService : IAudioService
    {
        public bool IsPlaying => false;
        public Task PlayAsync(string filePath) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
    }
}
