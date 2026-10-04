using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WinAI.Models;

namespace WinAI.Services.Providers
{
    public sealed class CustomAiProvider : BaseAiProvider
    {
        public override string Id => "custom";
        public override string DisplayName => "Custom Provider";

        public override bool IsConfigured
        {
            get
            {
                return CredentialVaultService.Instance.HasApiKey(Id) ||
                       !string.IsNullOrWhiteSpace(ApiKeyService.Instance.CustomKey) ||
                       !string.IsNullOrWhiteSpace(ApiKeyService.Instance.CustomBaseUrl);
            }
        }

        private string GetCustomApiKey()
        {
            string key = CredentialVaultService.Instance.GetApiKey(Id);
            if (!string.IsNullOrWhiteSpace(key)) return key;
            return ApiKeyService.Instance.CustomKey;
        }

        public override IReadOnlyList<AiModelDescriptor> GetModels()
        {
            bool configured = IsConfigured;
            var list = new List<AiModelDescriptor>();

            var cached = ModelService.Instance.GetModelsForProvider(Id);
            if (cached != null && cached.Count > 0)
            {
                foreach (var item in cached)
                {
                    var desc = new AiModelDescriptor(item.Id, item.DisplayName, Id, DisplayName, item.Capabilities, configured, item.Description);
                    desc.IsAvailable = item.IsAvailable;
                    desc.Status = item.Status;
                    list.Add(desc);
                }
            }
            else
            {
                list.Add(new AiModelDescriptor("custom-model", "Custom Model", Id, DisplayName, ModelCapabilities.Text, configured)
                {
                    IsAvailable = true,
                    Status = "Available"
                });
            }

            return list;
        }

        public override async Task<string> SendMessageAsync(AiModelDescriptor model, List<ChatMessage> conversationHistory)
        {
            string apiKey = GetCustomApiKey();
            string baseUrl = ApiKeyService.Instance.CustomBaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "http://localhost:11434/v1/chat/completions";
            }
            else if (!baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl.TrimEnd('/') + "/chat/completions";
            }

            string cleanModel = model?.Id?.Trim() ?? "custom-model";

            var messagesPayload = new List<object>();
            string currentDateTimeStr = DateTime.Now.ToString("dddd, MMMM d, yyyy h:mm tt");
            string timeZoneStr = TimeZoneInfo.Local.DisplayName;
            messagesPayload.Add(new
            {
                role = "system",
                content = $"You are WinAI, an intelligent and helpful AI assistant running on Windows 10 Mobile. The accurate real-time date and time is {currentDateTimeStr} ({timeZoneStr}). Always use this exact current date and time when asked about today's date, day, year, or time. If asked about live breaking news, live sports match scores, or real-time events that occurred after your knowledge training cutoff, state your knowledge cutoff date honestly and advise checking the browser for live updates, rather than guessing or denying that events took place."
            });

            var context = conversationHistory.Skip(Math.Max(0, conversationHistory.Count - 16));
            foreach (var msg in context)
            {
                messagesPayload.Add(new
                {
                    role = msg.IsUser ? "user" : "assistant",
                    content = msg.Text ?? string.Empty
                });
            }

            var requestBody = new
            {
                model = cleanModel,
                messages = messagesPayload,
                max_tokens = 2048,
                temperature = 0.7
            };

            string jsonContent = JsonConvert.SerializeObject(requestBody);

            using (var request = new HttpRequestMessage(HttpMethod.Post, baseUrl))
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                }
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using (var response = await HttpClient.SendAsync(request))
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        HandleApiError(response.StatusCode, responseString, DisplayName);
                    }

                    var json = JObject.Parse(responseString);
                    var content = json["choices"]?[0]?["message"]?["content"]?.ToString();
                    if (string.IsNullOrEmpty(content))
                    {
                        throw new Exception("Received empty response from Custom Provider.");
                    }
                    return content.Trim();
                }
            }
        }
    }
}
