using Microsoft.Maui.Media;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace VinhKhanh.Services
{
    public class NarrationService
    {
        private bool _isSpeaking = false;
        private System.Threading.CancellationTokenSource _cts;

        public async Task SpeakAsync(string text, string language = "vi")
        {
            if (string.IsNullOrEmpty(text)) return;

            try { _cts?.Cancel(); } catch { }

            _cts = new System.Threading.CancellationTokenSource();
            var token = _cts.Token;

            if (_isSpeaking) await Task.Delay(50);

            try
            {
                _isSpeaking = true;
                var locales = await TextToSpeech.Default.GetLocalesAsync();
                var locale = locales.FirstOrDefault(l => l.Language.StartsWith(language, StringComparison.OrdinalIgnoreCase));

                await TextToSpeech.Default.SpeakAsync(text, new SpeechOptions
                {
                    Locale = locale,
                    Pitch = 1.0f,
                    Volume = 1.0f
                }, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
            finally
            {
                _isSpeaking = false;
                try { _cts?.Dispose(); } catch { }
                _cts = null;
            }
        }

        public void Stop()
        {
            try { if (_cts != null && !_cts.IsCancellationRequested) _cts.Cancel(); } catch { }
            finally { _isSpeaking = false; try { _cts?.Dispose(); } catch { } _cts = null; }
        }
    }
}
