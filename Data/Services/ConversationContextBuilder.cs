using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinAI.Data.Models;
using WinAI.Models;

namespace WinAI.Data.Services
{
    /// <summary>
    /// Builds deterministic, windowed conversation history payloads for AI providers
    /// directly from the local SQLite message database.
    /// </summary>
    public sealed class ConversationContextBuilder : IConversationContextBuilder
    {
        private readonly ConversationService _conversationService;

        public ConversationContextBuilder(ConversationService conversationService = null)
        {
            _conversationService = conversationService ?? ConversationService.Instance;
        }

        public async Task<List<ChatMessage>> BuildContextAsync(string conversationId, int maxRecentMessages = 20)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return new List<ChatMessage>();
            }

            // Optimized query for large conversations: load only the most recent messages from SQLite
            var entities = await _conversationService.GetRecentMessagesAsync(conversationId, maxRecentMessages * 2).ConfigureAwait(false);
            if (entities == null || entities.Count == 0)
            {
                entities = await _conversationService.GetMessagesAsync(conversationId).ConfigureAwait(false);
            }

            if (entities == null || entities.Count == 0)
            {
                return new List<ChatMessage>();
            }

            // Exclude error messages from the context window
            var validMessages = entities
                .Where(m => !m.IsError && !string.IsNullOrWhiteSpace(m.Content))
                .OrderBy(m => m.Sequence)
                .ThenBy(m => m.CreatedAtUtc)
                .ToList();

            // Window to the most recent messages (e.g. last 20)
            if (validMessages.Count > maxRecentMessages)
            {
                validMessages = validMessages.Skip(validMessages.Count - maxRecentMessages).ToList();
            }

            var contextList = new List<ChatMessage>(validMessages.Count);
            foreach (var entity in validMessages)
            {
                bool isUser = entity.Role == MessageRole.User;
                string senderName = isUser ? "You" : (string.IsNullOrWhiteSpace(entity.ModelId) ? "Assistant" : entity.ModelId);

                var chatMsg = new ChatMessage(entity.Content, isUser, senderName)
                {
                    Id = entity.Id,
                    Timestamp = entity.LocalCreatedAt
                };

                contextList.Add(chatMsg);
            }

            return contextList;
        }
    }
}
