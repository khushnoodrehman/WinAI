using System.Collections.Generic;
using System.Threading.Tasks;
using WinAI.Models;

namespace WinAI.Data.Services
{
    /// <summary>
    /// Contract for building conversation context payloads for AI providers.
    /// Manages context window limits, deterministic sequence ordering, and history trimming.
    /// </summary>
    public interface IConversationContextBuilder
    {
        Task<List<ChatMessage>> BuildContextAsync(string conversationId, int maxRecentMessages = 20);
    }
}
