using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WinAI.Models;

namespace WinAI.Services
{
    /// <summary>
    /// Fetches available AI models dynamically from configured provider REST APIs.
    /// </summary>
    public sealed class ModelService
    {
        private static readonly Lazy<ModelService> _instance = new Lazy<ModelService>(() => new ModelService());
        public static ModelService Instance => _instance.Value;

        private readonly HttpClient _httpClient;
        private readonly ApiKeyService _apiKeyService = ApiKeyService.Instance;

        private List<AiModelItem> _cachedModels = new List<AiModelItem>();
        private DateTime _lastFetchTime = DateTime.MinValue;

        private ModelService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
        }

        public async Task<List<AiModelItem>> GetAvailableModelsAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedModels.Count > 0 && (DateTime.Now - _lastFetchTime).TotalMinutes < 15)
            {
                return _cachedModels;
            }

            var models = new List<AiModelItem>();

            // 1. OpenAI
            if (!string.IsNullOrWhiteSpace(_apiKeyService.OpenAiKey))
            {
                var openAiModels = await FetchOpenAiModelsAsync(_apiKeyService.OpenAiKey);
                models.AddRange(openAiModels);
            }

            // 2. Google Gemini
            if (!string.IsNullOrWhiteSpace(_apiKeyService.GeminiKey))
            {
                var geminiModels = await FetchGeminiModelsAsync(_apiKeyService.GeminiKey);
                models.AddRange(geminiModels);
            }

            // 3. DeepSeek
            if (!string.IsNullOrWhiteSpace(_apiKeyService.DeepSeekKey))
            {
                var deepSeekModels = await FetchDeepSeekModelsAsync(_apiKeyService.DeepSeekKey);
                models.AddRange(deepSeekModels);
            }

            // 4. Groq
            if (!string.IsNullOrWhiteSpace(_apiKeyService.GroqKey))
            {
                var groqModels = await FetchOpenAiCompatibleModelsAsync(
                    "https://api.groq.com/openai/v1/models",
                    _apiKeyService.GroqKey,
                    "Groq"
                );
                models.AddRange(groqModels);
            }

            // 5. OpenRouter
            if (!string.IsNullOrWhiteSpace(_apiKeyService.OpenRouterKey))
            {
                var openRouterModels = await FetchOpenRouterModelsAsync(_apiKeyService.OpenRouterKey);
                models.AddRange(openRouterModels);
            }

            // 6. Mistral AI
            if (!string.IsNullOrWhiteSpace(_apiKeyService.MistralKey))
            {
                var mistralModels = await FetchOpenAiCompatibleModelsAsync(
                    "https://api.mistral.ai/v1/models",
                    _apiKeyService.MistralKey,
                    "Mistral"
                );
                models.AddRange(mistralModels);
            }

            // 7. xAI
            if (!string.IsNullOrWhiteSpace(_apiKeyService.XAiKey))
            {
                var xaiModels = await FetchOpenAiCompatibleModelsAsync(
                    "https://api.x.ai/v1/models",
                    _apiKeyService.XAiKey,
                    "xAI"
                );
                models.AddRange(xaiModels);
            }

            // 8. Custom Endpoint
            if (!string.IsNullOrWhiteSpace(_apiKeyService.CustomBaseUrl))
            {
                string baseUrl = _apiKeyService.CustomBaseUrl.TrimEnd('/');
                string endpoint = baseUrl.EndsWith("/v1") ? $"{baseUrl}/models" : $"{baseUrl}/v1/models";
                var customModels = await FetchOpenAiCompatibleModelsAsync(
                    endpoint,
                    _apiKeyService.CustomKey,
                    "Custom"
                );
                models.AddRange(customModels);
            }

            // 9. Anthropic Claude (Anthropic does not have a public /models listing endpoint without beta headers, so we construct dynamically if key is set)
            if (!string.IsNullOrWhiteSpace(_apiKeyService.ClaudeKey))
            {
                models.Add(new AiModelItem { Id = "claude-3-5-sonnet-20241022", DisplayName = "Claude 3.5 Sonnet", ProviderName = "Anthropic" });
                models.Add(new AiModelItem { Id = "claude-3-5-haiku-20241022", DisplayName = "Claude 3.5 Haiku", ProviderName = "Anthropic" });
                models.Add(new AiModelItem { Id = "claude-3-opus-20240229", DisplayName = "Claude 3 Opus", ProviderName = "Anthropic" });
            }

            // 10. Perplexity
            if (!string.IsNullOrWhiteSpace(_apiKeyService.PerplexityKey))
            {
                models.Add(new AiModelItem { Id = "sonar-pro", DisplayName = "Sonar Pro (Search)", ProviderName = "Perplexity" });
                models.Add(new AiModelItem { Id = "sonar-reasoning", DisplayName = "Sonar Reasoning", ProviderName = "Perplexity" });
                models.Add(new AiModelItem { Id = "sonar", DisplayName = "Sonar", ProviderName = "Perplexity" });
            }

            if (models.Count > 0)
            {
                _cachedModels = models;
                _lastFetchTime = DateTime.Now;
            }

            return _cachedModels;
        }

        #region Provider Fetch Implementations

        private async Task<List<AiModelItem>> FetchOpenAiModelsAsync(string apiKey)
        {
            var list = new List<AiModelItem>();
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models"))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    var resp = await _httpClient.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync();
                        var obj = JObject.Parse(json);
                        var data = obj["data"] as JArray;
                        if (data != null)
                        {
                            foreach (var item in data)
                            {
                                string id = item["id"]?.ToString();
                                if (string.IsNullOrEmpty(id)) continue;

                                // Filter for relevant chat & reasoning models
                                if (id.StartsWith("gpt-4") || id.StartsWith("gpt-3.5") || id.StartsWith("o1") || id.StartsWith("o3") || id.StartsWith("chatgpt"))
                                {
                                    list.Add(new AiModelItem
                                    {
                                        Id = id,
                                        DisplayName = id,
                                        ProviderName = "OpenAI"
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback if offline
            }

            if (list.Count == 0)
            {
                list.Add(new AiModelItem { Id = "gpt-4o", DisplayName = "GPT-4o", ProviderName = "OpenAI" });
                list.Add(new AiModelItem { Id = "gpt-4o-mini", DisplayName = "GPT-4o Mini", ProviderName = "OpenAI" });
                list.Add(new AiModelItem { Id = "o1-mini", DisplayName = "o1 Mini", ProviderName = "OpenAI" });
            }

            return list.OrderBy(m => m.DisplayName).ToList();
        }

        private async Task<List<AiModelItem>> FetchGeminiModelsAsync(string apiKey)
        {
            var list = new List<AiModelItem>();
            try
            {
                string url = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}";
                var resp = await _httpClient.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    var obj = JObject.Parse(json);
                    var models = obj["models"] as JArray;
                    if (models != null)
                    {
                        foreach (var m in models)
                        {
                            string name = m["name"]?.ToString(); // e.g. "models/gemini-1.5-pro"
                            string displayName = m["displayName"]?.ToString();
                            var methods = m["supportedGenerationMethods"] as JArray;

                            if (methods != null && methods.Any(method => method.ToString() == "generateContent"))
                            {
                                string cleanId = name.StartsWith("models/") ? name.Substring(7) : name;
                                list.Add(new AiModelItem
                                {
                                    Id = cleanId,
                                    DisplayName = !string.IsNullOrWhiteSpace(displayName) ? displayName : cleanId,
                                    ProviderName = "Google Gemini"
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback
            }

            if (list.Count == 0)
            {
                list.Add(new AiModelItem { Id = "gemini-1.5-flash", DisplayName = "Gemini 1.5 Flash", ProviderName = "Google Gemini" });
                list.Add(new AiModelItem { Id = "gemini-1.5-pro", DisplayName = "Gemini 1.5 Pro", ProviderName = "Google Gemini" });
                list.Add(new AiModelItem { Id = "gemini-2.0-flash", DisplayName = "Gemini 2.0 Flash", ProviderName = "Google Gemini" });
            }

            return list.OrderBy(m => m.DisplayName).ToList();
        }

        private async Task<List<AiModelItem>> FetchDeepSeekModelsAsync(string apiKey)
        {
            var list = new List<AiModelItem>();
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/models"))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    var resp = await _httpClient.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync();
                        var obj = JObject.Parse(json);
                        var data = obj["data"] as JArray;
                        if (data != null)
                        {
                            foreach (var item in data)
                            {
                                string id = item["id"]?.ToString();
                                if (!string.IsNullOrEmpty(id))
                                {
                                    list.Add(new AiModelItem
                                    {
                                        Id = id,
                                        DisplayName = id,
                                        ProviderName = "DeepSeek"
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback
            }

            if (list.Count == 0)
            {
                list.Add(new AiModelItem { Id = "deepseek-chat", DisplayName = "DeepSeek-V3 (Chat)", ProviderName = "DeepSeek" });
                list.Add(new AiModelItem { Id = "deepseek-reasoner", DisplayName = "DeepSeek-R1 (Reasoner)", ProviderName = "DeepSeek" });
            }

            return list;
        }

        private async Task<List<AiModelItem>> FetchOpenRouterModelsAsync(string apiKey)
        {
            var list = new List<AiModelItem>();
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models"))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    var resp = await _httpClient.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync();
                        var obj = JObject.Parse(json);
                        var data = obj["data"] as JArray;
                        if (data != null)
                        {
                            // Take top 20 prominent models to avoid overwhelming mobile memory
                            foreach (var item in data.Take(20))
                            {
                                string id = item["id"]?.ToString();
                                string name = item["name"]?.ToString() ?? id;
                                if (!string.IsNullOrEmpty(id))
                                {
                                    list.Add(new AiModelItem
                                    {
                                        Id = id,
                                        DisplayName = name,
                                        ProviderName = "OpenRouter"
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback
            }

            if (list.Count == 0)
            {
                list.Add(new AiModelItem { Id = "meta-llama/llama-3.3-70b-instruct", DisplayName = "Llama 3.3 70B", ProviderName = "OpenRouter" });
                list.Add(new AiModelItem { Id = "anthropic/claude-3.5-sonnet", DisplayName = "Claude 3.5 Sonnet", ProviderName = "OpenRouter" });
            }

            return list;
        }

        private async Task<List<AiModelItem>> FetchOpenAiCompatibleModelsAsync(string endpoint, string apiKey, string providerName)
        {
            var list = new List<AiModelItem>();
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, endpoint))
                {
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    }
                    var resp = await _httpClient.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync();
                        var obj = JObject.Parse(json);
                        var data = obj["data"] as JArray;
                        if (data != null)
                        {
                            foreach (var item in data)
                            {
                                string id = item["id"]?.ToString();
                                if (!string.IsNullOrEmpty(id))
                                {
                                    list.Add(new AiModelItem
                                    {
                                        Id = id,
                                        DisplayName = id,
                                        ProviderName = providerName
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback
            }

            return list;
        }

        #endregion
    }
}
