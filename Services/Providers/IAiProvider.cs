using System.Collections.Generic;
using System.Threading.Tasks;
using WinAI.Models;

namespace WinAI.Services.Providers
{
    /// <summary>
    /// Common abstraction for all AI providers (OpenAI, Gemini, Claude, DeepSeek, Grok, Perplexity, etc.).
    /// Encapsulates provider-specific payload formatting, authentication, and execution.
    /// The Chat UI and Conversation layers depend strictly on this abstraction.
    /// </summary>
    public interface IAiProvider
    {
        string Id { get; }
        string DisplayName { get; }
        bool IsConfigured { get; }

        IReadOnlyList<AiModelDescriptor> GetModels();
        Task<string> SendMessageAsync(AiModelDescriptor model, List<ChatMessage> conversationHistory);
        Task<ModelTestResult> TestConnectionAsync(string apiKey = null);
    }
}
