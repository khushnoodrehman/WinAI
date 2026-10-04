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
    public sealed class OpenRouterProvider : BaseAiProvider
    {
        public override string Id => "openrouter";
        public override string DisplayName => "OpenRouter";

        public override IReadOnlyList<AiModelDescriptor> GetModels()
        {
            bool configured = IsConfigured;
            var list = new List<AiModelDescriptor>();

            var cached = ModelService.Instance.GetModelsForProvider(Id);
            if (cached != null && cached.Count > 0)
            {
                foreach (var item in cached)
                {
                    var caps = item.Capabilities;
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
                throw new InvalidOperationException("OpenRouter API key is missing. Please configure it in Key Vault.");
            }

            string cleanModel = model?.Id?.Trim() ?? "meta-llama/llama-3.3-70b-instruct:free";

            var messagesPayload = new List<object>();
            string currentDateTimeStr = DateTime.Now.ToString("dddd, MMMM d, yyyy h:mm tt");
            string timeZoneStr = TimeZoneInfo.Local.DisplayName;

            string systemPrompt = $"You are an intelligent, helpful AI assistant accessible via WinAI on Windows 10 Mobile. The accurate real-time date and time is {currentDateTimeStr} ({timeZoneStr}). Always use this exact current date and time when asked about today's date, day, year, or time. If the user asks about live breaking news, live sports match scores, or real-time events that occurred after your knowledge training cutoff, state your knowledge cutoff date honestly and advise checking the browser for live updates, rather than guessing or denying that events took place.";

            messagesPayload.Add(new
            {
                role = "system",
                content = systemPrompt
            });

            bool hasImage = conversationHistory.Any(m => m.IsUser && m.HasImage);

            var context = conversationHistory.Skip(Math.Max(0, conversationHistory.Count - 16));
            foreach (var msg in context)
            {
                string textToSend = msg.Text ?? string.Empty;

                if (msg.IsUser && msg.HasImage)
                {
                    var contentParts = new List<object>();
                    if (!string.IsNullOrWhiteSpace(textToSend))
                    {
                        contentParts.Add(new { type = "text", text = textToSend });
                    }
                    contentParts.Add(new
                    {
                        type = "image_url",
                        image_url = new
                        {
                            url = $"data:{msg.ImageMimeType ?? "image/jpeg"};base64,{msg.ImageBase64}"
                        }
                    });

                    messagesPayload.Add(new { role = "user", content = contentParts });
                }
                else
                {
                    messagesPayload.Add(new
                    {
                        role = msg.IsUser ? "user" : "assistant",
                        content = textToSend
                    });
                }
            }

            var fallbackModels = new List<string> { cleanModel };
            if (hasImage)
            {
                // Include multimodal fallbacks in the OpenRouter models array
                string[] visionFallbacks = new[]
                {
                    "google/gemini-2.0-flash-exp:free",
                    "google/gemini-flash-1.5:free",
                    "meta-llama/llama-3.2-11b-vision-instruct:free",
                    "qwen/qwen-2-vl-72b-instruct:free",
                    "openrouter/free",
                    "openrouter/auto"
                };

                foreach (var vf in visionFallbacks)
                {
                    if (!fallbackModels.Contains(vf)) fallbackModels.Add(vf);
                }
            }

            var requestBody = new
            {
                model = cleanModel,
                models = fallbackModels,
                messages = messagesPayload
            };

            string jsonContent = JsonConvert.SerializeObject(requestBody);

            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                request.Headers.Add("HTTP-Referer", "https://github.com/winai");
                request.Headers.Add("X-Title", "WinAI for Windows 10 Mobile");
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using (var response = await HttpClient.SendAsync(request))
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        // Seamless Vision Fallback: If chosen model cannot handle images, route to free vision model
                        if (hasImage && responseString.IndexOf("support image input", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return await SendVisionFallbackAsync(apiKey, messagesPayload);
                        }

                        HandleApiError(response.StatusCode, responseString, DisplayName);
                    }

                    var json = JObject.Parse(responseString);
                    var content = json["choices"]?[0]?["message"]?["content"]?.ToString();
                    if (string.IsNullOrEmpty(content))
                    {
                        throw new Exception("Received empty response from OpenRouter.");
                    }
                    return content.Trim();
                }
            }
        }

        private async Task<string> SendVisionFallbackAsync(string apiKey, List<object> messagesPayload)
        {
            string[] candidateVisionModels = new[]
            {
                "google/gemini-2.0-flash-exp:free",
                "google/gemini-flash-1.5:free",
                "meta-llama/llama-3.2-11b-vision-instruct:free",
                "openrouter/free",
                "openrouter/auto"
            };

            foreach (var visModel in candidateVisionModels)
            {
                try
                {
                    var fallbackBody = new
                    {
                        model = visModel,
                        messages = messagesPayload
                    };

                    using (var req = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions"))
                    {
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                        req.Headers.Add("HTTP-Referer", "https://github.com/winai");
                        req.Headers.Add("X-Title", "WinAI for Windows 10 Mobile");
                        req.Content = new StringContent(JsonConvert.SerializeObject(fallbackBody), Encoding.UTF8, "application/json");

                        using (var resp = await HttpClient.SendAsync(req))
                        {
                            string respStr = await resp.Content.ReadAsStringAsync();
                            if (resp.IsSuccessStatusCode)
                            {
                                var json = JObject.Parse(respStr);
                                var content = json["choices"]?[0]?["message"]?["content"]?.ToString();
                                if (!string.IsNullOrEmpty(content))
                                {
                                    return content.Trim();
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Continue to next candidate
                }
            }

            throw new InvalidOperationException("None of the free OpenRouter vision endpoints were able to process this image. Please try selecting Google Gemini in Key Vault.");
        }
    }
}
