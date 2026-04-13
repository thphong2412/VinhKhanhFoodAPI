using System;
using System.IO;
using System.Threading.Tasks;
using Android.Content;
using Android.OS;
using global::Android.Speech.Tts;
using global::Java.Util;
using Microsoft.Maui.Storage;
using VinhKhanh.Services;

namespace VinhKhanh.Platforms.Android
{
    public class TtsFileGenerator : Java.Lang.Object, IAudioGenerator, global::Android.Speech.Tts.TextToSpeech.IOnInitListener
    {
        private global::Android.Speech.Tts.TextToSpeech _tts;
        private TaskCompletionSource<bool> _initTcs;
        private TaskCompletionSource<bool> _synthTcs;
        private Context _context;

        public TtsFileGenerator()
        {
            _context = global::Android.App.Application.Context;
            _initTcs = new TaskCompletionSource<bool>();
            _tts = new global::Android.Speech.Tts.TextToSpeech(_context, this);
        }

        public void OnInit(OperationResult status)
        {
            if (status == OperationResult.Success)
            {
                _initTcs.TrySetResult(true);
            }
            else
            {
                _initTcs.TrySetResult(false);
            }
        }

        private global::Java.Util.Locale MapLocale(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode)) return global::Java.Util.Locale.Default;
            switch (languageCode.ToLowerInvariant())
            {
                case "vi": return new global::Java.Util.Locale("vi", "VN");
                case "en": return global::Java.Util.Locale.English;
                case "ja": return global::Java.Util.Locale.Japanese;
                case "ko": return new global::Java.Util.Locale("ko", "KR");
                default: return global::Java.Util.Locale.Default;
            }
        }

        public async Task<bool> GenerateTtsToFileAsync(string text, string languageCode, string outputPath)
        {
            try
            {
                // Wait for tts to init
                var inited = await _initTcs.Task.ConfigureAwait(false);
                if (!inited) return false;

                var loc = MapLocale(languageCode);
                var res = _tts.SetLanguage(loc);

                // Prepare params bundle for utterance
                var bundle = new Bundle();
                bundle.PutString(global::Android.Speech.Tts.TextToSpeech.Engine.KeyParamUtteranceId, "utt1");

                // Use SynthesizeToFile (available API)
                _synthTcs = new TaskCompletionSource<bool>();

                // Ensure output directory exists
                var dir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // Use SynthesizeToFile with Bundle and Java file (works on modern Android APIs)
                var jfile = new global::Java.IO.File(outputPath);
                _tts.SynthesizeToFile(text, bundle, jfile, "utt1");

                // Register utterance progress listener
                _tts.SetOnUtteranceProgressListener(new UtteranceListener(_synthTcs));

                // wait for completion (with timeout)
                var task = _synthTcs.Task;
                if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20))) == task)
                {
                    return task.Result && System.IO.File.Exists(outputPath);
                }
                else
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private class UtteranceListener : global::Android.Speech.Tts.UtteranceProgressListener
        {
            private TaskCompletionSource<bool> _tcs;
            public UtteranceListener(TaskCompletionSource<bool> tcs) { _tcs = tcs; }
            public override void OnStart(string utteranceId) { }
            public override void OnError(string utteranceId) { try { _tcs.TrySetResult(false); } catch { } }
            public override void OnDone(string utteranceId) { try { _tcs.TrySetResult(true); } catch { } }
        }
    }
}
