using System;
using System.Collections.Generic;
using Windows.Storage;

namespace WinAI.Services
{
    /// <summary>
    /// Service responsible for persistent storage and retrieval of AI provider API keys
    /// using Windows.Storage.ApplicationData.Current.LocalSettings.
    /// </summary>
    public sealed class ApiKeyService
    {
        private static readonly Lazy<ApiKeyService> _instance = new Lazy<ApiKeyService>(() => new ApiKeyService());
        public static ApiKeyService Instance => _instance.Value;

        // Setting keys
        public const string KeyOpenAI = "ApiKey_OpenAI";
        public const string KeyGemini = "ApiKey_Gemini";
        public const string KeyClaude = "ApiKey_Claude";
        public const string KeyDeepSeek = "ApiKey_DeepSeek";
        public const string KeyPerplexity = "ApiKey_Perplexity";
        public const string KeyGroq = "ApiKey_Groq";
        public const string KeyOpenRouter = "ApiKey_OpenRouter";
        public const string KeyMistral = "ApiKey_Mistral";
        public const string KeyXAI = "ApiKey_XAI";
        public const string KeyCustomBaseUrl = "ApiKey_CustomBaseUrl";
        public const string KeyCustomKey = "ApiKey_CustomKey";

        public event EventHandler KeysChanged;

        private readonly ApplicationDataContainer _settings;

        private ApiKeyService()
        {
            _settings = ApplicationData.Current.LocalSettings;
        }

        #region Properties

        public string OpenAiKey
        {
            get => GetValue(KeyOpenAI);
            set => SetValue(KeyOpenAI, value);
        }

        public string GeminiKey
        {
            get => GetValue(KeyGemini);
            set => SetValue(KeyGemini, value);
        }

        public string ClaudeKey
        {
            get => GetValue(KeyClaude);
            set => SetValue(KeyClaude, value);
        }

        public string DeepSeekKey
        {
            get => GetValue(KeyDeepSeek);
            set => SetValue(KeyDeepSeek, value);
        }

        public string PerplexityKey
        {
            get => GetValue(KeyPerplexity);
            set => SetValue(KeyPerplexity, value);
        }

        public string GroqKey
        {
            get => GetValue(KeyGroq);
            set => SetValue(KeyGroq, value);
        }

        public string OpenRouterKey
        {
            get => GetValue(KeyOpenRouter);
            set => SetValue(KeyOpenRouter, value);
        }

        public string MistralKey
        {
            get => GetValue(KeyMistral);
            set => SetValue(KeyMistral, value);
        }

        public string XAiKey
        {
            get => GetValue(KeyXAI);
            set => SetValue(KeyXAI, value);
        }

        public string CustomBaseUrl
        {
            get => GetValue(KeyCustomBaseUrl);
            set => SetValue(KeyCustomBaseUrl, value);
        }

        public string CustomKey
        {
            get => GetValue(KeyCustomKey);
            set => SetValue(KeyCustomKey, value);
        }

        #endregion

        #region Helper Methods

        private string GetValue(string key)
        {
            if (_settings.Values.TryGetValue(key, out object value) && value is string strVal)
            {
                return strVal;
            }
            return string.Empty;
        }

        private void SetValue(string key, string value)
        {
            string sanitized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (string.IsNullOrEmpty(sanitized))
            {
                if (_settings.Values.ContainsKey(key))
                {
                    _settings.Values.Remove(key);
                }
            }
            else
            {
                _settings.Values[key] = sanitized;
            }
        }

        public bool IsConfigured(string key)
        {
            string val = GetValue(key);
            return !string.IsNullOrWhiteSpace(val);
        }

        public bool HasAnyKey()
        {
            return GetConfiguredCount() > 0;
        }

        public int GetConfiguredCount()
        {
            int count = 0;
            if (!string.IsNullOrWhiteSpace(OpenAiKey)) count++;
            if (!string.IsNullOrWhiteSpace(GeminiKey)) count++;
            if (!string.IsNullOrWhiteSpace(ClaudeKey)) count++;
            if (!string.IsNullOrWhiteSpace(DeepSeekKey)) count++;
            if (!string.IsNullOrWhiteSpace(PerplexityKey)) count++;
            if (!string.IsNullOrWhiteSpace(GroqKey)) count++;
            if (!string.IsNullOrWhiteSpace(OpenRouterKey)) count++;
            if (!string.IsNullOrWhiteSpace(MistralKey)) count++;
            if (!string.IsNullOrWhiteSpace(XAiKey)) count++;
            if (!string.IsNullOrWhiteSpace(CustomBaseUrl)) count++;
            return count;
        }

        public List<string> GetConfiguredProviderNames()
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(OpenAiKey)) list.Add("OpenAI");
            if (!string.IsNullOrWhiteSpace(GeminiKey)) list.Add("Google Gemini");
            if (!string.IsNullOrWhiteSpace(ClaudeKey)) list.Add("Anthropic Claude");
            if (!string.IsNullOrWhiteSpace(DeepSeekKey)) list.Add("DeepSeek");
            if (!string.IsNullOrWhiteSpace(PerplexityKey)) list.Add("Perplexity");
            if (!string.IsNullOrWhiteSpace(GroqKey)) list.Add("Groq");
            if (!string.IsNullOrWhiteSpace(OpenRouterKey)) list.Add("OpenRouter");
            if (!string.IsNullOrWhiteSpace(MistralKey)) list.Add("Mistral AI");
            if (!string.IsNullOrWhiteSpace(XAiKey)) list.Add("xAI (Grok)");
            if (!string.IsNullOrWhiteSpace(CustomBaseUrl)) list.Add("Custom Endpoint");
            return list;
        }

        public void SaveKeys(
            string openAi,
            string gemini,
            string claude,
            string deepSeek,
            string perplexity,
            string groq,
            string openRouter,
            string mistral,
            string xAi,
            string customBaseUrl,
            string customKey)
        {
            OpenAiKey = openAi;
            GeminiKey = gemini;
            ClaudeKey = claude;
            DeepSeekKey = deepSeek;
            PerplexityKey = perplexity;
            GroqKey = groq;
            OpenRouterKey = openRouter;
            MistralKey = mistral;
            XAiKey = xAi;
            CustomBaseUrl = customBaseUrl;
            CustomKey = customKey;

            KeysChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearAll()
        {
            string[] allKeys = new[]
            {
                KeyOpenAI,
                KeyGemini,
                KeyClaude,
                KeyDeepSeek,
                KeyPerplexity,
                KeyGroq,
                KeyOpenRouter,
                KeyMistral,
                KeyXAI,
                KeyCustomBaseUrl,
                KeyCustomKey
            };

            foreach (var key in allKeys)
            {
                if (_settings.Values.ContainsKey(key))
                {
                    _settings.Values.Remove(key);
                }
            }

            KeysChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
