using System.Collections.Generic;
using System.Threading.Tasks;
using WinAI.Data.Models;

namespace WinAI.Data.Repositories
{
    /// <summary>
    /// Repository contract for managing ConversationEntity persistence.
    /// Isolates data access and SQL operations from application business logic.
    /// </summary>
    public interface IConversationRepository
    {
        Task<ConversationEntity> CreateAsync(ConversationEntity conversation);
        Task<ConversationEntity> GetByIdAsync(string id);
        Task<List<ConversationEntity>> GetAllAsync(bool includeArchived = false);
        Task<List<ConversationEntity>> GetPinnedAsync();
        Task<List<ConversationEntity>> SearchAsync(string query);
        Task<bool> UpdateAsync(ConversationEntity conversation);
        Task<bool> SetPinnedAsync(string id, bool isPinned);
        Task<bool> SetArchivedAsync(string id, bool isArchived);
        Task<bool> DeleteAsync(string id);
        Task<bool> ClearAllAsync();
        Task<int> GetCountAsync(bool includeArchived = false);
    }
}
