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
    public sealed class ClaudeProvider : BaseAiProvider
    {
        public override string Id => "claude";
        public override string DisplayName => "Anthropic Claude";

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
                throw new InvalidOperationException("Anthropic Claude API key is missing. Please configure it in Key Vault.");
            }

            string cleanModel = model?.Id?.Trim() ?? "claude-3-7-sonnet-20250219";
            if (cleanModel.Equals("claude", StringComparison.OrdinalIgnoreCase)) cleanModel = "claude-3-7-sonnet-20250219";

            var messagesPayload = new List<object>();

            var context = conversationHistory.Skip(Math.Max(0, conversationHistory.Count - 16));
            foreach (var msg in context)
            {
                string role = msg.IsUser ? "user" : "assistant";

                if (msg.IsUser && msg.HasImage)
                {
                    var contentParts = new List<object>();
                    if (!string.IsNullOrWhiteSpace(msg.Text))
                    {
                        contentParts.Add(new { type = "text", text = msg.Text });
                    }
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

            string currentDateTimeStr = DateTime.Now.ToString("dddd, MMMM d, yyyy h:mm tt");
            string timeZoneStr = TimeZoneInfo.Local.DisplayName;
            string systemPrompt = $"You are Claude, an AI assistant accessible via WinAI on Windows 10 Mobile. The accurate real-time date and time is {currentDateTimeStr} ({timeZoneStr}). Always use this exact current date and time when asked about today's date, day, year, or time. If asked about live breaking news, live sports match scores, or real-time events that occurred after your knowledge training cutoff, state your knowledge cutoff date honestly and advise checking the browser for live updates, rather than guessing or denying that events took place.";

            var requestBody = new
            {
                model = cleanModel,
                system = systemPrompt,
                max_tokens = 2048,
                messages = messagesPayload
            };

            string jsonContent = JsonConvert.SerializeObject(requestBody);

            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages"))
            {
                request.Headers.Add("x-api-key", apiKey.Trim());
                request.Headers.Add("anthropic-version", "2023-06-01");
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using (var response = await HttpClient.SendAsync(request))
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        HandleApiError(response.StatusCode, responseString, DisplayName);
                    }

                    var json = JObject.Parse(responseString);
                    var text = json["content"]?[0]?["text"]?.ToString();
                    if (string.IsNullOrEmpty(text))
                    {
                        throw new Exception("Received empty response from Anthropic Claude.");
                    }
                    return text.Trim();
                }
            }
        }
    }
}
