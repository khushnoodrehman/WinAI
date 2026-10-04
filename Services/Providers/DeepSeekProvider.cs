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
    public sealed class DeepSeekProvider : BaseAiProvider
    {
        public override string Id => "deepseek";
        public override string DisplayName => "DeepSeek";

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
                    if (item.Id.Contains("reasoner") || item.Id.Contains("r1")) caps |= ModelCapabilities.Reasoning;
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
                throw new InvalidOperationException("DeepSeek API key is missing. Please configure it in Key Vault.");
            }

            string cleanModel = model?.Id?.Trim() ?? "deepseek-chat";
            if (cleanModel.Equals("deepseek", StringComparison.OrdinalIgnoreCase)) cleanModel = "deepseek-chat";

            var messagesPayload = new List<object>();
            string currentDateTimeStr = DateTime.Now.ToString("dddd, MMMM d, yyyy h:mm tt");
            string timeZoneStr = TimeZoneInfo.Local.DisplayName;
            messagesPayload.Add(new
            {
                role = "system",
                content = $"You are DeepSeek, an AI assistant accessible via WinAI on Windows 10 Mobile. The accurate real-time date and time is {currentDateTimeStr} ({timeZoneStr}). Always use this exact current date and time when asked about today's date, day, year, or time. If asked about live breaking news, live sports match scores, or real-time events that occurred after your knowledge training cutoff, state your knowledge cutoff date honestly and advise checking the browser for live updates, rather than guessing or denying that events took place."
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

            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions"))
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
                        throw new Exception("Received empty response from DeepSeek.");
                    }
                    return content.Trim();
                }
            }
        }
    }
}
