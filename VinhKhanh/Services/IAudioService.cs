using System.Threading.Tasks;

namespace VinhKhanh.Services
{
    public interface IAudioService
    {
        Task PlayAsync(string filePath);
        Task StopAsync();
        bool IsPlaying { get; }
    }
}
