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
    public sealed class OpenAiProvider : BaseAiProvider
    {
        public override string Id => "openai";
        public override string DisplayName => "OpenAI";

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
                    if (item.Id.Contains("gpt-4o")) caps |= ModelCapabilities.Vision;
                    if (item.Id.StartsWith("o1") || item.Id.StartsWith("o3")) caps |= ModelCapabilities.Reasoning;

                    var desc = new AiModelDescriptor(item.Id, item.DisplayName, Id, DisplayName, caps, configured, item.Description);
                    desc.IsAvailable = item.IsAvailable;
                    desc.Status = item.Status;
                    list.Add(desc);
                }
            }

            return list;
        }

        public override async Task<string> SendMessageAsync(AiModelDescriptor model, List<ChatMessage> conversationHistory)
        {
            string apiKey = GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("OpenAI API key is missing. Please configure it in Key Vault.");
            }

            string cleanModel = model?.Id?.Trim() ?? "gpt-4o";
            if (cleanModel.Equals("openai", StringComparison.OrdinalIgnoreCase)) cleanModel = "gpt-4o";

            var messagesPayload = new List<object>();

            // Include system prompt with accurate real-time date
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
                string role = msg.IsUser ? "user" : "assistant";

                if (msg.IsUser && msg.HasImage && model.SupportsVision)
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

                    messagesPayload.Add(new { role, content = contentParts });
                }
                else
                {
                    messagesPayload.Add(new
                    {
                        role,
                        content = msg.Text ?? string.Empty
                    });
                }
            }

            var requestBody = new
            {
                model = cleanModel,
                messages = messagesPayload,
                max_tokens = 2048,
                temperature = 0.7
            };

            string jsonContent = JsonConvert.SerializeObject(requestBody);

            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions"))
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
                        throw new Exception("Received empty response from OpenAI.");
                    }
                    return content.Trim();
                }
            }
        }
    }
}
