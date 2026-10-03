using System.Collections.Generic;
using System.Threading.Tasks;
using WinAI.Data.Models;

namespace WinAI.Data.Repositories
{
    /// <summary>
    /// Repository contract for managing MessageEntity persistence.
    /// Handles deterministic message ordering, sequence allocation, and retrieval.
    /// </summary>
    public interface IMessageRepository
    {
        Task<MessageEntity> AddAsync(MessageEntity message);
        Task<List<MessageEntity>> GetByConversationIdAsync(string conversationId);
        Task<List<MessageEntity>> GetByConversationIdAsync(string conversationId, int limit, int offset);
        Task<List<MessageEntity>> GetRecentMessagesAsync(string conversationId, int limit);
        Task<List<MessageEntity>> GetMessagesBeforeSequenceAsync(string conversationId, int beforeSequence, int limit);
        Task<MessageEntity> GetByIdAsync(string messageId);
        Task<int> GetNextSequenceAsync(string conversationId);
        Task<bool> DeleteAsync(string messageId);
        Task<int> DeleteByConversationIdAsync(string conversationId);
        Task<int> GetCountForConversationAsync(string conversationId);
        Task<MessageEntity> GetLastMessageAsync(string conversationId);
    }
}
