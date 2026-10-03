using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Networking.Connectivity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WinAI.Models;
using WinAI.Services.Providers;

namespace WinAI.Services
{
    /// <summary>
    /// Service responsible for dispatching asynchronous chat requests to AI REST APIs.
    /// Supports OpenAI, Google Gemini, Anthropic Claude, DeepSeek, Groq, OpenRouter, Mistral, xAI, and Custom endpoints.
    /// </summary>
    public sealed class AiChatService
    {
        private static readonly Lazy<AiChatService> _instance = new Lazy<AiChatService>(() => new AiChatService());
        public static AiChatService Instance => _instance.Value;

        private readonly HttpClient _httpClient;
        private readonly ApiKeyService _apiKeyService = ApiKeyService.Instance;

        private AiChatService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60) // Generous timeout for reasoning models (DeepSeek-R1 / o1)
            };
        }

        public bool HasInternetConnection()
        {
            try
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                return profile != null && profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
            }
            catch
            {
                return true; // Assume true if check fails to prevent blocking on custom networks
            }
        }

        public async Task<string> SendMessageAsync(AiModelDescriptor model, List<ChatMessage> conversationHistory)
        {
            if (model == null)
            {
                throw new InvalidOperationException("No AI model is currently selected.");
            }

            var provider = AiProviderRegistry.Instance.GetProvider(model.ProviderId);
            if (provider != null)
            {
                return await provider.SendMessageAsync(model, conversationHistory);
            }

            return await SendMessageAsync(model.ToModelItem(), conversationHistory);
        }

        public async Task<string> SendMessageAsync(string providerId, string modelId, List<ChatMessage> conversationHistory)
        {
            var descriptor = AiProviderRegistry.Instance.GetModel(providerId, modelId);
            if (descriptor == null)
            {
                descriptor = new AiModelDescriptor(modelId, modelId, providerId, providerId);
            }
            return await SendMessageAsync(descriptor, conversationHistory);
        }

        public async Task<string> SendMessageAsync(AiModelItem model, List<ChatMessage> conversationHistory)
        {
            if (model == null)
            {
                throw new InvalidOperationException("No AI model is currently selected.");
            }

            if (!HasInternetConnection())
            {
                throw new HttpRequestException("No internet connection detected. Please check your Wi-Fi or cellular data.");
            }

            string provider = model.ProviderName?.ToLowerInvariant() ?? "";

            if (provider.Contains("gemini") || provider.Contains("google"))
            {
                return await CallGeminiAsync(model.Id, conversationHistory);
            }
            else if (provider.Contains("anthropic") || provider.Contains("claude"))
            {
                return await CallAnthropicAsync(model.Id, conversationHistory);
            }
            else if (provider.Contains("deepseek"))
            {
                return await CallOpenAiCompatibleAsync(
                    endpoint: "https://api.deepseek.com/chat/completions",
                    apiKey: _apiKeyService.DeepSeekKey,
                    modelId: model.Id,
                    history: conversationHistory,
                    providerName: "DeepSeek"
                );
            }
            else if (provider.Contains("groq"))
            {
                return await CallOpenAiCompatibleAsync(
                    endpoint: "https://api.groq.com/openai/v1/chat/completions",
                    apiKey: _apiKeyService.GroqKey,
                    modelId: model.Id,
                    history: conversationHistory,
                    providerName: "Groq"
                );
            }
            else if (provider.Contains("openrouter"))
            {
                return await CallOpenAiCompatibleAsync(
                    endpoint: "https://openrouter.ai/api/v1/chat/completions",
                    apiKey: _apiKeyService.OpenRouterKey,
                    modelId: model.Id,
                    history: conversationHistory,
                    providerName: "OpenRouter"
                );
            }
            else if (provider.Contains("mistral"))
            {
                return await CallOpenAiCompatibleAsync(
                    endpoint: "https://api.mistral.ai/v1/chat/completions",
                    apiKey: _apiKeyService.MistralKey,
                    modelId: model.Id,
                    history: conversationHistory,
                    providerName: "Mistral"
                );
            }
            else if (provider.Contains("xai"))
            {
                return await CallOpenAiCompatibleAsync(
                    endpoint: "https://api.x.ai/v1/chat/completions",
                    apiKey: _apiKeyService.XAiKey,
                    modelId: model.Id,
                    history: conversationHistory,
                    providerName: "xAI"
                );
            }
            else if (provider.Contains("perplexity"))
            {
                return await CallOpenAiCompatibleAsync(
                    endpoint: "https://api.perplexity.ai/chat/completions",
                    apiKey: _apiKeyService.PerplexityKey,
                    modelId: model.Id,
                    history: conversationHistory,
                    providerName: "Perplexity"
                );
            }
            else if (provider.Contains("custom"))
            {
                string baseUrl = _apiKeyService.CustomBaseUrl.TrimEnd('/');
                string endpoint = baseUrl.EndsWith("/v1")
                    ? $"{baseUrl}/chat/completions"
                    : $"{baseUrl}/v1/chat/completions";

                return await CallOpenAiCompatibleAsync(
                    endpoint: endpoint,
                    apiKey: _apiKeyService.CustomKey,
                    modelId: model.Id,
                    history: conversationHistory,
                    providerName: "Custom Endpoint"
                );
            }
            else
            {
                // Default to OpenAI
                return await CallOpenAiCompatibleAsync(
                    endpoint: "https://api.openai.com/v1/chat/completions",
                    apiKey: _apiKeyService.OpenAiKey,
                    modelId: model.Id,
                    history: conversationHistory,
                    providerName: "OpenAI"
                );
            }
        }

        #region OpenAI & Compatible Providers

        private async Task<string> CallOpenAiCompatibleAsync(
            string endpoint,
            string apiKey,
            string modelId,
            List<ChatMessage> history,
            string providerName)
        {
            if (string.IsNullOrWhiteSpace(apiKey) && !providerName.Contains("Custom"))
            {
                throw new InvalidOperationException($"No API key found for {providerName}. Please enter your key in Settings.");
            }

            var messagesPayload = new List<object>();

            // Include system instruction
            messagesPayload.Add(new
            {
                role = "system",
                content = "You are a helpful, intelligent AI assistant running inside WinAI on Windows 10 Mobile."
            });

            // Include last 12 messages for conversation context
            var contextMessages = history.Skip(Math.Max(0, history.Count - 12));
            foreach (var msg in contextMessages)
            {
                if (msg.IsUser && msg.HasImage)
                {
                    var contentParts = new List<object>();
                    if (!string.IsNullOrWhiteSpace(msg.Text))
                    {
                        contentParts.Add(new { type = "text", text = msg.Text });
                    }
                    contentParts.Add(new
                    {
                        type = "image_url",
                        image_url = new
                        {
                            url = $"data:{msg.ImageMimeType ?? "image/jpeg"};base64,{msg.ImageBase64}"
                        }
                    });

                    messagesPayload.Add(new
                    {
                        role = "user",
                        content = contentParts
                    });
                }
                else
                {
                    messagesPayload.Add(new
                    {
                        role = msg.IsUser ? "user" : "assistant",
                        content = msg.Text ?? ""
                    });
                }
            }

            var requestBody = new
            {
                model = modelId,
                messages = messagesPayload
            };

            string jsonContent = JsonConvert.SerializeObject(requestBody);

            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                }

                if (providerName == "OpenRouter")
                {
                    request.Headers.Add("HTTP-Referer", "https://github.com/winai");
                    request.Headers.Add("X-Title", "WinAI for Windows 10 Mobile");
                }

                HttpResponseMessage response = await _httpClient.SendAsync(request);
                string responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    HandleApiError(response.StatusCode, responseString, providerName);
                }

                var json = JObject.Parse(responseString);
                var content = json["choices"]?[0]?["message"]?["content"]?.ToString();

                if (string.IsNullOrEmpty(content))
                {
                    throw new Exception("Received empty response from the AI provider.");
                }

                return content.Trim();
            }
        }

        #endregion

        #region Google Gemini

        private async Task<string> CallGeminiAsync(string modelId, List<ChatMessage> history)
        {
            string apiKey = _apiKeyService.GeminiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("No API key found for Google Gemini. Please enter your key in Settings.");
            }

            string cleanModel = modelId?.Trim() ?? "";
            if (cleanModel.StartsWith("models/")) cleanModel = cleanModel.Substring(7);

            // Normalize common aliases to valid Gemini API model identifiers
            if (string.IsNullOrWhiteSpace(cleanModel) || cleanModel.Equals("gemini", StringComparison.OrdinalIgnoreCase))
            {
                cleanModel = "gemini-1.5-flash";
            }
            else if (cleanModel.Equals("Gemini 1.5 Pro", StringComparison.OrdinalIgnoreCase) || cleanModel.Equals("gemini-pro", StringComparison.OrdinalIgnoreCase))
            {
                cleanModel = "gemini-1.5-pro";
            }
            else if (cleanModel.Equals("Gemini 1.5 Flash", StringComparison.OrdinalIgnoreCase))
            {
                cleanModel = "gemini-1.5-flash";
            }
            else if (cleanModel.Equals("Gemini 2.0 Flash", StringComparison.OrdinalIgnoreCase))
            {
                cleanModel = "gemini-2.0-flash";
            }

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{cleanModel}:generateContent?key={apiKey.Trim()}";

            var contentsPayload = new List<object>();

            var contextMessages = history.Skip(Math.Max(0, history.Count - 12));
            foreach (var msg in contextMessages)
            {
                var partsList = new List<object>();
                if (!string.IsNullOrWhiteSpace(msg.Text))
                {
                    partsList.Add(new { text = msg.Text });
                }

                if (msg.IsUser && msg.HasImage)
                {
                    partsList.Add(new
                    {
                        inline_data = new
                        {
                            mime_type = msg.ImageMimeType ?? "image/jpeg",
                            data = msg.ImageBase64
                        }
                    });
                }

                if (partsList.Count == 0)
                {
                    partsList.Add(new { text = "" });
                }

                contentsPayload.Add(new
                {
                    role = msg.IsUser ? "user" : "model",
                    parts = partsList
                });
            }

            var requestBody = new
            {
                contents = contentsPayload
            };

            string jsonContent = JsonConvert.SerializeObject(requestBody);

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.SendAsync(request);
                string responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    HandleApiError(response.StatusCode, responseString, "Google Gemini");
                }

                var json = JObject.Parse(responseString);
                var textPart = json["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

                if (string.IsNullOrEmpty(textPart))
                {
                    throw new Exception("Received empty response from Google Gemini.");
                }

                return textPart.Trim();
            }
        }

        #endregion

        #region Anthropic Claude

        private async Task<string> CallAnthropicAsync(string modelId, List<ChatMessage> history)
        {
            string apiKey = _apiKeyService.ClaudeKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("No API key found for Anthropic Claude. Please enter your key in Settings.");
            }

            string url = "https://api.anthropic.com/v1/messages";

            var messagesPayload = new List<object>();

            var contextMessages = history.Skip(Math.Max(0, history.Count - 12));
            foreach (var msg in contextMessages)
            {
                if (msg.IsUser && msg.HasImage)
                {
                    var contentParts = new List<object>();
                    contentParts.Add(new
                    {
                        type = "image",
                        source = new
                        {
                            type = "base64",
                            media_type = msg.ImageMimeType ?? "image/jpeg",
                            data = msg.ImageBase64
                        }
                    });

                    if (!string.IsNullOrWhiteSpace(msg.Text))
                    {
                        contentParts.Add(new { type = "text", text = msg.Text });
                    }

                    messagesPayload.Add(new
                    {
                        role = "user",
                        content = contentParts
                    });
                }
                else
                {
                    messagesPayload.Add(new
                    {
                        role = msg.IsUser ? "user" : "assistant",
                        content = msg.Text ?? ""
                    });
                }
            }

            var requestBody = new
            {
                model = modelId,
                max_tokens = 2048,
                system = "You are a helpful, concise AI assistant for Windows 10 Mobile.",
                messages = messagesPayload
            };

            string jsonContent = JsonConvert.SerializeObject(requestBody);

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                request.Headers.Add("x-api-key", apiKey.Trim());
                request.Headers.Add("anthropic-version", "2023-06-01");

                HttpResponseMessage response = await _httpClient.SendAsync(request);
                string responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    HandleApiError(response.StatusCode, responseString, "Anthropic Claude");
                }

                var json = JObject.Parse(responseString);
                var contentText = json["content"]?[0]?["text"]?.ToString();

                if (string.IsNullOrEmpty(contentText))
                {
                    throw new Exception("Received empty response from Anthropic Claude.");
                }

                return contentText.Trim();
            }
        }

        #endregion

        #region Error Handling

        private static string SanitizeErrorDetail(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail)) return detail;
            string scrubbed = Regex.Replace(detail, @"sk-ant-[a-zA-Z0-9_\-]{20,}", "[REDACTED_KEY]");
            scrubbed = Regex.Replace(scrubbed, @"sk-[a-zA-Z0-9_\-]{20,}", "[REDACTED_KEY]");
            scrubbed = Regex.Replace(scrubbed, @"AIza[a-zA-Z0-9_\-]{20,}", "[REDACTED_KEY]");
            return scrubbed;
        }

        private void HandleApiError(System.Net.HttpStatusCode statusCode, string responseBody, string providerName)
        {
            string errorMessage = $"HTTP {(int)statusCode} from {providerName}.";

            try
            {
                var errorObj = JObject.Parse(responseBody);
                string detail = errorObj["error"]?["message"]?.ToString()
                             ?? errorObj["error"]?.ToString()
                             ?? errorObj["message"]?.ToString();

                if (!string.IsNullOrWhiteSpace(detail))
                {
                    errorMessage = $"{providerName} Error: {SanitizeErrorDetail(detail)}";
                }
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(responseBody) && responseBody.Length < 200)
                {
                    errorMessage = $"{providerName} Error: {SanitizeErrorDetail(responseBody)}";
                }
            }

            if (statusCode == System.Net.HttpStatusCode.Unauthorized || statusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException($"Invalid or unauthorized API key for {providerName}. Please verify your key in Settings.\n\nDetail: {errorMessage}");
            }
            else if ((int)statusCode == 429)
            {
                string hint = providerName.Contains("Gemini")
                    ? "\n\nTip: Gemini 1.5 Flash has higher free quota than 1.5 Pro. You can select Gemini 1.5 Flash from the top model menu."
                    : "\n\nTip: Check your billing credits and account tier on the provider's developer dashboard.";
                throw new InvalidOperationException($"Rate limit or quota reached on {providerName}.{hint}\n\nDetail: {errorMessage}");
            }
            else
            {
                throw new HttpRequestException(errorMessage);
            }
        }

        #endregion
    }
}
