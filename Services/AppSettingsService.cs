using System;
using Windows.Storage;
using WinAI.Models;
using WinAI.Services.Providers;

namespace WinAI.Services
{
    /// <summary>
    /// Centralized service and single source of truth for persistent application settings,
    /// preferred default AI provider, and default model configuration.
    /// Guarantees that saved settings survive app restarts and are initialized before UI rendering.
    /// </summary>
    public sealed class AppSettingsService
    {
        public const string SettingDefaultProviderKey = "App_DefaultProvider";
        public const string SettingDefaultModelKey = "App_DefaultModel";
        public const string SettingFontSizeKey = "App_FontSize";
        public const string SettingLanguageKey = "App_Language";

        private static readonly Lazy<AppSettingsService> _instance = 
            new Lazy<AppSettingsService>(() => new AppSettingsService());

        public static AppSettingsService Instance => _instance.Value;

        private readonly ApplicationDataContainer _localSettings;
        private string _cachedProviderId;
        private string _cachedModelId;
        private bool _isInitialized = false;

        public event EventHandler DefaultModelChanged;

        private AppSettingsService()
        {
            _localSettings = ApplicationData.Current.LocalSettings;
        }

        /// <summary>
        /// Asynchronously initializes application state, ensuring local model cache and persistent
        /// settings are fully loaded before UI pages render.
        /// </summary>
        public async System.Threading.Tasks.Task InitializeAsync()
        {
            if (_isInitialized) return;

            // 1. Await persistent model cache from disk
            await ModelService.Instance.InitializeAsync();

            // 2. Load persisted provider and model settings
            string savedProvider = GetString(SettingDefaultProviderKey, "openai");
            string savedModel = GetString(SettingDefaultModelKey, "gpt-4o");

            _cachedProviderId = AiProviderRegistry.NormalizeProviderId(savedProvider);
            _cachedModelId = string.IsNullOrWhiteSpace(savedModel) ? "gpt-4o" : savedModel.Trim();

            _isInitialized = true;
        }

        public string DefaultProviderId
        {
            get
            {
                if (!_isInitialized)
                {
                    string saved = GetString(SettingDefaultProviderKey, "openai");
                    _cachedProviderId = AiProviderRegistry.NormalizeProviderId(saved);
                }
                return _cachedProviderId ?? "openai";
            }
            set
            {
                string norm = AiProviderRegistry.NormalizeProviderId(value);
                _cachedProviderId = norm;
                SetString(SettingDefaultProviderKey, norm);
            }
        }

        public string DefaultModelId
        {
            get
            {
                if (!_isInitialized)
                {
                    _cachedModelId = GetString(SettingDefaultModelKey, "gpt-4o");
                }
                return _cachedModelId ?? "gpt-4o";
            }
            set
            {
                _cachedModelId = value ?? "gpt-4o";
                SetString(SettingDefaultModelKey, _cachedModelId);
            }
        }

        public AiModelDescriptor GetDefaultModelDescriptor()
        {
            string providerId = DefaultProviderId;
            string modelId = DefaultModelId;

            var descriptor = AiProviderRegistry.Instance.GetModel(providerId, modelId);
            if (descriptor != null)
            {
                return descriptor;
            }

            var provider = AiProviderRegistry.Instance.GetProvider(providerId);
            if (provider != null)
            {
                var models = provider.GetModels();
                if (models != null && models.Count > 0)
                {
                    return models[0];
                }
            }

            return new AiModelDescriptor("gpt-4o", "GPT-4o", "openai", "OpenAI", ModelCapabilities.Text | ModelCapabilities.Vision, false);
        }

        public void SetDefaultModel(string providerId, string modelId, string displayName = null)
        {
            string norm = AiProviderRegistry.NormalizeProviderId(providerId);
            _cachedProviderId = norm;
            _cachedModelId = modelId ?? "gpt-4o";

            SetString(SettingDefaultProviderKey, norm);
            SetString(SettingDefaultModelKey, _cachedModelId);

            DefaultModelChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetString(string key, string defaultValue)
        {
            try
            {
                if (_localSettings.Values.TryGetValue(key, out object val) && val is string str)
                {
                    return str;
                }
            }
            catch
            {
                // Fallback
            }
            return defaultValue;
        }

        public void SetString(string key, string value)
        {
            try
            {
                _localSettings.Values[key] = value;
            }
            catch
            {
                // Fallback
            }
        }
    }
}
