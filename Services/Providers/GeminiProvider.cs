using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WinAI.Models;

namespace WinAI.Services.Providers
{
    public sealed class GeminiProvider : BaseAiProvider
    {
        public override string Id => "gemini";
        public override string DisplayName => "Google Gemini";

        public override IReadOnlyList<AiModelDescriptor> GetModels()
        {
            bool configured = IsConfigured;
            var list = new List<AiModelDescriptor>();

            var cached = ModelService.Instance.GetModelsForProvider(Id);
            if (cached != null && cached.Count > 0)
            {
                foreach (var item in cached)
                {
                    var caps = ModelCapabilities.Text | ModelCapabilities.Vision;
                    if (item.Id.Contains("pro")) caps |= ModelCapabilities.Reasoning;

                    list.Add(new AiModelDescriptor(item.Id, item.DisplayName, Id, DisplayName, caps, configured, item.Description));
                }
            }
            else
            {
                list.Add(new AiModelDescriptor("gemini-1.5-flash", "Gemini 1.5 Flash (Fast/High Quota)", Id, DisplayName, ModelCapabilities.Text | ModelCapabilities.Vision, configured));
                list.Add(new AiModelDescriptor("gemini-2.0-flash", "Gemini 2.0 Flash", Id, DisplayName, ModelCapabilities.Text | ModelCapabilities.Vision, configured));
                list.Add(new AiModelDescriptor("gemini-1.5-pro", "Gemini 1.5 Pro (Reasoning)", Id, DisplayName, ModelCapabilities.Text | ModelCapabilities.Vision | ModelCapabilities.Reasoning, configured));
            }

            return list;
        }

        public override async Task<string> SendMessageAsync(AiModelDescriptor model, List<ChatMessage> conversationHistory)
        {
            string apiKey = GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Google Gemini API key is missing. Please configure it in Key Vault.");
            }

            string cleanModel = model?.Id?.Trim() ?? "";
            if (cleanModel.StartsWith("models/")) cleanModel = cleanModel.Substring(7);

            // Normalize common aliases to prevent "models/gemini not found"
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

            var context = conversationHistory.Skip(Math.Max(0, conversationHistory.Count - 16));
            foreach (var msg in context)
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

                using (var response = await HttpClient.SendAsync(request))
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        HandleApiError(response.StatusCode, responseString, DisplayName);
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
        }
    }
}
