using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WinAI.Data.Database;
using WinAI.Data.Models;

namespace WinAI.Data.Repositories
{
    public sealed class MessageRepository : IMessageRepository
    {
        private readonly WinAIDatabase _db;

        public MessageRepository(WinAIDatabase database = null)
        {
            _db = database ?? DatabaseInitializer.Database;
        }

        public async Task<MessageEntity> AddAsync(MessageEntity message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            if (string.IsNullOrEmpty(message.Id))
            {
                message.Id = Guid.NewGuid().ToString("D");
            }

            if (message.Sequence <= 0)
            {
                message.Sequence = await GetNextSequenceAsync(message.ConversationId).ConfigureAwait(false);
            }

            const string sql = @"
                INSERT INTO Messages (
                    Id, ConversationId, Role, Content, CreatedAtUtc,
                    ProviderId, ModelId, Sequence, TokenCount, IsError
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            ";

            await _db.ExecuteNonQueryAsync(
                sql,
                message.Id,
                message.ConversationId,
                (int)message.Role,
                message.Content ?? string.Empty,
                message.CreatedAtUtc,
                message.ProviderId ?? string.Empty,
                message.ModelId ?? string.Empty,
                message.Sequence,
                message.TokenCount,
                message.IsError ? 1 : 0
            ).ConfigureAwait(false);

            return message;
        }

        public async Task<List<MessageEntity>> GetByConversationIdAsync(string conversationId)
        {
            return await GetByConversationIdAsync(conversationId, 0, 0).ConfigureAwait(false);
        }

        public async Task<List<MessageEntity>> GetByConversationIdAsync(string conversationId, int limit, int offset)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return new List<MessageEntity>();

            if (limit <= 0)
            {
                const string sql = @"
                SELECT Id, ConversationId, Role, Content, CreatedAtUtc,
                       ProviderId, ModelId, Sequence, TokenCount, IsError
                FROM Messages
                WHERE ConversationId = ?
                ORDER BY Sequence ASC, CreatedAtUtc ASC;
            ";
                return await _db.QueryAsync(sql, MapRowToEntity, conversationId).ConfigureAwait(false);
            }
            else
            {
                const string sql = @"
                SELECT Id, ConversationId, Role, Content, CreatedAtUtc,
                       ProviderId, ModelId, Sequence, TokenCount, IsError
                FROM Messages
                WHERE ConversationId = ?
                ORDER BY Sequence ASC, CreatedAtUtc ASC
                LIMIT ? OFFSET ?;
            ";
                return await _db.QueryAsync(sql, MapRowToEntity, conversationId, limit, offset).ConfigureAwait(false);
            }
        }

        public async Task<List<MessageEntity>> GetRecentMessagesAsync(string conversationId, int limit)
        {
            if (string.IsNullOrWhiteSpace(conversationId) || limit <= 0) return new List<MessageEntity>();

            const string sql = @"
                SELECT * FROM (
                    SELECT Id, ConversationId, Role, Content, CreatedAtUtc,
                           ProviderId, ModelId, Sequence, TokenCount, IsError
                    FROM Messages
                    WHERE ConversationId = ?
                    ORDER BY Sequence DESC
                    LIMIT ?
                ) sub
                ORDER BY Sequence ASC;
            ";

            return await _db.QueryAsync(sql, MapRowToEntity, conversationId, limit).ConfigureAwait(false);
        }

        public async Task<List<MessageEntity>> GetMessagesBeforeSequenceAsync(string conversationId, int beforeSequence, int limit)
        {
            if (string.IsNullOrWhiteSpace(conversationId) || limit <= 0) return new List<MessageEntity>();

            const string sql = @"
                SELECT * FROM (
                    SELECT Id, ConversationId, Role, Content, CreatedAtUtc,
                           ProviderId, ModelId, Sequence, TokenCount, IsError
                    FROM Messages
                    WHERE ConversationId = ? AND Sequence < ?
                    ORDER BY Sequence DESC
                    LIMIT ?
                ) sub
                ORDER BY Sequence ASC;
            ";

            return await _db.QueryAsync(sql, MapRowToEntity, conversationId, beforeSequence, limit).ConfigureAwait(false);
        }

        public async Task<MessageEntity> GetByIdAsync(string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId)) return null;

            const string sql = @"
                SELECT Id, ConversationId, Role, Content, CreatedAtUtc,
                       ProviderId, ModelId, Sequence, TokenCount, IsError
                FROM Messages
                WHERE Id = ?
                LIMIT 1;
            ";

            var list = await _db.QueryAsync(sql, MapRowToEntity, messageId).ConfigureAwait(false);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task<int> GetNextSequenceAsync(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return 1;

            const string sql = @"
                SELECT COALESCE(MAX(Sequence), 0) + 1
                FROM Messages
                WHERE ConversationId = ?;
            ";

            return await _db.ExecuteScalarAsync<int>(sql, conversationId).ConfigureAwait(false);
        }

        public async Task<bool> DeleteAsync(string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId)) return false;

            const string sql = "DELETE FROM Messages WHERE Id = ?;";
            int rows = await _db.ExecuteNonQueryAsync(sql, messageId).ConfigureAwait(false);
            return rows > 0;
        }

        public async Task<int> DeleteByConversationIdAsync(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return 0;

            const string sql = "DELETE FROM Messages WHERE ConversationId = ?;";
            return await _db.ExecuteNonQueryAsync(sql, conversationId).ConfigureAwait(false);
        }

        public async Task<int> GetCountForConversationAsync(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return 0;

            const string sql = "SELECT COUNT(*) FROM Messages WHERE ConversationId = ?;";
            return await _db.ExecuteScalarAsync<int>(sql, conversationId).ConfigureAwait(false);
        }

        public async Task<MessageEntity> GetLastMessageAsync(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return null;

            const string sql = @"
                SELECT Id, ConversationId, Role, Content, CreatedAtUtc,
                       ProviderId, ModelId, Sequence, TokenCount, IsError
                FROM Messages
                WHERE ConversationId = ?
                ORDER BY Sequence DESC
                LIMIT 1;
            ";

            var list = await _db.QueryAsync(sql, MapRowToEntity, conversationId).ConfigureAwait(false);
            return list.Count > 0 ? list[0] : null;
        }

        private static MessageEntity MapRowToEntity(ISqliteRow row)
        {
            return new MessageEntity
            {
                Id = row.GetString("Id"),
                ConversationId = row.GetString("ConversationId"),
                Role = (MessageRole)row.GetInt32("Role"),
                Content = row.GetString("Content"),
                CreatedAtUtc = row.GetDateTimeUtc("CreatedAtUtc"),
                ProviderId = row.GetString("ProviderId"),
                ModelId = row.GetString("ModelId"),
                Sequence = row.GetInt32("Sequence"),
                TokenCount = row.GetInt32("TokenCount"),
                IsError = row.GetBoolean("IsError")
            };
        }
    }
}
