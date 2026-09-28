using System;
using System.Threading.Tasks;
using Windows.Media.SpeechRecognition;

namespace WinAI.Services
{
    /// <summary>
    /// Integrates Windows.Media.SpeechRecognition for native voice-to-text typing on Windows 10 Mobile.
    /// </summary>
    public sealed class VoiceService
    {
        private static readonly Lazy<VoiceService> _instance = new Lazy<VoiceService>(() => new VoiceService());
        public static VoiceService Instance => _instance.Value;

        private SpeechRecognizer _speechRecognizer;
        private bool _isInitialized;

        private VoiceService()
        {
        }

        public async Task<bool> InitializeAsync()
        {
            if (_isInitialized && _speechRecognizer != null)
            {
                return true;
            }

            try
            {
                _speechRecognizer = new SpeechRecognizer();
                var result = await _speechRecognizer.CompileConstraintsAsync();
                _isInitialized = result.Status == SpeechRecognitionResultStatus.Success;
                return _isInitialized;
            }
            catch
            {
                _isInitialized = false;
                return false;
            }
        }

        public async Task<string> RecognizeSpeechAsync()
        {
            bool ready = await InitializeAsync();
            if (!ready) return null;

            try
            {
                var result = await _speechRecognizer.RecognizeAsync();
                if (result.Status == SpeechRecognitionResultStatus.Success && !string.IsNullOrWhiteSpace(result.Text))
                {
                    return result.Text;
                }
            }
            catch
            {
                // Gracefully handle microphone permission / hardware unavailability
            }

            return null;
        }
    }
}
