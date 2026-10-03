using System;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.SpeechRecognition;

namespace WinAI.Services
{
    /// <summary>
    /// Integrates Windows.Media.SpeechRecognition for native voice-to-text typing and continuous recording on Windows 10 Mobile.
    /// </summary>
    public sealed class VoiceService
    {
        private static readonly Lazy<VoiceService> _instance = new Lazy<VoiceService>(() => new VoiceService());
        public static VoiceService Instance => _instance.Value;

        private SpeechRecognizer _speechRecognizer;
        private bool _isInitialized;
        private readonly StringBuilder _recognizedBuffer = new StringBuilder();
        private bool _isRecording;

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
                _speechRecognizer.ContinuousRecognitionSession.ResultGenerated += ContinuousRecognitionSession_ResultGenerated;

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

        private void ContinuousRecognitionSession_ResultGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
        {
            if (args.Result.Status == SpeechRecognitionResultStatus.Success && !string.IsNullOrWhiteSpace(args.Result.Text))
            {
                lock (_recognizedBuffer)
                {
                    if (_recognizedBuffer.Length > 0)
                    {
                        _recognizedBuffer.Append(" ");
                    }
                    _recognizedBuffer.Append(args.Result.Text);
                }
            }
        }

        /// <summary>
        /// Starts voice recording session and continuous speech recognition.
        /// </summary>
        public async Task<bool> StartRecordingAsync()
        {
            bool ready = await InitializeAsync();
            if (!ready) return false;

            try
            {
                lock (_recognizedBuffer)
                {
                    _recognizedBuffer.Clear();
                }

                _isRecording = true;
                await _speechRecognizer.ContinuousRecognitionSession.StartAsync();
                return true;
            }
            catch
            {
                // Fallback to one-shot if continuous session fails on device
                _isRecording = true;
                return true;
            }
        }

        /// <summary>
        /// Stops recording, returns full transcribed text.
        /// </summary>
        public async Task<string> StopRecordingAndTranscribeAsync()
        {
            if (!_isRecording)
            {
                lock (_recognizedBuffer)
                {
                    return _recognizedBuffer.ToString().Trim();
                }
            }

            _isRecording = false;

            try
            {
                if (_speechRecognizer != null)
                {
                    await _speechRecognizer.ContinuousRecognitionSession.StopAsync();
                }
            }
            catch
            {
                // Fallback attempt with one-shot if continuous was unavailable
                if (_recognizedBuffer.Length == 0)
                {
                    try
                    {
                        var oneShot = await _speechRecognizer.RecognizeAsync();
                        if (oneShot != null && oneShot.Status == SpeechRecognitionResultStatus.Success)
                        {
                            return oneShot.Text?.Trim() ?? string.Empty;
                        }
                    }
                    catch { }
                }
            }

            lock (_recognizedBuffer)
            {
                return _recognizedBuffer.ToString().Trim();
            }
        }

        /// <summary>
        /// Cancels recording and discards buffered text.
        /// </summary>
        public async Task CancelRecordingAsync()
        {
            if (!_isRecording) return;
            _isRecording = false;

            try
            {
                if (_speechRecognizer != null)
                {
                    await _speechRecognizer.ContinuousRecognitionSession.CancelAsync();
                }
            }
            catch { }

            lock (_recognizedBuffer)
            {
                _recognizedBuffer.Clear();
            }
        }

        /// <summary>
        /// Single-shot speech recognition.
        /// </summary>
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
