using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Globalization;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.SpeechRecognition;
using Windows.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WinAI.Services
{
    public enum VoiceRecognitionError
    {
        None,
        PermissionDenied,
        PrivacyPolicyNotAccepted,
        MicrophoneUnavailable,
        LanguageNotInstalled,
        SpeechUnavailable,
        AudioSystemError,
        NotUnderstood,
        NetworkFailure,
        AudioQualityFailure,
        Unknown
    }

    public sealed class VoiceRecognitionResult
    {
        public bool Success { get; set; }
        public string Text { get; set; } = string.Empty;
        public VoiceRecognitionError Error { get; set; } = VoiceRecognitionError.None;
        public string ErrorMessage { get; set; } = string.Empty;
        public string SettingsUri { get; set; } = string.Empty;
    }

    /// <summary>
    /// Windows 10 Mobile voice-to-text service using SpeechRecognizer with continuous dictation.
    /// NOTE: all events (HypothesisReceived, StatusChanged, SessionCompleted...) fire on a
    /// BACKGROUND thread. Use Dispatcher.RunAsync in your page/viewmodel before touching UI.
    /// </summary>
    public sealed class VoiceService
    {
        private const uint HR_PRIVACY_POLICY_NOT_ACCEPTED = 0x80045509;
        private const uint HR_ACCESS_DENIED = 0x80070005;
        private const uint HR_AUDIO_SYS_ERROR = 0x8004503A;

        private static readonly Lazy<VoiceService> _instance = new Lazy<VoiceService>(() => new VoiceService());
        public static VoiceService Instance => _instance.Value;

        private SpeechRecognizer _speechRecognizer;
        private Windows.Foundation.IAsyncOperation<SpeechRecognitionResult> _currentRecognitionOperation;
        private MediaCapture _mediaCapture;
        private StorageFile _currentAudioFile;
        private readonly ApiKeyService _apiKeyService = ApiKeyService.Instance;
        private readonly object _bufferLock = new object();
        private readonly StringBuilder _recognizedBuffer = new StringBuilder();
        private string _currentHypothesis = string.Empty;
        private string _latestTranscribedText = string.Empty;
        private volatile bool _isRecording;
        private volatile bool _isStopping;

        // Used so Stop can WAIT for the engine to really finish (final result arrives before Completed)
        private TaskCompletionSource<bool> _sessionCompletedTcs;
        private SpeechRecognitionResultStatus _lastCompletionStatus = SpeechRecognitionResultStatus.Success;

        public bool IsRecording => _isRecording;
        public Language CurrentLanguage { get; private set; }

        public event EventHandler<string> HypothesisReceived;
        public event EventHandler<SpeechRecognizerState> StateChanged;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<VoiceRecognitionResult> SessionCompleted;

        private VoiceService()
        {
        }

        #region Modern AI Speech Recognition (MediaCapture + Whisper / Gemini)

        /// <summary>
        /// Starts microphone audio recording using MediaCapture to an M4A file in TemporaryFolder.
        /// This works natively on both Windows 10 Mobile and Windows 10/11 without Cortana or speech packs.
        /// </summary>
        public async Task<VoiceRecognitionResult> StartAudioRecordingAsync()
        {
            try
            {
                bool hasHardware = await CheckMicrophoneHardwareAsync();
                if (!hasHardware)
                {
                    return new VoiceRecognitionResult
                    {
                        Success = false,
                        Error = VoiceRecognitionError.MicrophoneUnavailable,
                        ErrorMessage = "Microphone hardware was not detected."
                    };
                }

                if (_mediaCapture != null)
                {
                    await CancelAudioRecordingAsync();
                }

                _mediaCapture = new MediaCapture();
                var settings = new MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = StreamingCaptureMode.Audio
                };
                await _mediaCapture.InitializeAsync(settings);

                var tempFolder = ApplicationData.Current.TemporaryFolder;
                _currentAudioFile = await tempFolder.CreateFileAsync("winai_voice_prompt.m4a", CreationCollisionOption.ReplaceExisting);

                var profile = MediaEncodingProfile.CreateM4a(AudioEncodingQuality.Medium);
                await _mediaCapture.StartRecordToStorageFileAsync(profile, _currentAudioFile);

                _isRecording = true;
                _isStopping = false;
                StatusChanged?.Invoke(this, "Listening...");
                return new VoiceRecognitionResult { Success = true };
            }
            catch (UnauthorizedAccessException)
            {
                _isRecording = false;
                return PermissionDeniedResult();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Voice] StartAudioRecordingAsync exception: {ex.Message}");
                _isRecording = false;
                return MapException(ex, "Microphone recording could not be started.");
            }
        }

        /// <summary>
        /// Stops audio recording, reads recorded M4A audio bytes, and transcribes them using AI
        /// prioritizing the currently active/selected provider and tested available models.
        /// </summary>
        public async Task<VoiceRecognitionResult> StopAudioRecordingAndTranscribeAsync(string selectedProviderId = null, string selectedModelId = null)
        {
            if (!_isRecording || _mediaCapture == null || _currentAudioFile == null)
            {
                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.NotUnderstood,
                    ErrorMessage = "No active recording found."
                };
            }

            _isRecording = false;
            _isStopping = true;
            StatusChanged?.Invoke(this, "Transcribing with AI...");

            byte[] audioBytes = null;
            try
            {
                await _mediaCapture.StopRecordAsync();
                _mediaCapture.Dispose();
                _mediaCapture = null;

                using (var stream = await _currentAudioFile.OpenReadAsync())
                using (var netStream = stream.AsStreamForRead())
                using (var ms = new MemoryStream())
                {
                    await netStream.CopyToAsync(ms);
                    audioBytes = ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Voice] Finalize recording error: {ex.Message}");
                if (_mediaCapture != null)
                {
                    try { _mediaCapture.Dispose(); } catch { }
                    _mediaCapture = null;
                }
                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.AudioQualityFailure,
                    ErrorMessage = "Could not finalize audio recording."
                };
            }
            finally
            {
                _isStopping = false;
            }

            if (audioBytes == null || audioBytes.Length == 0)
            {
                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.NotUnderstood,
                    ErrorMessage = "Audio recording was empty."
                };
            }

            return await TranscribeAudioBytesAsync(audioBytes, selectedProviderId, selectedModelId);
        }

        /// <summary>
        /// Cancels active audio recording and deletes temporary audio file.
        /// </summary>
        public async Task CancelAudioRecordingAsync()
        {
            _isRecording = false;
            _isStopping = false;

            try
            {
                if (_mediaCapture != null)
                {
                    try { await _mediaCapture.StopRecordAsync(); } catch { }
                    _mediaCapture.Dispose();
                    _mediaCapture = null;
                }

                if (_currentAudioFile != null)
                {
                    try { await _currentAudioFile.DeleteAsync(StorageDeleteOption.PermanentDelete); } catch { }
                    _currentAudioFile = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Voice] CancelAudioRecordingAsync error: {ex.Message}");
            }
        }

        /// <summary>
        /// Dispatches recorded audio bytes to modern AI speech-to-text endpoints.
        /// Dynamically prioritizes the user's selected provider and models that have been tested and cached.
        /// </summary>
        public async Task<VoiceRecognitionResult> TranscribeAudioBytesAsync(byte[] audioBytes, string preferredProviderId = null, string preferredModelId = null)
        {
            string normProv = preferredProviderId?.Trim()?.ToLowerInvariant() ?? "";

            // 1. If user's active/selected provider is Google Gemini, try Gemini with their active/tested models first!
            if (normProv.Contains("gemini") || normProv.Contains("google"))
            {
                var geminiResult = await TryGeminiTranscriptionAsync(preferredModelId, audioBytes);
                if (geminiResult != null) return geminiResult;
            }
            // 2. If user's active/selected provider is Groq, try Groq first
            else if (normProv.Contains("groq"))
            {
                var groqResult = await TryGroqTranscriptionAsync(audioBytes);
                if (groqResult != null) return groqResult;
            }
            // 3. If user's active/selected provider is OpenAI, try OpenAI first
            else if (normProv.Contains("openai"))
            {
                var openAiResult = await TryOpenAiTranscriptionAsync(preferredModelId, audioBytes);
                if (openAiResult != null) return openAiResult;
            }

            // Fallback to whichever configured audio transcription key is available:
            // Priority A: Google Gemini (using the tested & available Gemini models)
            var fGemini = await TryGeminiTranscriptionAsync(preferredModelId, audioBytes);
            if (fGemini != null) return fGemini;

            // Priority B: Groq Whisper (Ultra-fast)
            var fGroq = await TryGroqTranscriptionAsync(audioBytes);
            if (fGroq != null) return fGroq;

            // Priority C: OpenAI Whisper
            var fOpenAi = await TryOpenAiTranscriptionAsync(preferredModelId, audioBytes);
            if (fOpenAi != null) return fOpenAi;

            // If no supported audio AI keys are configured
            return new VoiceRecognitionResult
            {
                Success = false,
                Error = VoiceRecognitionError.SpeechUnavailable,
                ErrorMessage = "Voice transcription requires an API key (Gemini, Groq, or OpenAI). Please configure one in Key Vault.",
                SettingsUri = "ms-settings:privacy-speech"
            };
        }

        private async Task<VoiceRecognitionResult> TryGeminiTranscriptionAsync(string preferredModelId, byte[] audioBytes)
        {
            string geminiKey = _apiKeyService.GeminiKey;
            if (string.IsNullOrWhiteSpace(geminiKey)) return null;

            try
            {
                var candidates = new List<string>();

                // A. User's currently selected model if it's a Gemini model
                if (!string.IsNullOrWhiteSpace(preferredModelId) &&
                    (preferredModelId.IndexOf("gemini", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     preferredModelId.IndexOf("gemma", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    candidates.Add(preferredModelId);
                }

                // B. Fetch models that were tested and cached for Gemini
                var availableModels = ModelService.Instance.GetModelsForProvider("gemini");
                if (availableModels != null && availableModels.Count > 0)
                {
                    // Prefer Flash models first (best for audio & rate limits)
                    foreach (var m in availableModels)
                    {
                        if (m.Id.IndexOf("flash", StringComparison.OrdinalIgnoreCase) >= 0 && !candidates.Contains(m.Id))
                        {
                            candidates.Add(m.Id);
                        }
                    }

                    // Add any other available Gemini models (excluding TTS/Image-only)
                    foreach (var m in availableModels)
                    {
                        if (!candidates.Contains(m.Id) &&
                            m.Id.IndexOf("image", StringComparison.OrdinalIgnoreCase) < 0 &&
                            m.Id.IndexOf("tts", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            candidates.Add(m.Id);
                        }
                    }
                }

                // C. Fallbacks
                if (!candidates.Contains("gemini-2.5-flash")) candidates.Add("gemini-2.5-flash");
                if (!candidates.Contains("gemini-flash-latest")) candidates.Add("gemini-flash-latest");
                if (!candidates.Contains("gemini-2.5-flash-lite")) candidates.Add("gemini-2.5-flash-lite");
                if (!candidates.Contains("gemini-2.5-pro")) candidates.Add("gemini-2.5-pro");

                // Try candidates in order until one succeeds
                foreach (var candidateModel in candidates)
                {
                    try
                    {
                        StatusChanged?.Invoke(this, $"Transcribing with Gemini ({candidateModel})...");
                        string text = await TranscribeWithGeminiAsync(geminiKey, candidateModel, audioBytes);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return new VoiceRecognitionResult { Success = true, Text = text.Trim() };
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Voice] Gemini candidate '{candidateModel}' failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Voice] Gemini transcription error: {ex.Message}");
            }

            return null;
        }

        private async Task<VoiceRecognitionResult> TryGroqTranscriptionAsync(byte[] audioBytes)
        {
            string groqKey = _apiKeyService.GroqKey;
            if (string.IsNullOrWhiteSpace(groqKey)) return null;

            try
            {
                StatusChanged?.Invoke(this, "Transcribing with Groq Whisper...");
                string text = await TranscribeWithWhisperAsync(
                    "https://api.groq.com/openai/v1/audio/transcriptions",
                    groqKey,
                    "whisper-large-v3-turbo",
                    audioBytes);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    return new VoiceRecognitionResult { Success = true, Text = text.Trim() };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Voice] Groq Whisper error: {ex.Message}");
            }
            return null;
        }

        private async Task<VoiceRecognitionResult> TryOpenAiTranscriptionAsync(string preferredModelId, byte[] audioBytes)
        {
            string openAiKey = _apiKeyService.OpenAiKey;
            if (string.IsNullOrWhiteSpace(openAiKey)) return null;

            try
            {
                StatusChanged?.Invoke(this, "Transcribing with OpenAI...");
                string model = "whisper-1";
                if (!string.IsNullOrWhiteSpace(preferredModelId) && preferredModelId.Contains("transcribe"))
                {
                    model = preferredModelId;
                }

                string text = await TranscribeWithWhisperAsync(
                    "https://api.openai.com/v1/audio/transcriptions",
                    openAiKey,
                    model,
                    audioBytes);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    return new VoiceRecognitionResult { Success = true, Text = text.Trim() };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Voice] OpenAI transcription error: {ex.Message}");
            }
            return null;
        }

        private static async Task<string> TranscribeWithWhisperAsync(string endpoint, string apiKey, string model, byte[] audioBytes)
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                using (var content = new MultipartFormDataContent())
                {
                    var fileContent = new ByteArrayContent(audioBytes);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/m4a");
                    content.Add(fileContent, "file", "voice.m4a");
                    content.Add(new StringContent(model), "model");

                    using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                        request.Content = content;

                        var response = await client.SendAsync(request);
                        string responseBody = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            Debug.WriteLine($"[Voice] Whisper HTTP {(int)response.StatusCode}: {responseBody}");
                            return null;
                        }

                        var jobj = JObject.Parse(responseBody);
                        return jobj["text"]?.ToString();
                    }
                }
            }
        }

        private static async Task<string> TranscribeWithGeminiAsync(string apiKey, string modelId, byte[] audioBytes)
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                string cleanModel = modelId?.Trim() ?? "";
                if (cleanModel.StartsWith("models/")) cleanModel = cleanModel.Substring(7);
                if (string.IsNullOrWhiteSpace(cleanModel)) cleanModel = "gemini-2.5-flash";

                string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{cleanModel}:generateContent?key={apiKey.Trim()}";

                string base64Audio = Convert.ToBase64String(audioBytes);

                var requestObj = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = "Transcribe the spoken audio verbatim. Output only the transcribed text, nothing else. Do not wrap in quotes." },
                                new
                                {
                                    inline_data = new
                                    {
                                        mime_type = "audio/mp4",
                                        data = base64Audio
                                    }
                                }
                            }
                        }
                    }
                };

                string json = JsonConvert.SerializeObject(requestObj);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    var response = await client.PostAsync(endpoint, content);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine($"[Voice] Gemini HTTP {(int)response.StatusCode} for model '{cleanModel}': {responseBody}");
                        return null;
                    }

                    var jobj = JObject.Parse(responseBody);
                    var candidates = jobj["candidates"] as JArray;
                    var text = candidates?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                    return text?.Trim();
                }
            }
        }

        #endregion

        #region Helpers

        private static VoiceRecognitionResult MapException(Exception ex, string fallbackMessage)
        {
            if (ex is UnauthorizedAccessException)
            {
                return PermissionDeniedResult();
            }

            uint hr = (uint)ex.HResult;
            Debug.WriteLine($"[Voice] Exception HR=0x{hr:X8} Msg={ex.Message}");

            if (hr == HR_PRIVACY_POLICY_NOT_ACCEPTED)
            {
                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.PrivacyPolicyNotAccepted,
                    ErrorMessage = "Online speech is off. Tap to open Settings.",
                    SettingsUri = "ms-settings:privacy-speech"
                };
            }
            if (hr == HR_ACCESS_DENIED)
            {
                return PermissionDeniedResult();
            }
            if (hr == HR_AUDIO_SYS_ERROR)
            {
                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.AudioSystemError,
                    ErrorMessage = "Audio device is busy. Please try again."
                };
            }

            return new VoiceRecognitionResult
            {
                Success = false,
                Error = VoiceRecognitionError.SpeechUnavailable,
                ErrorMessage = fallbackMessage
            };
        }

        private static VoiceRecognitionResult PermissionDeniedResult()
        {
            return new VoiceRecognitionResult
            {
                Success = false,
                Error = VoiceRecognitionError.PermissionDenied,
                ErrorMessage = "Microphone access is denied. Tap to open Settings.",
                SettingsUri = "ms-settings:privacy-microphone"
            };
        }

        private static VoiceRecognitionResult MapCompletionStatus(SpeechRecognitionResultStatus status)
        {
            switch (status)
            {
                case SpeechRecognitionResultStatus.NetworkFailure:
                    return new VoiceRecognitionResult
                    {
                        Success = false,
                        Error = VoiceRecognitionError.NetworkFailure,
                        ErrorMessage = "Network error. Please check your internet connection."
                    };

                case SpeechRecognitionResultStatus.TopicLanguageNotSupported:
                case SpeechRecognitionResultStatus.GrammarLanguageMismatch:
                    return new VoiceRecognitionResult
                    {
                        Success = false,
                        Error = VoiceRecognitionError.LanguageNotInstalled,
                        ErrorMessage = "Speech language is not supported or installed. Tap to open Settings.",
                        SettingsUri = "ms-settings:speech"
                    };

                case SpeechRecognitionResultStatus.AudioQualityFailure:
                    return new VoiceRecognitionResult
                    {
                        Success = false,
                        Error = VoiceRecognitionError.AudioQualityFailure,
                        ErrorMessage = "Audio quality was too poor. Please try again."
                    };

                case SpeechRecognitionResultStatus.MicrophoneUnavailable:
                    return new VoiceRecognitionResult
                    {
                        Success = false,
                        Error = VoiceRecognitionError.MicrophoneUnavailable,
                        ErrorMessage = "Microphone is unavailable."
                    };

                case SpeechRecognitionResultStatus.TimeoutExceeded:
                case SpeechRecognitionResultStatus.PauseLimitExceeded:
                default:
                    return new VoiceRecognitionResult
                    {
                        Success = false,
                        Error = VoiceRecognitionError.NotUnderstood,
                        ErrorMessage = "Could not understand the recording."
                    };
            }
        }

        private static bool IsFatalHr(Exception ex)
        {
            if (ex is UnauthorizedAccessException) return true;
            uint hr = (uint)ex.HResult;
            return hr == HR_PRIVACY_POLICY_NOT_ACCEPTED || hr == HR_ACCESS_DENIED;
        }

        /// <summary>
        /// Tries to compile the recognizer with the given constraint (or no constraint if null).
        /// Permission/privacy errors are rethrown, everything else just returns false.
        /// </summary>
        private async Task<bool> TryCompileAsync(SpeechRecognizer recognizer, ISpeechRecognitionConstraint constraint, string label)
        {
            try
            {
                recognizer.Constraints.Clear();
                if (constraint != null)
                {
                    recognizer.Constraints.Add(constraint);
                }

                var comp = await recognizer.CompileConstraintsAsync();
                Debug.WriteLine($"[Voice] Compile '{label}' => {comp.Status}");
                return comp.Status == SpeechRecognitionResultStatus.Success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Voice] Compile '{label}' threw HR=0x{(uint)ex.HResult:X8} {ex.Message}");
                if (IsFatalHr(ex))
                {
                    throw;
                }
                return false;
            }
        }

        #endregion

        /// <summary>
        /// Probes for audio capture hardware availability without locking the audio driver.
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
                // Don't block if enumeration fails
                return true;
            }
        }

        private static Language FindLanguage(IEnumerable<Language> languages, string configured)
        {
            if (languages == null || string.IsNullOrWhiteSpace(configured)) return null;

            // Exact / partial match on name or tag
            foreach (var lang in languages)
            {
                if (lang.DisplayName.IndexOf(configured, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    lang.LanguageTag.IndexOf(configured, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return lang;
                }
            }

            // "English" => any en-* variant
            if (configured.IndexOf("English", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                foreach (var lang in languages)
                {
                    if (lang.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                    {
                        return lang;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves the speech language. The phone's SystemSpeechLanguage is preferred whenever it
        /// is compatible with the app's configured language, because that pack is guaranteed installed.
        /// </summary>
        public Language ResolveSpeechLanguage()
        {
            try
            {
                var systemLang = SpeechRecognizer.SystemSpeechLanguage;
                var supportedTopic = SpeechRecognizer.SupportedTopicLanguages;
                var supportedGrammar = SpeechRecognizer.SupportedGrammarLanguages;

                string configuredLang = AppSettingsService.Instance.GetString(AppSettingsService.SettingLanguageKey, "English");

                // 1. System language matches what the app wants (or app has no preference) -> use it
                if (systemLang != null)
                {
                    if (string.IsNullOrWhiteSpace(configuredLang))
                    {
                        return systemLang;
                    }

                    bool systemMatches =
                        systemLang.DisplayName.IndexOf(configuredLang, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        systemLang.LanguageTag.IndexOf(configuredLang, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (configuredLang.IndexOf("English", StringComparison.OrdinalIgnoreCase) >= 0 &&
                         systemLang.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));

                    if (systemMatches)
                    {
                        return systemLang;
                    }
                }

                // 2. App wants a different language than the system one -> search supported lists
                var found = FindLanguage(supportedTopic, configuredLang) ?? FindLanguage(supportedGrammar, configuredLang);
                if (found != null)
                {
                    return found;
                }

                // 3. Fallbacks
                if (systemLang != null)
                {
                    return systemLang;
                }
                if (supportedTopic != null && supportedTopic.Count > 0)
                {
                    return supportedTopic[0];
                }
                if (supportedGrammar != null && supportedGrammar.Count > 0)
                {
                    return supportedGrammar[0];
                }

                return null;
            }
            catch
            {
                return null;
            }
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

            // Step 1: Check microphone hardware
            bool hasHardware = await CheckMicrophoneHardwareAsync();
            if (!hasHardware)
            {
                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.MicrophoneUnavailable,
                    ErrorMessage = "Microphone hardware was not detected."
                };
            }

            // Step 2: Resolve speech language
            var targetLanguage = ResolveSpeechLanguage();
            CurrentLanguage = targetLanguage ?? SpeechRecognizer.SystemSpeechLanguage;
            Debug.WriteLine($"[Voice] Using language: {CurrentLanguage?.LanguageTag}");

            // Step 3: Initialize and compile SpeechRecognizer
            await CleanupRecognizerAsync();

            try
            {
                _speechRecognizer = targetLanguage != null
                    ? new SpeechRecognizer(targetLanguage)
                    : new SpeechRecognizer();

                _speechRecognizer.HypothesisGenerated += SpeechRecognizer_HypothesisGenerated;
                _speechRecognizer.StateChanged += SpeechRecognizer_StateChanged;
                _speechRecognizer.ContinuousRecognitionSession.ResultGenerated += ContinuousRecognitionSession_ResultGenerated;
                _speechRecognizer.ContinuousRecognitionSession.Completed += ContinuousRecognitionSession_Completed;

                // Tiered constraint compilation:
                // 1. Dictation topic constraint (Free-form text dictation)
                // 2. WebSearch topic constraint (Conversational queries fallback)
                // 3. No constraints (Fallback only)
                bool compiled =
                    await TryCompileAsync(_speechRecognizer,
                        new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"),
                        "Dictation")
                    ||
                    await TryCompileAsync(_speechRecognizer,
                        new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.WebSearch, "websearch"),
                        "WebSearch")
                    ||
                    await TryCompileAsync(_speechRecognizer, null, "Default");

                if (!compiled)
                {
                    await CleanupRecognizerAsync();
                    return new VoiceRecognitionResult
                    {
                        Success = false,
                        Error = VoiceRecognitionError.SpeechUnavailable,
                        ErrorMessage = "Speech recognition could not be initialized."
                    };
                }

                // Generous timeouts so pauses or short thinking don't prematurely abort recording
                try
                {
                    _speechRecognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(15);
                    _speechRecognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(5);
                    _speechRecognizer.Timeouts.BabbleTimeout = TimeSpan.FromSeconds(30);
                }
                catch
                {
                }

                // By default a continuous session auto-stops after ~5s of silence
                try
                {
                    _speechRecognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromSeconds(20);
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                await CleanupRecognizerAsync();
                return MapException(ex, "Speech recognition is unavailable.");
            }

            // Step 4: Start Continuous Recognition Session
            try
            {
                lock (_bufferLock)
                {
                    _recognizedBuffer.Clear();
                    _currentHypothesis = string.Empty;
                    _latestTranscribedText = string.Empty;
                }

                _lastCompletionStatus = SpeechRecognitionResultStatus.Success;
                _sessionCompletedTcs = new TaskCompletionSource<bool>();
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
                return MapException(ex, "Speech recognition could not be started.");
            }
        }

        private void SpeechRecognizer_HypothesisGenerated(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
        {
            if (!_isRecording) return;

            string hypothesis = args.Hypothesis?.Text ?? string.Empty;
            Debug.WriteLine($"[Voice] Hypothesis: '{hypothesis}'");

            string combinedText;

            lock (_bufferLock)
            {
                _currentHypothesis = hypothesis;
                combinedText = BuildCurrentTranscription();
                if (!string.IsNullOrWhiteSpace(combinedText))
                {
                    _latestTranscribedText = combinedText;
                }
            }

            if (!string.IsNullOrWhiteSpace(combinedText))
            {
                HypothesisReceived?.Invoke(this, combinedText);
            }
        }

        private void SpeechRecognizer_StateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
        {
            Debug.WriteLine($"[Voice] State: {args.State}");
            StateChanged?.Invoke(this, args.State);

            switch (args.State)
            {
                case SpeechRecognizerState.Capturing:
                case SpeechRecognizerState.SpeechDetected:
                case SpeechRecognizerState.SoundStarted:
                    StatusChanged?.Invoke(this, "Listening...");
                    break;
                case SpeechRecognizerState.Processing:
                    StatusChanged?.Invoke(this, "Transcribing...");
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
            // IMPORTANT: this log tells you exactly why recognition fails
            Debug.WriteLine($"[Voice] ResultGenerated: Text='{args.Result?.Text}' Status={args.Result?.Status} Confidence={args.Result?.Confidence}");

            if (args.Result != null && !string.IsNullOrWhiteSpace(args.Result.Text))
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
                    _latestTranscribedText = combinedText;
                }

                HypothesisReceived?.Invoke(this, combinedText);
            }
        }

        private void ContinuousRecognitionSession_Completed(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
        {
            Debug.WriteLine($"[Voice] Session Completed: Status={args.Status}");
            _lastCompletionStatus = args.Status;

            // Always signal Stop that the engine is finished (final result has been delivered by now)
            _sessionCompletedTcs?.TrySetResult(true);

            // If the user tapped Done or Cancel, Stop/Cancel methods return the result themselves.
            if (!_isRecording || _isStopping) return;

            // Otherwise the session ended by itself (silence timeout, network failure, etc.)
            string finalText = GetCurrentTranscribedText();
            if (string.IsNullOrWhiteSpace(finalText))
            {
                lock (_bufferLock)
                {
                    finalText = _latestTranscribedText;
                }
            }

            VoiceRecognitionResult result;
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                result = new VoiceRecognitionResult { Success = true, Text = finalText.Trim() };
            }
            else
            {
                result = MapCompletionStatus(args.Status);
            }

            _isRecording = false;
            SessionCompleted?.Invoke(this, result);
        }

        /// <summary>
        /// Stops recording, waits for the engine to deliver the final result and returns the full text.
        /// Releases recognizer session cleanly.
        /// </summary>
        public async Task<VoiceRecognitionResult> StopRecordingAndTranscribeAsync()
        {
            _isStopping = true;
            StatusChanged?.Invoke(this, "Transcribing...");

            try
            {
                if (_speechRecognizer != null && _speechRecognizer.ContinuousRecognitionSession != null)
                {
                    // StopAsync flushes audio and the engine then raises ResultGenerated, then Completed.
                    // Cloud dictation can take several seconds, so wait for the Completed event (max 8s).
                    var stopTask = _speechRecognizer.ContinuousRecognitionSession.StopAsync().AsTask();
                    var tcs = _sessionCompletedTcs;

                    var waitTask = tcs != null ? tcs.Task : Task.FromResult(true);
                    var all = Task.WhenAll(stopTask, waitTask);

                    var finished = await Task.WhenAny(all, Task.Delay(8000));
                    if (finished != all)
                    {
                        Debug.WriteLine("[Voice] Stop timed out, cancelling session");
                        try
                        {
                            await _speechRecognizer.ContinuousRecognitionSession.CancelAsync().AsTask();
                        }
                        catch
                        {
                        }
                    }
                    else
                    {
                        // Small grace period for any last callback
                        await Task.Delay(100);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Voice] Stop exception: {ex.Message}");
            }

            _isRecording = false;

            string finalText = GetCurrentTranscribedText();
            if (string.IsNullOrWhiteSpace(finalText))
            {
                lock (_bufferLock)
                {
                    finalText = _latestTranscribedText;
                }
            }

            var lastStatus = _lastCompletionStatus;

            await CleanupRecognizerAsync();
            _isStopping = false;

            if (string.IsNullOrWhiteSpace(finalText))
            {
                // Return the REAL reason instead of always "could not understand"
                return MapCompletionStatus(lastStatus);
            }

            return new VoiceRecognitionResult
            {
                Success = true,
                Text = finalText.Trim()
            };
        }

        /// <summary>
        /// Cancels recording session, discards any recognized speech and cleans up recognizer.
        /// </summary>
        public async Task CancelRecordingAsync()
        {
            _isStopping = true;
            _isRecording = false;

            try
            {
                _currentRecognitionOperation?.Cancel();
            }
            catch
            {
            }

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
                _latestTranscribedText = string.Empty;
            }

            await CleanupRecognizerAsync();
            _isStopping = false;
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
        /// Standalone speech recognition using core Windows RecognizeAsync engine.
        /// Auto-detects speech, listens to spoken input, and finalizes cleanly without hanging.
        /// </summary>
        public async Task<VoiceRecognitionResult> RecognizeSpeechAsync()
        {
            try
            {
                bool hasHardware = await CheckMicrophoneHardwareAsync();
                if (!hasHardware)
                {
                    return new VoiceRecognitionResult
                    {
                        Success = false,
                        Error = VoiceRecognitionError.MicrophoneUnavailable,
                        ErrorMessage = "Microphone hardware was not detected."
                    };
                }

                var targetLanguage = ResolveSpeechLanguage();
                CurrentLanguage = targetLanguage ?? SpeechRecognizer.SystemSpeechLanguage;
                await CleanupRecognizerAsync();

                _speechRecognizer = targetLanguage != null
                    ? new SpeechRecognizer(targetLanguage)
                    : new SpeechRecognizer();

                _speechRecognizer.StateChanged += SpeechRecognizer_StateChanged;
                _speechRecognizer.HypothesisGenerated += SpeechRecognizer_HypothesisGenerated;

                try
                {
                    _speechRecognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(10);
                    _speechRecognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(2);
                    _speechRecognizer.Timeouts.BabbleTimeout = TimeSpan.FromSeconds(30);
                }
                catch
                {
                }

                bool compiled =
                    await TryCompileAsync(_speechRecognizer,
                        new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"),
                        "Dictation")
                    ||
                    await TryCompileAsync(_speechRecognizer,
                        new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.WebSearch, "websearch"),
                        "WebSearch")
                    ||
                    await TryCompileAsync(_speechRecognizer, null, "Default");

                if (!compiled)
                {
                    await CleanupRecognizerAsync();
                    return new VoiceRecognitionResult
                    {
                        Success = false,
                        Error = VoiceRecognitionError.SpeechUnavailable,
                        ErrorMessage = "Speech recognition could not be initialized."
                    };
                }

                _isRecording = true;
                StatusChanged?.Invoke(this, "Listening...");

                _currentRecognitionOperation = _speechRecognizer.RecognizeAsync();
                var result = await _currentRecognitionOperation.AsTask();

                _isRecording = false;
                await CleanupRecognizerAsync();

                Debug.WriteLine($"[Voice] RecognizeAsync: Status={result?.Status} Text='{result?.Text}'");

                if (result != null && result.Status == SpeechRecognitionResultStatus.Success && !string.IsNullOrWhiteSpace(result.Text))
                {
                    return new VoiceRecognitionResult
                    {
                        Success = true,
                        Text = result.Text.Trim()
                    };
                }

                if (result != null)
                {
                    if (result.Status == SpeechRecognitionResultStatus.UserCanceled)
                    {
                        return new VoiceRecognitionResult
                        {
                            Success = false,
                            Error = VoiceRecognitionError.None,
                            ErrorMessage = string.Empty
                        };
                    }
                    return MapCompletionStatus(result.Status);
                }

                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.NotUnderstood,
                    ErrorMessage = "Could not understand the recording."
                };
            }
            catch (TaskCanceledException)
            {
                _isRecording = false;
                await CleanupRecognizerAsync();
                return new VoiceRecognitionResult
                {
                    Success = false,
                    Error = VoiceRecognitionError.None,
                    ErrorMessage = string.Empty
                };
            }
            catch (Exception ex)
            {
                _isRecording = false;
                await CleanupRecognizerAsync();
                return MapException(ex, "Speech recognition is unavailable.");
            }
        }

        /// <summary>
        /// Native Windows speech recognition with UI dialog.
        /// Useful as a backup if continuous recognition keeps failing.
        /// </summary>
        public async Task<VoiceRecognitionResult> RecognizeWithUIAsync()
        {
            try
            {
                var targetLanguage = ResolveSpeechLanguage();
                SpeechRecognizer recognizer = targetLanguage != null
                    ? new SpeechRecognizer(targetLanguage)
                    : new SpeechRecognizer();

                using (recognizer)
                {
                    try
                    {
                        recognizer.UIOptions.AudiblePrompt = "Speak your message...";
                        recognizer.UIOptions.ExampleText = "e.g. Tell me about Windows Phone";
                        recognizer.UIOptions.IsReadBackEnabled = false;
                        recognizer.UIOptions.ShowConfirmation = false;
                    }
                    catch
                    {
                    }

                    bool compiled =
                        await TryCompileAsync(recognizer,
                            new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"),
                            "UI-Dictation")
                        ||
                        await TryCompileAsync(recognizer,
                            new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.WebSearch, "websearch"),
                            "UI-WebSearch")
                        ||
                        await TryCompileAsync(recognizer, null, "UI-Default");

                    if (!compiled)
                    {
                        return new VoiceRecognitionResult
                        {
                            Success = false,
                            Error = VoiceRecognitionError.SpeechUnavailable,
                            ErrorMessage = "Speech recognition could not be initialized."
                        };
                    }

                    var uiResult = await recognizer.RecognizeWithUIAsync();
                    Debug.WriteLine($"[Voice] UI result: Text='{uiResult?.Text}' Status={uiResult?.Status}");

                    if (uiResult != null && uiResult.Status == SpeechRecognitionResultStatus.Success && !string.IsNullOrWhiteSpace(uiResult.Text))
                    {
                        return new VoiceRecognitionResult
                        {
                            Success = true,
                            Text = uiResult.Text.Trim()
                        };
                    }

                    return uiResult != null
                        ? MapCompletionStatus(uiResult.Status)
                        : new VoiceRecognitionResult
                        {
                            Success = false,
                            Error = VoiceRecognitionError.NotUnderstood,
                            ErrorMessage = "Could not understand the recording."
                        };
                }
            }
            catch (Exception ex)
            {
                return MapException(ex, "Speech recognition is unavailable.");
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
                _currentRecognitionOperation = null;
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