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
    public sealed class GrokProvider : BaseAiProvider
    {
        public override string Id => "xai";
        public override string DisplayName => "xAI (Grok)";

        public override IReadOnlyList<AiModelDescriptor> GetModels()
        {
            bool configured = IsConfigured;
            var list = new List<AiModelDescriptor>();

            var cached = ModelService.Instance.GetModelsForProvider(Id);
            if (cached != null && cached.Count > 0)
            {
                foreach (var item in cached)
                {
                    var caps = ModelCapabilities.Text;
                    if (item.Id.Contains("vision")) caps |= ModelCapabilities.Vision;
                    list.Add(new AiModelDescriptor(item.Id, item.DisplayName, Id, DisplayName, caps, configured, item.Description));
                }
            }
            else
            {
                list.Add(new AiModelDescriptor("grok-2-1212", "Grok 2", Id, DisplayName, ModelCapabilities.Text, configured));
                list.Add(new AiModelDescriptor("grok-2-vision-1212", "Grok 2 Vision", Id, DisplayName, ModelCapabilities.Text | ModelCapabilities.Vision, configured));
                list.Add(new AiModelDescriptor("grok-beta", "Grok Beta", Id, DisplayName, ModelCapabilities.Text, configured));
            }

            return list;
        }

        public override async Task<string> SendMessageAsync(AiModelDescriptor model, List<ChatMessage> conversationHistory)
        {
            string apiKey = GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("xAI (Grok) API key is missing. Please configure it in Key Vault.");
            }

            string cleanModel = model?.Id?.Trim() ?? "grok-2-1212";
            if (cleanModel.Equals("xai", StringComparison.OrdinalIgnoreCase) || cleanModel.Equals("grok", StringComparison.OrdinalIgnoreCase))
            {
                cleanModel = "grok-2-1212";
            }

            var messagesPayload = new List<object>();
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

            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.ai/v1/chat/completions"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
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
                        throw new Exception("Received empty response from xAI Grok.");
                    }
                    return content.Trim();
                }
            }
        }
    }
}
