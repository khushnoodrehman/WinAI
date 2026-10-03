using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WinAI.Models;

namespace WinAI.Services.Providers
{
    /// <summary>
    /// Base class providing shared HTTP communication, credential resolution, 
    /// and standard error extraction for AI providers.
    /// </summary>
    public abstract class BaseAiProvider : IAiProvider
    {
        protected static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        public abstract string Id { get; }
        public abstract string DisplayName { get; }

        public virtual bool IsConfigured => CredentialVaultService.Instance.HasApiKey(Id);

        protected string GetApiKey() => CredentialVaultService.Instance.GetApiKey(Id);

        public abstract IReadOnlyList<AiModelDescriptor> GetModels();

        public abstract Task<string> SendMessageAsync(AiModelDescriptor model, List<ChatMessage> conversationHistory);

        public virtual async Task<ModelTestResult> TestConnectionAsync(string apiKey = null)
        {
            string keyToTest = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : GetApiKey();
            return await ModelService.Instance.TestAndFetchModelsForProviderAsync(Id, keyToTest);
        }

        protected void HandleApiError(System.Net.HttpStatusCode statusCode, string responseBody, string providerName)
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
                    errorMessage = $"{providerName} Error: {detail}";
                }
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(responseBody) && responseBody.Length < 250)
                {
                    errorMessage = $"{providerName} Error: {responseBody}";
                }
            }

            if (statusCode == System.Net.HttpStatusCode.Unauthorized || statusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException($"Invalid or unauthorized API key for {providerName}. Please check your key in Key Vault.\n\nDetail: {errorMessage}");
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
    }
}
