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

            string currentDateTimeStr = DateTime.Now.ToString("dddd, MMMM d, yyyy h:mm tt");
            string timeZoneStr = TimeZoneInfo.Local.DisplayName;
            string systemPrompt = $"You are WinAI, an intelligent and helpful AI assistant running on Windows 10 Mobile. The current date and time is {currentDateTimeStr} ({timeZoneStr}). Always use this accurate real-time date and time whenever asked about dates, days, years, or current time.";

            // Google Search Grounding Tool enables Gemini to access live real-time web events and Google search
            var requestBodyWithSearch = new
            {
                system_instruction = new
                {
                    parts = new object[]
                    {
                        new { text = systemPrompt }
                    }
                },
                contents = contentsPayload,
                tools = new object[]
                {
                    new
                    {
                        google_search = new { }
                    }
                }
            };

            string jsonContent = JsonConvert.SerializeObject(requestBodyWithSearch);

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using (var response = await HttpClient.SendAsync(request))
                {
                    string responseString = await response.Content.ReadAsStringAsync();

                    // If google_search tool is not supported by this specific model/tier, fallback to standard generateContent
                    if (!response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        var fallbackBody = new
                        {
                            system_instruction = new
                            {
                                parts = new object[]
                                {
                                    new { text = systemPrompt }
                                }
                            },
                            contents = contentsPayload
                        };
                        string fallbackJson = JsonConvert.SerializeObject(fallbackBody);
                        using (var fallbackReq = new HttpRequestMessage(HttpMethod.Post, url))
                        {
                            fallbackReq.Content = new StringContent(fallbackJson, Encoding.UTF8, "application/json");
                            using (var fallbackResp = await HttpClient.SendAsync(fallbackReq))
                            {
                                responseString = await fallbackResp.Content.ReadAsStringAsync();
                                if (!fallbackResp.IsSuccessStatusCode)
                                {
                                    HandleApiError(fallbackResp.StatusCode, responseString, DisplayName);
                                }
                            }
                        }
                    }
                    else if (!response.IsSuccessStatusCode)
                    {
                        HandleApiError(response.StatusCode, responseString, DisplayName);
                    }

                    return ParseGeminiResponse(responseString);
                }
            }
        }

        private string ParseGeminiResponse(string responseString)
        {
            var json = JObject.Parse(responseString);
            var candidate = json["candidates"]?[0];
            if (candidate == null)
            {
                throw new Exception("Received empty response from Google Gemini.");
            }

            var sb = new StringBuilder();
            var parts = candidate["content"]?["parts"] as JArray;
            if (parts != null)
            {
                foreach (var part in parts)
                {
                    string pText = part["text"]?.ToString();
                    if (!string.IsNullOrEmpty(pText))
                    {
                        sb.Append(pText);
                    }
                }
            }

            if (sb.Length == 0)
            {
                throw new Exception("Received empty response from Google Gemini.");
            }

            // Extract Google Search Grounding sources if available
            var chunks = candidate["groundingMetadata"]?["groundingChunks"] as JArray;
            if (chunks != null && chunks.Count > 0)
            {
                var sourcesList = new List<string>();
                foreach (var chunk in chunks)
                {
                    string uri = chunk["web"]?["uri"]?.ToString();
                    string title = chunk["web"]?["title"]?.ToString();
                    if (!string.IsNullOrEmpty(uri))
                    {
                        string displayTitle = !string.IsNullOrEmpty(title) ? title.Trim() : uri;
                        string entry = $"• [{displayTitle}]({uri})";
                        if (!sourcesList.Contains(entry))
                        {
                            sourcesList.Add(entry);
                        }
                    }
                }

                if (sourcesList.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.AppendLine("🔍 **Web Sources:**");
                    foreach (var s in sourcesList.Take(4))
                    {
                        sb.AppendLine(s);
                    }
                }
            }

            return sb.ToString().Trim();
        }
    }
}
