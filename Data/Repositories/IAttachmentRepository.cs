using System.Collections.Generic;
using System.Threading.Tasks;
using WinAI.Data.Models;

namespace WinAI.Data.Repositories
{
    public interface IAttachmentRepository
    {
        Task<AttachmentEntity> AddAsync(AttachmentEntity attachment);
        Task<AttachmentEntity> GetByIdAsync(string id);
        Task<List<AttachmentEntity>> GetByMessageIdAsync(string messageId);
        Task<List<AttachmentEntity>> GetByConversationIdAsync(string conversationId);
        Task<List<AttachmentEntity>> GetAllAsync(int limit = 100, int offset = 0);
        Task<int> GetCountAsync();
        Task<bool> DeleteAsync(string id);
        Task<int> DeleteByMessageIdAsync(string messageId);
        Task<int> DeleteByConversationIdAsync(string conversationId);
        Task<List<string>> GetAllLocalFileNamesAsync();
    }
}
