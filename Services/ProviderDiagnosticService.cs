using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinAI.Models;
using WinAI.Services.Providers;

namespace WinAI.Services
{
    public class DiagnosticResult
    {
        public bool Success { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public long LatencyMs { get; set; }
        public string ModelId { get; set; }
        public string ProviderId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Dedicated diagnostic service for testing individual AI models and providers.
    /// CRITICAL ARCHITECTURAL GUARANTEE:
    /// Test requests are executed directly against the provider HTTP client and
    /// NEVER interact with ConversationService, MessageRepository, or SQLite database.
    /// Diagnostic messages NEVER appear in chat UI, conversation lists, or persistent history.
    /// </summary>
    public sealed class ProviderDiagnosticService
    {
        private static readonly Lazy<ProviderDiagnosticService> _instance =
            new Lazy<ProviderDiagnosticService>(() => new ProviderDiagnosticService());

        public static ProviderDiagnosticService Instance => _instance.Value;

        private readonly AiProviderRegistry _registry = AiProviderRegistry.Instance;
        private readonly ModelService _modelService = ModelService.Instance;

        private ProviderDiagnosticService() { }

        /// <summary>
        /// Executes an ephemeral diagnostic test against a specific model of a provider.
        /// Sends minimal test prompt "Respond with: OK", measures latency, validates availability,
        /// and updates local model cache without persisting any conversation history.
        /// </summary>
        public async Task<DiagnosticResult> TestModelAsync(string providerId, string modelId)
        {
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(modelId))
            {
                return new DiagnosticResult
                {
                    Success = false,
                    Status = "Unavailable",
                    Message = "Provider ID and Model ID must be specified.",
                    ModelId = modelId,
                    ProviderId = providerId
                };
            }

            var provider = _registry.GetProvider(providerId);
            if (provider == null)
            {
                _modelService.UpdateModelStatus(providerId, modelId, isAvailable: false, status: "Unavailable", details: "Provider not found.");
                return new DiagnosticResult
                {
                    Success = false,
                    Status = "Unavailable",
                    Message = $"Provider '{providerId}' is not registered.",
                    ModelId = modelId,
                    ProviderId = providerId
                };
            }

            if (!provider.IsConfigured)
            {
                _modelService.UpdateModelStatus(providerId, modelId, isAvailable: false, status: "Needs API Key", details: "No API key configured in Key Vault.");
                return new DiagnosticResult
                {
                    Success = false,
                    Status = "Needs API Key",
                    Message = $"No API key configured for {provider.DisplayName} in Key Vault.",
                    ModelId = modelId,
                    ProviderId = provider.Id
                };
            }

            // 1. Mark model as Testing
            _modelService.UpdateModelStatus(provider.Id, modelId, isAvailable: true, status: "Testing", details: "Testing connection...");

            // 2. Build ephemeral internal diagnostic request
            // Strictly independent - ZERO conversation service, ZERO database persistence
            var descriptor = provider.GetModels().FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase))
                ?? new AiModelDescriptor(modelId, modelId, provider.Id, provider.DisplayName, ModelCapabilities.Text, isConfigured: true);

            var ephemeralHistory = new List<ChatMessage>
            {
                new ChatMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    Text = "Respond with: OK",
                    IsUser = true,
                    Timestamp = DateTime.UtcNow
                }
            };

            var sw = Stopwatch.StartNew();

            try
            {
                // Execute directly through provider API with a 20 second timeout
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                {
                    var sendTask = provider.SendMessageAsync(descriptor, ephemeralHistory);
                    var completedTask = await Task.WhenAny(sendTask, Task.Delay(20000, cts.Token));

                    if (completedTask != sendTask)
                    {
                        sw.Stop();
                        string timeoutMsg = "Connection timed out after 20 seconds.";
                        _modelService.UpdateModelStatus(provider.Id, modelId, isAvailable: false, status: "Network Error", details: timeoutMsg, latencyMs: sw.ElapsedMilliseconds);
                        return new DiagnosticResult
                        {
                            Success = false,
                            Status = "Network Error",
                            Message = timeoutMsg,
                            LatencyMs = sw.ElapsedMilliseconds,
                            ModelId = modelId,
                            ProviderId = provider.Id
                        };
                    }

                    cts.Cancel(); // Cancel timeout delay
                    string response = await sendTask;
                    sw.Stop();

                    bool hasResponse = !string.IsNullOrWhiteSpace(response);
                    string status = hasResponse ? "Available" : "Unavailable";
                    string msg = hasResponse 
                        ? $"Model is working ({sw.ElapsedMilliseconds}ms response)" 
                        : "Empty response received from provider.";

                    _modelService.UpdateModelStatus(provider.Id, modelId, isAvailable: hasResponse, status: status, details: msg, latencyMs: sw.ElapsedMilliseconds);

                    return new DiagnosticResult
                    {
                        Success = hasResponse,
                        Status = status,
                        Message = msg,
                        LatencyMs = sw.ElapsedMilliseconds,
                        ModelId = modelId,
                        ProviderId = provider.Id
                    };
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                string rawError = ex.Message ?? "Unknown error";
                string status = ClassifyErrorStatus(rawError, out string friendlyMessage);

                _modelService.UpdateModelStatus(provider.Id, modelId, isAvailable: false, status: status, details: friendlyMessage, latencyMs: sw.ElapsedMilliseconds);

                return new DiagnosticResult
                {
                    Success = false,
                    Status = status,
                    Message = friendlyMessage,
                    LatencyMs = sw.ElapsedMilliseconds,
                    ModelId = modelId,
                    ProviderId = provider.Id
                };
            }
        }

        private static string ClassifyErrorStatus(string error, out string friendlyMessage)
        {
            string errLower = error.ToLowerInvariant();

            if (errLower.Contains("401") || 
                errLower.Contains("unauthorized") || 
                errLower.Contains("invalid api key") ||
                errLower.Contains("authentication") ||
                errLower.Contains("forbidden") ||
                errLower.Contains("403"))
            {
                friendlyMessage = "Authentication failed. Check your API key in Key Vault.";
                return "Authentication Failed";
            }

            if (errLower.Contains("429") || 
                errLower.Contains("quota") || 
                errLower.Contains("rate limit") ||
                errLower.Contains("resource exhausted"))
            {
                friendlyMessage = "Rate limited or quota exceeded on this provider.";
                return "Rate Limited";
            }

            if (errLower.Contains("404") || 
                errLower.Contains("not found") || 
                errLower.Contains("model_not_found") ||
                errLower.Contains("deprecated") ||
                errLower.Contains("decommissioned") ||
                errLower.Contains("no longer supported") ||
                errLower.Contains("unsupported"))
            {
                friendlyMessage = "Model no longer supported or not found on provider.";
                return "Unavailable";
            }

            if (errLower.Contains("network") || 
                errLower.Contains("connection") || 
                errLower.Contains("timed out") ||
                errLower.Contains("name resolution") ||
                errLower.Contains("dns") ||
                errLower.Contains("socket"))
            {
                friendlyMessage = "Network error. Please check your internet connection.";
                return "Network Error";
            }

            friendlyMessage = error.Length > 120 ? error.Substring(0, 117) + "..." : error;
            return "Unavailable";
        }
    }
}
