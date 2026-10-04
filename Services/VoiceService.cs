using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Globalization;
using Windows.Media.Capture;
using Windows.Media.SpeechRecognition;

namespace WinAI.Services
{
    public enum VoiceRecognitionError
    {
        None,
        PermissionDenied,
        MicrophoneUnavailable,
        LanguageNotInstalled,
        SpeechUnavailable,
        NotUnderstood,
        Unknown
    }

    public sealed class VoiceRecognitionResult
    {
        public bool Success { get; set; }
        public string Text { get; set; } = string.Empty;
        public VoiceRecognitionError Error { get; set; } = VoiceRecognitionError.None;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Native Windows 10 Mobile compatible voice-to-text service utilizing
    /// Windows.Media.SpeechRecognition.SpeechRecognizer with dictation constraints,
    /// live interim hypothesis generation, audio permission probes, and clean lifecycle management.
    /// </summary>
    public sealed class VoiceService
    {
        private static readonly Lazy<VoiceService> _instance = new Lazy<VoiceService>(() => new VoiceService());
        public static VoiceService Instance => _instance.Value;

        private SpeechRecognizer _speechRecognizer;
        private readonly object _bufferLock = new object();
        private readonly StringBuilder _recognizedBuffer = new StringBuilder();
        private string _currentHypothesis = string.Empty;
        private bool _isRecording;
        private bool _isStopping;

        public bool IsRecording => _isRecording;
        public Language CurrentLanguage { get; private set; }

        public event EventHandler<string> HypothesisReceived;
        public event EventHandler<SpeechRecognizerState> StateChanged;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<VoiceRecognitionResult> SessionCompleted;

        private VoiceService()
        {
        }

        /// <summary>
        /// Probes for audio capture hardware availability.
        /// </summary>
        public async Task<bool> CheckMicrophoneHardwareAsync()
        {
            try
            {
                var devices = await DeviceInformation.FindAllAsync(DeviceClass.AudioCapture);
                return devices != null && devices.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Probes for microphone permission using MediaCapture audio initialization.
        /// Returns true if granted, false if denied or unavailable.
        /// </summary>
        public async Task<VoiceRecognitionResult> CheckMicrophonePermissionAsync()
        {
            bool hasHardware = await CheckMicrophoneHardwareAsync();
            if (!hasHardware)
            {
                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.MicrophoneUnavailable,
                    ErrorMessage = "Microphone could not be accessed."
                };
            }

            try
            {
                var settings = new MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = StreamingCaptureMode.Audio,
                    MediaCategory = MediaCategory.Speech
                };

                using (var capture = new MediaCapture())
                {
                    await capture.InitializeAsync(settings);
                }

                return new VoiceRecognitionResult { Success = true };
            }
            catch (UnauthorizedAccessException)
            {
                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.PermissionDenied,
                    ErrorMessage = "Microphone permission is required."
                };
            }
            catch (Exception ex)
            {
                uint hr = (uint)ex.HResult;
                if (hr == 0x80070005) // E_ACCESSDENIED
                {
                    return new VoiceRecognitionResult
                    {
                        Success = false,
                        Error = VoiceRecognitionError.PermissionDenied,
                        ErrorMessage = "Microphone permission is required."
                    };
                }
                else if (hr == 0xC00DABE0 || hr == 0x80070490) // MF_E_NO_CAPTURE_DEVICES_FOUND or Element not found
                {
                    return new VoiceRecognitionResult
                    {
                        Success = false,
                        Error = VoiceRecognitionError.MicrophoneUnavailable,
                        ErrorMessage = "Microphone could not be accessed."
                    };
                }

                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.MicrophoneUnavailable,
                    ErrorMessage = "Microphone could not be accessed."
                };
            }
        }

        /// <summary>
        /// Resolves the best available speech recognition language based on
        /// the application's configured language and system speech language fallback.
        /// </summary>
        public Language ResolveSpeechLanguage()
        {
            var supported = SpeechRecognizer.SupportedTopicLanguages;
            if (supported == null || supported.Count == 0)
            {
                return null;
            }

            // 1. Try application's configured language
            string configuredLang = AppSettingsService.Instance.GetString(AppSettingsService.SettingLanguageKey, "English");
            if (!string.IsNullOrWhiteSpace(configuredLang))
            {
                foreach (var lang in supported)
                {
                    if (lang.DisplayName.IndexOf(configuredLang, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        lang.LanguageTag.IndexOf(configuredLang, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return lang;
                    }
                }

                // If "English", prefer any "en-" variant
                if (configuredLang.IndexOf("English", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foreach (var lang in supported)
                    {
                        if (lang.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                        {
                            return lang;
                        }
                    }
                }
            }

            // 2. Try system speech language
            var systemLang = SpeechRecognizer.SystemSpeechLanguage;
            if (systemLang != null)
            {
                foreach (var lang in supported)
                {
                    if (lang.LanguageTag.Equals(systemLang.LanguageTag, StringComparison.OrdinalIgnoreCase))
                    {
                        return lang;
                    }
                }
            }

            // 3. Fallback to first supported topic language
            return supported[0];
        }

        /// <summary>
        /// Starts voice recording and continuous speech recognition session.
        /// </summary>
        public async Task<VoiceRecognitionResult> StartRecordingAsync()
        {
            if (_isRecording)
            {
                await CancelRecordingAsync();
            }

            // Step 1: Check microphone hardware and permission
            var permissionCheck = await CheckMicrophonePermissionAsync();
            if (!permissionCheck.Success)
            {
                return permissionCheck;
            }

            // Step 2: Resolve speech language
            var targetLanguage = ResolveSpeechLanguage();
            if (targetLanguage == null)
            {
                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.LanguageNotInstalled,
                    ErrorMessage = "Speech recognition language is not installed."
                };
            }
            CurrentLanguage = targetLanguage;

            // Step 3: Initialize and compile SpeechRecognizer
            await CleanupRecognizerAsync();

            try
            {
                _speechRecognizer = new SpeechRecognizer(targetLanguage);
                _speechRecognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));

                _speechRecognizer.HypothesisGenerated += SpeechRecognizer_HypothesisGenerated;
                _speechRecognizer.StateChanged += SpeechRecognizer_StateChanged;
                _speechRecognizer.ContinuousRecognitionSession.ResultGenerated += ContinuousRecognitionSession_ResultGenerated;
                _speechRecognizer.ContinuousRecognitionSession.Completed += ContinuousRecognitionSession_Completed;

                var compilationResult = await _speechRecognizer.CompileConstraintsAsync();
                if (compilationResult.Status != SpeechRecognitionResultStatus.Success)
                {
                    await CleanupRecognizerAsync();
                    return new VoiceRecognitionResult
                    {
                        Success = false,
                        Error = VoiceRecognitionError.SpeechUnavailable,
                        ErrorMessage = "Speech recognition is unavailable."
                    };
                }
            }
            catch (UnauthorizedAccessException)
            {
                await CleanupRecognizerAsync();
                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.PermissionDenied,
                    ErrorMessage = "Microphone permission is required."
                };
            }
            catch (Exception ex)
            {
                await CleanupRecognizerAsync();
                uint hr = (uint)ex.HResult;
                if (hr == 0x80045509 || hr == 0x8004503A || hr == 0x80070005)
                {
                    return new VoiceRecognitionResult
                    {
                        Success = false,
                        Error = VoiceRecognitionError.PermissionDenied,
                        ErrorMessage = "Microphone permission is required."
                    };
                }

                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.SpeechUnavailable,
                    ErrorMessage = "Speech recognition is unavailable."
                };
            }

            // Step 4: Start Continuous Recognition Session
            try
            {
                lock (_bufferLock)
                {
                    _recognizedBuffer.Clear();
                    _currentHypothesis = string.Empty;
                }

                _isStopping = false;
                _isRecording = true;

                StatusChanged?.Invoke(this, "Listening...");
                await _speechRecognizer.ContinuousRecognitionSession.StartAsync();

                return new VoiceRecognitionResult { Success = true };
            }
            catch (Exception ex)
            {
                _isRecording = false;
                await CleanupRecognizerAsync();

                uint hr = (uint)ex.HResult;
                if (hr == 0x80045509 || hr == 0x8004503A || hr == 0x80070005)
                {
                    return new VoiceRecognitionResult
                    {
                        Success = false,
                        Error = VoiceRecognitionError.PermissionDenied,
                        ErrorMessage = "Microphone permission is required."
                    };
                }

                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.SpeechUnavailable,
                    ErrorMessage = "Speech recognition is unavailable."
                };
            }
        }

        private void SpeechRecognizer_HypothesisGenerated(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
        {
            if (!_isRecording || _isStopping) return;

            string hypothesis = args.Hypothesis?.Text ?? string.Empty;
            string combinedText;

            lock (_bufferLock)
            {
                _currentHypothesis = hypothesis;
                combinedText = BuildCurrentTranscription();
            }

            HypothesisReceived?.Invoke(this, combinedText);
        }

        private void SpeechRecognizer_StateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
        {
            StateChanged?.Invoke(this, args.State);

            switch (args.State)
            {
                case SpeechRecognizerState.Capturing:
                    StatusChanged?.Invoke(this, "Listening...");
                    break;
                case SpeechRecognizerState.SpeechDetected:
                case SpeechRecognizerState.SoundStarted:
                    StatusChanged?.Invoke(this, "Listening...");
                    break;
                case SpeechRecognizerState.Processing:
                    StatusChanged?.Invoke(this, "Processing...");
                    break;
                case SpeechRecognizerState.Idle:
                    if (_isRecording && !_isStopping)
                    {
                        StatusChanged?.Invoke(this, "Listening...");
                    }
                    break;
            }
        }

        private void ContinuousRecognitionSession_ResultGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
        {
            if (args.Result != null && args.Result.Status == SpeechRecognitionResultStatus.Success && !string.IsNullOrWhiteSpace(args.Result.Text))
            {
                string combinedText;
                lock (_bufferLock)
                {
                    if (_recognizedBuffer.Length > 0)
                    {
                        _recognizedBuffer.Append(" ");
                    }
                    _recognizedBuffer.Append(args.Result.Text.Trim());
                    _currentHypothesis = string.Empty;
                    combinedText = _recognizedBuffer.ToString().Trim();
                }

                HypothesisReceived?.Invoke(this, combinedText);
            }
        }

        private void ContinuousRecognitionSession_Completed(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
        {
            if (!_isRecording) return;

            // Session completed by timeout or external event
            string finalText = GetCurrentTranscribedText();
            bool hasText = !string.IsNullOrWhiteSpace(finalText);

            var result = new VoiceRecognitionResult
            {
                Success = hasText,
                Text = finalText,
                Error = hasText ? VoiceRecognitionError.None : VoiceRecognitionError.NotUnderstood,
                ErrorMessage = hasText ? string.Empty : "Could not understand the recording."
            };

            _isRecording = false;
            SessionCompleted?.Invoke(this, result);
        }

        /// <summary>
        /// Stops recording, compiles and returns the full transcribed text.
        /// Releases recognizer session cleanly.
        /// </summary>
        public async Task<VoiceRecognitionResult> StopRecordingAndTranscribeAsync()
        {
            if (!_isRecording && string.IsNullOrWhiteSpace(GetCurrentTranscribedText()))
            {
                return new VoiceRecognitionResult
                {
                    Success = false,
                    Text = string.Empty,
                    Error = VoiceRecognitionError.NotUnderstood,
                    ErrorMessage = "Could not understand the recording."
                };
            }

            _isStopping = true;
            StatusChanged?.Invoke(this, "Transcribing...");

            try
            {
                if (_speechRecognizer != null && _speechRecognizer.ContinuousRecognitionSession != null)
                {
                    await _speechRecognizer.ContinuousRecognitionSession.StopAsync();
                }
            }
            catch
            {
                // Ignored - session may already be stopping
            }

            _isRecording = false;
            _isStopping = false;

            string finalText = GetCurrentTranscribedText();
            await CleanupRecognizerAsync();

            if (string.IsNullOrWhiteSpace(finalText))
            {
                return new VoiceRecognitionResult
                {
                    Success = false,
                    Text = string.Empty,
                    Error = VoiceRecognitionError.NotUnderstood,
                    ErrorMessage = "Could not understand the recording."
                };
            }

            return new VoiceRecognitionResult
            {
                Success = true,
                Text = finalText
            };
        }

        /// <summary>
        /// Cancels recording session, discards any recognized speech and cleans up recognizer.
        /// </summary>
        public async Task CancelRecordingAsync()
        {
            if (!_isRecording && _speechRecognizer == null)
            {
                lock (_bufferLock)
                {
                    _recognizedBuffer.Clear();
                    _currentHypothesis = string.Empty;
                }
                return;
            }

            _isStopping = true;
            _isRecording = false;

            try
            {
                if (_speechRecognizer != null && _speechRecognizer.ContinuousRecognitionSession != null)
                {
                    await _speechRecognizer.ContinuousRecognitionSession.CancelAsync();
                }
            }
            catch
            {
                // Ignored
            }

            lock (_bufferLock)
            {
                _recognizedBuffer.Clear();
                _currentHypothesis = string.Empty;
            }

            _isStopping = false;
            await CleanupRecognizerAsync();
        }

        private string BuildCurrentTranscription()
        {
            string bufferText = _recognizedBuffer.ToString().Trim();
            string hyp = _currentHypothesis.Trim();

            if (string.IsNullOrEmpty(bufferText))
            {
                return hyp;
            }
            if (string.IsNullOrEmpty(hyp))
            {
                return bufferText;
            }

            if (!bufferText.EndsWith(hyp, StringComparison.OrdinalIgnoreCase))
            {
                return $"{bufferText} {hyp}".Trim();
            }

            return bufferText;
        }

        public string GetCurrentTranscribedText()
        {
            lock (_bufferLock)
            {
                return BuildCurrentTranscription();
            }
        }

        /// <summary>
        /// Unsubscribes all event handlers and disposes the speech recognizer
        /// to ensure no resource leaks or orphaned audio sessions.
        /// </summary>
        public async Task CleanupRecognizerAsync()
        {
            try
            {
                if (_speechRecognizer != null)
                {
                    _speechRecognizer.HypothesisGenerated -= SpeechRecognizer_HypothesisGenerated;
                    _speechRecognizer.StateChanged -= SpeechRecognizer_StateChanged;

                    if (_speechRecognizer.ContinuousRecognitionSession != null)
                    {
                        _speechRecognizer.ContinuousRecognitionSession.ResultGenerated -= ContinuousRecognitionSession_ResultGenerated;
                        _speechRecognizer.ContinuousRecognitionSession.Completed -= ContinuousRecognitionSession_Completed;
                    }

                    _speechRecognizer.Dispose();
                    _speechRecognizer = null;
                }
            }
            catch
            {
                _speechRecognizer = null;
            }

            await Task.CompletedTask;
        }
    }
}
