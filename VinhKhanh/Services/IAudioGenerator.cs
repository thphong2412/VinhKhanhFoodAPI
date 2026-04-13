using System.Threading.Tasks;

namespace VinhKhanh.Services
{
    public interface IAudioGenerator
    {
        // Generate TTS audio file for given text and language code, save to outputPath (full path)
        // Return true if generation succeeded and file exists.
        Task<bool> GenerateTtsToFileAsync(string text, string languageCode, string outputPath);
    }
}
