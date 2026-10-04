using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WinAI.Models;
using WinAI.Services.Providers;

namespace WinAI.Services
{
    public class ModelTestResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<AiModelItem> Models { get; set; } = new List<AiModelItem>();
        public int StatusCode { get; set; }
    }

    /// <summary>
    /// Fetches available AI models dynamically from configured provider REST APIs,
    /// caches them persistently to local storage so they are not re-fetched on every app open,
    /// and allows live testing and quota/limit diagnostics per provider.
    /// </summary>
    public sealed class ModelService
    {
        private const string CacheFileName = "cached_models.json";

        private static readonly Lazy<ModelService> _instance = new Lazy<ModelService>(() => new ModelService());
        public static ModelService Instance => _instance.Value;

        private readonly HttpClient _httpClient;
        private readonly ApiKeyService _apiKeyService = ApiKeyService.Instance;

        private readonly Dictionary<string, List<AiModelItem>> _providerModelsCache = 
            new Dictionary<string, List<AiModelItem>>(StringComparer.OrdinalIgnoreCase);

        private List<AiModelItem> _allCachedModels = new List<AiModelItem>();
        private DateTime _lastFetchTime = DateTime.MinValue;

        private ModelService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            // 1. Populate built-in fallback models so the app always has working models offline
            InitializeDefaultModels();
        }

        private bool _isInitialized = false;

        /// <summary>
        /// Asynchronously loads cached models from persistent disk storage on app startup.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized) return;
            await LoadCacheFromDiskAsync();
            _isInitialized = true;
        }

        #region Default Fallback Models

        private void InitializeDefaultModels()
        {
            // Models are ONLY populated when an API key is configured by the user,
            // tested against the provider's REST API, and cached locally on disk.
            var knownProviders = new[] { "openai", "gemini", "claude", "perplexity", "deepseek", "xai", "groq", "openrouter", "custom" };
            foreach (var p in knownProviders)
            {
                if (!_providerModelsCache.ContainsKey(p))
                {
                    _providerModelsCache[p] = new List<AiModelItem>();
                }
            }
        }

        /// <summary>
        /// Clears cached models for a provider when its API key is removed.
        /// </summary>
        public void ClearModelsForProvider(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return;
            string key = AiProviderRegistry.NormalizeProviderId(providerId);
            lock (_providerModelsCache)
            {
                if (_providerModelsCache.ContainsKey(key))
                {
                    _providerModelsCache[key].Clear();
                }
            }
            var _ = SaveCacheToDiskAsync();
            ModelStatusChanged?.Invoke(this, key);
        }

        #endregion

        #region Local Disk Persistence (No Network on App Open)

        private async Task SaveCacheToDiskAsync()
        {
            try
            {
                var localFolder = ApplicationData.Current.LocalFolder;
                var file = await localFolder.CreateFileAsync(CacheFileName, CreationCollisionOption.ReplaceExisting);
                string json;
                lock (_providerModelsCache)
                {
                    json = JsonConvert.SerializeObject(_providerModelsCache);
                }
                await FileIO.WriteTextAsync(file, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModelService] Failed to save cached models to disk: {ex.Message}");
            }
        }

        private async Task LoadCacheFromDiskAsync()
        {
            try
            {
                var localFolder = ApplicationData.Current.LocalFolder;
                var item = await localFolder.TryGetItemAsync(CacheFileName);
                if (item is StorageFile file)
                {
                    string json = await FileIO.ReadTextAsync(file);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var dict = JsonConvert.DeserializeObject<Dictionary<string, List<AiModelItem>>>(json);
                        if (dict != null)
                        {
                            lock (_providerModelsCache)
                            {
                                foreach (var kvp in dict)
                                {
                                    if (kvp.Value != null && kvp.Value.Count > 0)
                                    {
                                        _providerModelsCache[kvp.Key] = kvp.Value;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModelService] Failed to load cached models from disk: {ex.Message}");
            }
        }

        #endregion

        #region Synchronous Local Model Retrieval

        /// <summary>
        /// Gets all models available for a specific provider from local cache without making network calls.
        /// </summary>
        public List<AiModelItem> GetModelsForProvider(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return new List<AiModelItem>();

            string key = providerId.Trim().ToLowerInvariant();
            if (key.Contains("gemini") || key.Contains("google")) key = "gemini";
            else if (key.Contains("claude") || key.Contains("anthropic")) key = "claude";
            else if (key.Contains("perplexity") || key.Contains("sonar")) key = "perplexity";
            else if (key.Contains("deepseek")) key = "deepseek";
            else if (key.Contains("xai") || key.Contains("grok")) key = "xai";
            else if (key.Contains("groq")) key = "groq";
            else if (key.Contains("openrouter")) key = "openrouter";
            else if (key.Contains("custom")) key = "custom";
            else key = "openai";

            lock (_providerModelsCache)
            {
                if (_providerModelsCache.TryGetValue(key, out var list) && list != null && list.Count > 0)
                {
                    return new List<AiModelItem>(list);
                }
            }

            return new List<AiModelItem>();
        }

        public string GetDefaultModelIdForProvider(string providerId)
        {
            var list = GetModelsForProvider(providerId);
            return list.Count > 0 ? list[0].Id : "gpt-4o";
        }

        public string GetDefaultModelDisplayNameForProvider(string providerId)
        {
            var list = GetModelsForProvider(providerId);
            return list.Count > 0 ? list[0].DisplayName : "GPT-4o";
        }

        public event EventHandler<string> ModelStatusChanged;

        public void UpdateModelStatus(string providerId, string modelId, bool isAvailable, string status, string details = null, long? latencyMs = null)
        {
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(modelId)) return;

            string key = AiProviderRegistry.NormalizeProviderId(providerId);
            lock (_providerModelsCache)
            {
                if (!_providerModelsCache.TryGetValue(key, out var list) || list == null)
                {
                    list = new List<AiModelItem>();
                    _providerModelsCache[key] = list;
                }

                var match = list.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    match.IsAvailable = isAvailable;
                    match.Status = status;
                    match.StatusDetails = details;
                    match.LastValidatedUtc = DateTime.UtcNow;
                    if (latencyMs.HasValue) match.LatencyMs = latencyMs.Value;
                }
                else
                {
                    list.Add(new AiModelItem
                    {
                        Id = modelId,
                        DisplayName = modelId,
                        ProviderName = providerId,
                        IsAvailable = isAvailable,
                        Status = status,
                        StatusDetails = details,
                        LastValidatedUtc = DateTime.UtcNow,
                        LatencyMs = latencyMs
                    });
                }
            }

            var _ = SaveCacheToDiskAsync();
            ModelStatusChanged?.Invoke(this, key);
        }

        public async Task<ModelTestResult> RefreshModelsForProviderAsync(string providerId)
        {
            string key = CredentialVaultService.Instance.GetApiKey(providerId);
            if (string.IsNullOrWhiteSpace(key))
            {
                return new ModelTestResult
                {
                    Success = false,
                    StatusCode = 401,
                    Message = "No API key configured for this provider in Key Vault."
                };
            }

            var result = await TestAndFetchModelsForProviderAsync(providerId, key);
            if (result.Success)
            {
                ModelStatusChanged?.Invoke(this, AiProviderRegistry.NormalizeProviderId(providerId));
            }
            return result;
        }

        #endregion

        #region Live Key Testing & Dynamic Fetching

        /// <summary>
        /// Tests the given API key against the provider's REST API and fetches all available models.
        /// If successful, saves models persistently to disk so they are ready for future sessions.
        /// If rate-limited or quota-exceeded, returns actionable diagnostic messages.
        /// </summary>
        public async Task<ModelTestResult> TestAndFetchModelsForProviderAsync(string providerId, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ModelTestResult
                {
                    Success = false,
                    Message = "Please enter an API key to test.",
                    StatusCode = 400
                };
            }

            string cleanKey = apiKey.Trim();
            string pid = providerId?.Trim().ToLowerInvariant() ?? "openai";

            try
            {
                if (pid.Contains("gemini") || pid.Contains("google"))
                {
                    return await TestAndFetchGeminiAsync(cleanKey);
                }
                else if (pid.Contains("claude") || pid.Contains("anthropic"))
                {
                    return await TestAndFetchClaudeAsync(cleanKey);
                }
                else if (pid.Contains("deepseek"))
                {
                    return await TestAndFetchDeepSeekAsync(cleanKey);
                }
                else if (pid.Contains("groq"))
                {
                    return await TestAndFetchOpenAiCompatibleAsync(
                        endpoint: "https://api.groq.com/openai/v1/models",
                        apiKey: cleanKey,
                        providerKey: "groq",
                        providerName: "Groq"
                    );
                }
                else if (pid.Contains("openrouter"))
                {
                    return await TestAndFetchOpenRouterAsync(cleanKey);
                }
                else if (pid.Contains("perplexity") || pid.Contains("sonar"))
                {
                    return await TestAndFetchPerplexityAsync(cleanKey);
                }
                else if (pid.Contains("xai") || pid.Contains("grok"))
                {
                    return await TestAndFetchOpenAiCompatibleAsync(
                        endpoint: "https://api.x.ai/v1/models",
                        apiKey: cleanKey,
                        providerKey: "xai",
                        providerName: "xAI"
                    );
                }
                else if (pid.Contains("custom"))
                {
                    string baseUrl = ApiKeyService.Instance.CustomBaseUrl;
                    if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "http://localhost:11434/v1/models";
                    else if (!baseUrl.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
                    {
                        baseUrl = baseUrl.TrimEnd('/') + "/models";
                    }

                    return await TestAndFetchOpenAiCompatibleAsync(
                        endpoint: baseUrl,
                        apiKey: cleanKey,
                        providerKey: "custom",
                        providerName: "Custom Provider"
                    );
                }
                else
                {
                    // Default to OpenAI
                    return await TestAndFetchOpenAiAsync(cleanKey);
                }
            }
            catch (Exception ex)
            {
                return new ModelTestResult
                {
                    Success = false,
                    Message = $"Connection failed: {ex.Message}",
                    StatusCode = 0
                };
            }
        }

        private async Task<ModelTestResult> TestAndFetchGeminiAsync(string apiKey)
        {
            string url = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}";
            using (var resp = await _httpClient.GetAsync(url))
            {
                string json = await resp.Content.ReadAsStringAsync();
                int status = (int)resp.StatusCode;

                if (!resp.IsSuccessStatusCode)
                {
                    string errDetail = ExtractJsonErrorMessage(json) ?? resp.ReasonPhrase;
                    if (status == 429)
                    {
                        return new ModelTestResult
                        {
                            Success = false,
                            StatusCode = 429,
                            Message = $"Quota Exceeded (429): Google Gemini free tier quota reached. Check your Google AI Studio plan or use Gemini 1.5 Flash.\nDetail: {errDetail}"
                        };
                    }
                    if (status == 400 || status == 403)
                    {
                        return new ModelTestResult
                        {
                            Success = false,
                            StatusCode = status,
                            Message = $"Invalid API Key: {errDetail}"
                        };
                    }
                    return new ModelTestResult
                    {
                        Success = false,
                        StatusCode = status,
                        Message = $"Gemini Error ({status}): {errDetail}"
                    };
                }

                var list = new List<AiModelItem>();
                var obj = JObject.Parse(json);
                var models = obj["models"] as JArray;

                if (models != null)
                {
                    foreach (var m in models)
                    {
                        string name = m["name"]?.ToString(); // e.g. "models/gemini-1.5-pro"
                        string displayName = m["displayName"]?.ToString();
                        var methods = m["supportedGenerationMethods"] as JArray;

                        // Only include models that support generateContent (exclude embeddings, etc.)
                        if (methods != null && methods.Any(method => method.ToString() == "generateContent"))
                        {
                            string cleanId = name.StartsWith("models/") ? name.Substring(7) : name;
                            if (string.IsNullOrEmpty(displayName)) displayName = cleanId;

                            list.Add(new AiModelItem
                            {
                                Id = cleanId,
                                DisplayName = displayName,
                                ProviderName = "Google Gemini",
                                Description = cleanId
                            });
                        }
                    }
                }


                // Sort: put Flash models first (best quotas for free users), then Pro
                list = list.OrderByDescending(m => m.Id.Contains("flash"))
                           .ThenByDescending(m => m.Id.Contains("2.0"))
                           .ThenBy(m => m.DisplayName)
                           .ToList();

                lock (_providerModelsCache)
                {
                    _providerModelsCache["gemini"] = list;
                }
                await SaveCacheToDiskAsync();

                return new ModelTestResult
                {
                    Success = true,
                    StatusCode = 200,
                    Message = $"Connected to Google Gemini! Found {list.Count} available models (cached locally).",
                    Models = list
                };
            }
        }

        private async Task<ModelTestResult> TestAndFetchOpenAiAsync(string apiKey)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models"))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using (var resp = await _httpClient.SendAsync(req))
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    int status = (int)resp.StatusCode;

                    if (!resp.IsSuccessStatusCode)
                    {
                        string errDetail = ExtractJsonErrorMessage(json) ?? resp.ReasonPhrase;
                        if (status == 401)
                        {
                            return new ModelTestResult { Success = false, StatusCode = 401, Message = $"Invalid OpenAI API key. Check key on platform.openai.com." };
                        }
                        if (status == 429)
                        {
                            return new ModelTestResult { Success = false, StatusCode = 429, Message = $"OpenAI Quota Exceeded (429): Check credits/billing at platform.openai.com." };
                        }
                        return new ModelTestResult { Success = false, StatusCode = status, Message = $"OpenAI Error ({status}): {errDetail}" };
                    }

                    var list = new List<AiModelItem>();
                    var obj = JObject.Parse(json);
                    var data = obj["data"] as JArray;

                    if (data != null)
                    {
                        foreach (var item in data)
                        {
                            string id = item["id"]?.ToString();
                            if (string.IsNullOrEmpty(id)) continue;

                            // Filter for chat & reasoning models
                            if (id.StartsWith("gpt-4") || id.StartsWith("gpt-3.5") || id.StartsWith("o1") || id.StartsWith("o3") || id.StartsWith("chatgpt"))
                            {
                                if (!id.Contains("audio") && !id.Contains("realtime") && !id.Contains("embedding") && !id.Contains("moderation"))
                                {
                                    list.Add(new AiModelItem
                                    {
                                        Id = id,
                                        DisplayName = id,
                                        ProviderName = "OpenAI",
                                        Description = id
                                    });
                                }
                            }
                        }
                    }


                    list = list.OrderBy(m => m.DisplayName).ToList();

                    lock (_providerModelsCache)
                    {
                        _providerModelsCache["openai"] = list;
                    }
                    await SaveCacheToDiskAsync();

                    return new ModelTestResult
                    {
                        Success = true,
                        StatusCode = 200,
                        Message = $"Connected to OpenAI! Found {list.Count} models (cached locally).",
                        Models = list
                    };
                }
            }
        }

        private async Task<ModelTestResult> TestAndFetchClaudeAsync(string apiKey)
        {
            // Anthropic allows checking models via /v1/models with anthropic-version
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models"))
                {
                    req.Headers.Add("x-api-key", apiKey);
                    req.Headers.Add("anthropic-version", "2023-06-01");
                    using (var resp = await _httpClient.SendAsync(req))
                    {
                        string json = await resp.Content.ReadAsStringAsync();
                        if (resp.IsSuccessStatusCode)
                        {
                            var list = new List<AiModelItem>();
                            var obj = JObject.Parse(json);
                            var data = obj["data"] as JArray;
                            if (data != null)
                            {
                                foreach (var item in data)
                                {
                                    string id = item["id"]?.ToString();
                                    string name = item["display_name"]?.ToString() ?? id;
                                    if (!string.IsNullOrEmpty(id))
                                    {
                                        list.Add(new AiModelItem { Id = id, DisplayName = name, ProviderName = "Anthropic Claude" });
                                    }
                                }
                            }

                            if (list.Count > 0)
                            {
                                lock (_providerModelsCache) { _providerModelsCache["claude"] = list; }
                                await SaveCacheToDiskAsync();
                                return new ModelTestResult { Success = true, StatusCode = 200, Message = $"Connected to Anthropic! Found {list.Count} models.", Models = list };
                            }
                        }
                        else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized || resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            return new ModelTestResult { Success = false, StatusCode = (int)resp.StatusCode, Message = "Invalid Anthropic API key." };
                        }
                        else if ((int)resp.StatusCode == 429)
                        {
                            return new ModelTestResult { Success = false, StatusCode = 429, Message = "Anthropic Quota/Rate Limit Exceeded (429)." };
                        }
                    }
                }
            }
            catch
            {
                // Fallback to key validation
            }

            // Key syntax validation fallback for Anthropic
            if (apiKey.StartsWith("sk-ant-") && apiKey.Length > 20)
            {
                var list = new List<AiModelItem>
                {
                    new AiModelItem { Id = "claude-3-5-sonnet-20241022", DisplayName = "Claude 3.5 Sonnet", ProviderName = "Anthropic Claude" },
                    new AiModelItem { Id = "claude-3-5-haiku-20241022", DisplayName = "Claude 3.5 Haiku", ProviderName = "Anthropic Claude" },
                    new AiModelItem { Id = "claude-3-opus-20240229", DisplayName = "Claude 3 Opus", ProviderName = "Anthropic Claude" }
                };
                lock (_providerModelsCache) { _providerModelsCache["claude"] = list; }
                await SaveCacheToDiskAsync();
                return new ModelTestResult { Success = true, StatusCode = 200, Message = "Anthropic key saved! 3 standard models configured.", Models = list };
            }

            return new ModelTestResult { Success = false, StatusCode = 400, Message = "Anthropic API key format unrecognized (should start with sk-ant-)." };
        }

        private async Task<ModelTestResult> TestAndFetchDeepSeekAsync(string apiKey)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/models"))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using (var resp = await _httpClient.SendAsync(req))
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    int status = (int)resp.StatusCode;

                    if (!resp.IsSuccessStatusCode)
                    {
                        string err = ExtractJsonErrorMessage(json) ?? resp.ReasonPhrase;
                        if (status == 401) return new ModelTestResult { Success = false, StatusCode = 401, Message = "Invalid DeepSeek API key." };
                        if (status == 402 || status == 429) return new ModelTestResult { Success = false, StatusCode = status, Message = "DeepSeek Insufficient Balance or Rate Limit (429)." };
                        return new ModelTestResult { Success = false, StatusCode = status, Message = $"DeepSeek Error ({status}): {err}" };
                    }

                    var list = new List<AiModelItem>();
                    var obj = JObject.Parse(json);
                    var data = obj["data"] as JArray;
                    if (data != null)
                    {
                        foreach (var item in data)
                        {
                            string id = item["id"]?.ToString();
                            if (!string.IsNullOrEmpty(id))
                            {
                                string name = id;
                                if (id == "deepseek-chat") name = "DeepSeek-V3 (Chat)";
                                else if (id == "deepseek-reasoner") name = "DeepSeek-R1 (Reasoner)";

                                list.Add(new AiModelItem { Id = id, DisplayName = name, ProviderName = "DeepSeek" });
                            }
                        }
                    }

                    if (list.Count == 0)
                    {
                        list.Add(new AiModelItem { Id = "deepseek-chat", DisplayName = "DeepSeek-V3 (Chat)", ProviderName = "DeepSeek" });
                        list.Add(new AiModelItem { Id = "deepseek-reasoner", DisplayName = "DeepSeek-R1 (Reasoner)", ProviderName = "DeepSeek" });
                    }

                    lock (_providerModelsCache) { _providerModelsCache["deepseek"] = list; }
                    await SaveCacheToDiskAsync();

                    return new ModelTestResult { Success = true, StatusCode = 200, Message = $"Connected to DeepSeek! Found {list.Count} models.", Models = list };
                }
            }
        }

        private async Task<ModelTestResult> TestAndFetchPerplexityAsync(string apiKey)
        {
            var list = new List<AiModelItem>
            {
                new AiModelItem { Id = "sonar", DisplayName = "Sonar (Search)", ProviderName = "Perplexity AI" },
                new AiModelItem { Id = "sonar-pro", DisplayName = "Sonar Pro (Search)", ProviderName = "Perplexity AI" },
                new AiModelItem { Id = "sonar-reasoning", DisplayName = "Sonar Reasoning", ProviderName = "Perplexity AI" }
            };

            lock (_providerModelsCache) { _providerModelsCache["perplexity"] = list; }
            await SaveCacheToDiskAsync();

            return new ModelTestResult
            {
                Success = true,
                StatusCode = 200,
                Message = "Perplexity AI configured with 3 models (Sonar, Sonar Pro, Reasoning).",
                Models = list
            };
        }

        private async Task<ModelTestResult> TestAndFetchOpenRouterAsync(string apiKey)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models"))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using (var resp = await _httpClient.SendAsync(req))
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                    {
                        return new ModelTestResult { Success = false, StatusCode = (int)resp.StatusCode, Message = $"OpenRouter Error: {ExtractJsonErrorMessage(json) ?? resp.ReasonPhrase}" };
                    }

                    var list = new List<AiModelItem>();
                    var obj = JObject.Parse(json);
                    var data = obj["data"] as JArray;
                    if (data != null)
                    {
                        var rawItems = new List<AiModelItem>();
                        foreach (var item in data)
                        {
                            string id = item["id"]?.ToString();
                            string name = item["name"]?.ToString() ?? id;
                            if (!string.IsNullOrEmpty(id))
                            {
                                var caps = ModelCapabilities.Text;
                                var arch = item["architecture"];
                                if (arch != null)
                                {
                                    var inMods = arch["input_modalities"] as JArray;
                                    if (inMods != null && inMods.Any(m => m.ToString().Equals("image", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        caps |= ModelCapabilities.Vision;
                                    }
                                    string mod = arch["modality"]?.ToString() ?? "";
                                    if (mod.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        caps |= ModelCapabilities.Vision;
                                    }
                                }

                                if (id.IndexOf("vision", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    id.IndexOf("-vl-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    id.IndexOf("vl:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    id.IndexOf("omni", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    caps |= ModelCapabilities.Vision;
                                }

                                if (id.IndexOf("r1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    id.IndexOf("reason", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    id.IndexOf("o1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    id.IndexOf("o3", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    caps |= ModelCapabilities.Reasoning;
                                }

                                rawItems.Add(new AiModelItem
                                {
                                    Id = id,
                                    DisplayName = name,
                                    ProviderName = "OpenRouter",
                                    Capabilities = caps
                                });
                            }
                        }

                        // Prioritize free models (:free) at the top so the user can immediately use them without cost!
                        list = rawItems.OrderByDescending(m => m.Id.EndsWith(":free") || m.DisplayName.ToLowerInvariant().Contains("free"))
                                       .ThenBy(m => m.DisplayName)
                                       .Take(60)
                                       .ToList();
                    }

                    lock (_providerModelsCache) { _providerModelsCache["openrouter"] = list; }
                    await SaveCacheToDiskAsync();
                    return new ModelTestResult { Success = true, StatusCode = 200, Message = $"Connected to OpenRouter! Cached {list.Count} models.", Models = list };
                }
            }
        }

        private async Task<ModelTestResult> TestAndFetchOpenAiCompatibleAsync(string endpoint, string apiKey, string providerKey, string providerName)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, endpoint))
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }
                using (var resp = await _httpClient.SendAsync(req))
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                    {
                        return new ModelTestResult { Success = false, StatusCode = (int)resp.StatusCode, Message = $"{providerName} Error: {ExtractJsonErrorMessage(json) ?? resp.ReasonPhrase}" };
                    }

                    var list = new List<AiModelItem>();
                    var obj = JObject.Parse(json);
                    var data = obj["data"] as JArray;
                    if (data != null)
                    {
                        foreach (var item in data)
                        {
                            string id = item["id"]?.ToString();
                            if (!string.IsNullOrEmpty(id))
                            {
                                list.Add(new AiModelItem { Id = id, DisplayName = id, ProviderName = providerName });
                            }
                        }
                    }

                    lock (_providerModelsCache) { _providerModelsCache[providerKey] = list; }
                    await SaveCacheToDiskAsync();
                    return new ModelTestResult { Success = true, StatusCode = 200, Message = $"Connected to {providerName}! Cached {list.Count} models.", Models = list };
                }
            }
        }

        private static string ExtractJsonErrorMessage(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return null;
                var obj = JObject.Parse(json);
                return obj["error"]?["message"]?.ToString() 
                    ?? obj["error"]?.ToString() 
                    ?? obj["message"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Legacy Full Refresh Support

        public async Task<List<AiModelItem>> GetAvailableModelsAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _allCachedModels.Count > 0 && (DateTime.Now - _lastFetchTime).TotalMinutes < 60)
            {
                return _allCachedModels;
            }

            var models = new List<AiModelItem>();

            // Aggregate from local cache for all configured providers
            if (!string.IsNullOrWhiteSpace(_apiKeyService.OpenAiKey) || CredentialVaultService.Instance.HasApiKey("openai"))
                models.AddRange(GetModelsForProvider("openai"));

            if (!string.IsNullOrWhiteSpace(_apiKeyService.GeminiKey) || CredentialVaultService.Instance.HasApiKey("gemini"))
                models.AddRange(GetModelsForProvider("gemini"));

            if (!string.IsNullOrWhiteSpace(_apiKeyService.ClaudeKey) || CredentialVaultService.Instance.HasApiKey("claude"))
                models.AddRange(GetModelsForProvider("claude"));

            if (!string.IsNullOrWhiteSpace(_apiKeyService.DeepSeekKey) || CredentialVaultService.Instance.HasApiKey("deepseek"))
                models.AddRange(GetModelsForProvider("deepseek"));

            if (!string.IsNullOrWhiteSpace(_apiKeyService.PerplexityKey) || CredentialVaultService.Instance.HasApiKey("perplexity"))
                models.AddRange(GetModelsForProvider("perplexity"));

            if (!string.IsNullOrWhiteSpace(_apiKeyService.GroqKey) || CredentialVaultService.Instance.HasApiKey("groq"))
                models.AddRange(GetModelsForProvider("groq"));

            if (!string.IsNullOrWhiteSpace(_apiKeyService.OpenRouterKey) || CredentialVaultService.Instance.HasApiKey("openrouter"))
                models.AddRange(GetModelsForProvider("openrouter"));

            if (!string.IsNullOrWhiteSpace(_apiKeyService.XAiKey) || CredentialVaultService.Instance.HasApiKey("xai"))
                models.AddRange(GetModelsForProvider("xai"));

            if (models.Count == 0)
            {
                // Fallback to OpenAI defaults if no keys configured
                models.AddRange(GetModelsForProvider("openai"));
            }

            _allCachedModels = models;
            _lastFetchTime = DateTime.Now;

            return _allCachedModels;
        }

        #endregion
    }
}
