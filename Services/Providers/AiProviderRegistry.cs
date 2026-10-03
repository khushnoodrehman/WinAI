using System;
using System.Collections.Generic;
using System.Linq;
using WinAI.Models;

namespace WinAI.Services.Providers
{
    public class ModelSwitchResult
    {
        public bool Success { get; set; }
        public string ProviderId { get; set; }
        public string ModelId { get; set; }
        public AiModelDescriptor SelectedModel { get; set; }
        public bool RequiresConfiguration { get; set; }
        public string ErrorMessage { get; set; }
        public string TargetProviderName { get; set; }
    }

    public interface IAiProviderRegistry
    {
        IReadOnlyList<IAiProvider> GetProviders();
        IAiProvider GetProvider(string providerId);
        bool IsProviderConfigured(string providerId);
        IReadOnlyList<AiModelDescriptor> GetAllModels();
        IReadOnlyList<AiModelDescriptor> GetModelsForProvider(string providerId);
        AiModelDescriptor GetModel(string providerId, string modelId);
        bool IsModelAvailable(string providerId, string modelId);
        ModelSwitchResult TrySwitchModel(string currentProviderId, string currentModelId, string targetProviderId, string targetModelId);
    }

    /// <summary>
    /// Centralized registry for all AI providers and model descriptors.
    /// Manages provider discovery, model resolution, Key Vault availability checks,
    /// and atomic model switching within existing conversations.
    /// </summary>
    public sealed class AiProviderRegistry : IAiProviderRegistry
    {
        private static readonly Lazy<AiProviderRegistry> _instance = 
            new Lazy<AiProviderRegistry>(() => new AiProviderRegistry());

        public static AiProviderRegistry Instance => _instance.Value;

        private readonly Dictionary<string, IAiProvider> _providers = 
            new Dictionary<string, IAiProvider>(StringComparer.OrdinalIgnoreCase);

        public AiProviderRegistry()
        {
            RegisterProvider(new OpenAiProvider());
            RegisterProvider(new GeminiProvider());
            RegisterProvider(new ClaudeProvider());
            RegisterProvider(new DeepSeekProvider());
            RegisterProvider(new GrokProvider());
            RegisterProvider(new PerplexityProvider());
        }

        public void RegisterProvider(IAiProvider provider)
        {
            if (provider == null || string.IsNullOrWhiteSpace(provider.Id)) return;
            _providers[provider.Id] = provider;
        }

        public IReadOnlyList<IAiProvider> GetProviders()
        {
            return _providers.Values.ToList();
        }

        public IAiProvider GetProvider(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return _providers.TryGetValue("openai", out var def) ? def : null;

            string key = NormalizeProviderId(providerId);
            if (_providers.TryGetValue(key, out var provider))
            {
                return provider;
            }

            return _providers.TryGetValue("openai", out var fallback) ? fallback : null;
        }

        public bool IsProviderConfigured(string providerId)
        {
            var provider = GetProvider(providerId);
            return provider != null && provider.IsConfigured;
        }

        public IReadOnlyList<AiModelDescriptor> GetAllModels()
        {
            var list = new List<AiModelDescriptor>();
            foreach (var p in _providers.Values)
            {
                list.AddRange(p.GetModels());
            }
            return list;
        }

        public IReadOnlyList<AiModelDescriptor> GetModelsForProvider(string providerId)
        {
            var provider = GetProvider(providerId);
            if (provider == null) return new List<AiModelDescriptor>();
            return provider.GetModels();
        }

        public AiModelDescriptor GetModel(string providerId, string modelId)
        {
            var provider = GetProvider(providerId);
            if (provider == null) return null;

            var models = provider.GetModels();
            if (models.Count == 0) return null;

            if (string.IsNullOrWhiteSpace(modelId))
            {
                return models[0];
            }

            string clean = modelId.Trim();
            if (clean.StartsWith("models/")) clean = clean.Substring(7);

            var exact = models.FirstOrDefault(m => string.Equals(m.Id, clean, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            var byName = models.FirstOrDefault(m => string.Equals(m.DisplayName, clean, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;

            var contains = models.FirstOrDefault(m => m.Id.IndexOf(clean, StringComparison.OrdinalIgnoreCase) >= 0);
            if (contains != null) return contains;

            return models[0];
        }

        public bool IsModelAvailable(string providerId, string modelId)
        {
            if (!IsProviderConfigured(providerId)) return false;
            var model = GetModel(providerId, modelId);
            return model != null && model.IsConfigured;
        }

        /// <summary>
        /// Validates and executes a model switch within a conversation.
        /// If the target provider is not configured with an API key, the switch is rejected
        /// and the previous active model is safely retained.
        /// </summary>
        public ModelSwitchResult TrySwitchModel(
            string currentProviderId, 
            string currentModelId, 
            string targetProviderId, 
            string targetModelId)
        {
            var targetProvider = GetProvider(targetProviderId);
            if (targetProvider == null)
            {
                var curModel = GetModel(currentProviderId, currentModelId);
                return new ModelSwitchResult
                {
                    Success = false,
                    ProviderId = currentProviderId,
                    ModelId = currentModelId,
                    SelectedModel = curModel,
                    RequiresConfiguration = false,
                    ErrorMessage = $"Unknown provider: '{targetProviderId}'."
                };
            }

            // Check if required API credentials exist in Key Vault
            if (!targetProvider.IsConfigured)
            {
                var curModel = GetModel(currentProviderId, currentModelId);
                return new ModelSwitchResult
                {
                    Success = false,
                    ProviderId = currentProviderId,
                    ModelId = currentModelId,
                    SelectedModel = curModel,
                    RequiresConfiguration = true,
                    TargetProviderName = targetProvider.DisplayName,
                    ErrorMessage = $"{targetProvider.DisplayName} is not configured. An API key is required in Key Vault."
                };
            }

            // Target provider is valid and configured; resolve model descriptor
            var resolvedModel = GetModel(targetProvider.Id, targetModelId);
            if (resolvedModel == null)
            {
                var curModel = GetModel(currentProviderId, currentModelId);
                return new ModelSwitchResult
                {
                    Success = false,
                    ProviderId = currentProviderId,
                    ModelId = currentModelId,
                    SelectedModel = curModel,
                    RequiresConfiguration = false,
                    ErrorMessage = $"No valid model found for {targetProvider.DisplayName}."
                };
            }

            return new ModelSwitchResult
            {
                Success = true,
                ProviderId = targetProvider.Id,
                ModelId = resolvedModel.Id,
                SelectedModel = resolvedModel,
                RequiresConfiguration = false,
                TargetProviderName = targetProvider.DisplayName,
                ErrorMessage = null
            };
        }

        public static string NormalizeProviderId(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return "openai";
            string lower = providerId.Trim().ToLowerInvariant();

            if (lower.Contains("gemini") || lower.Contains("google")) return "gemini";
            if (lower.Contains("claude") || lower.Contains("anthropic")) return "claude";
            if (lower.Contains("deepseek")) return "deepseek";
            if (lower.Contains("xai") || lower.Contains("grok")) return "xai";
            if (lower.Contains("perplexity") || lower.Contains("sonar")) return "perplexity";
            if (lower.Contains("groq")) return "groq";
            if (lower.Contains("openrouter")) return "openrouter";
            return "openai";
        }
    }
}
