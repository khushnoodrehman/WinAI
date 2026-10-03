using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WinAI.Data.Database;
using WinAI.Data.Models;

namespace WinAI.Data.Repositories
{
    public sealed class ConversationRepository : IConversationRepository
    {
        private readonly WinAIDatabase _db;

        public ConversationRepository(WinAIDatabase database = null)
        {
            _db = database ?? DatabaseInitializer.Database;
        }

        public async Task<ConversationEntity> CreateAsync(ConversationEntity conversation)
        {
            if (conversation == null) throw new ArgumentNullException(nameof(conversation));

            if (string.IsNullOrEmpty(conversation.Id))
            {
                conversation.Id = Guid.NewGuid().ToString("D");
            }

            const string sql = @"
                INSERT INTO Conversations (
                    Id, Title, CreatedAtUtc, UpdatedAtUtc, ProviderId, ModelId,
                    IsPinned, IsArchived, MessageCount, LastMessagePreview
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            ";

            await _db.ExecuteNonQueryAsync(
                sql,
                conversation.Id,
                conversation.Title ?? "New Chat",
                conversation.CreatedAtUtc,
                conversation.UpdatedAtUtc,
                conversation.ProviderId ?? string.Empty,
                conversation.ModelId ?? string.Empty,
                conversation.IsPinned ? 1 : 0,
                conversation.IsArchived ? 1 : 0,
                conversation.MessageCount,
                conversation.LastMessagePreview ?? string.Empty
            ).ConfigureAwait(false);

            return conversation;
        }

        public async Task<ConversationEntity> GetByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            const string sql = @"
                SELECT Id, Title, CreatedAtUtc, UpdatedAtUtc, ProviderId, ModelId,
                       IsPinned, IsArchived, MessageCount, LastMessagePreview
                FROM Conversations
                WHERE Id = ?
                LIMIT 1;
            ";

            var list = await _db.QueryAsync(sql, MapRowToEntity, id).ConfigureAwait(false);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task<List<ConversationEntity>> GetAllAsync(bool includeArchived = false)
        {
            string sql = includeArchived
                ? @"SELECT Id, Title, CreatedAtUtc, UpdatedAtUtc, ProviderId, ModelId,
                           IsPinned, IsArchived, MessageCount, LastMessagePreview
                    FROM Conversations
                    ORDER BY IsPinned DESC, UpdatedAtUtc DESC;"
                : @"SELECT Id, Title, CreatedAtUtc, UpdatedAtUtc, ProviderId, ModelId,
                           IsPinned, IsArchived, MessageCount, LastMessagePreview
                    FROM Conversations
                    WHERE IsArchived = 0
                    ORDER BY IsPinned DESC, UpdatedAtUtc DESC;";

            return await _db.QueryAsync(sql, MapRowToEntity).ConfigureAwait(false);
        }

        public async Task<List<ConversationEntity>> GetPinnedAsync()
        {
            const string sql = @"
                SELECT Id, Title, CreatedAtUtc, UpdatedAtUtc, ProviderId, ModelId,
                       IsPinned, IsArchived, MessageCount, LastMessagePreview
                FROM Conversations
                WHERE IsPinned = 1 AND IsArchived = 0
                ORDER BY UpdatedAtUtc DESC;
            ";

            return await _db.QueryAsync(sql, MapRowToEntity).ConfigureAwait(false);
        }

        public async Task<List<ConversationEntity>> SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return await GetAllAsync(includeArchived: false).ConfigureAwait(false);
            }

            string pattern = $"%{query.Trim()}%";

            // Search in Conversation Title, LastMessagePreview, or in Messages Content!
            const string sql = @"
                SELECT DISTINCT c.Id, c.Title, c.CreatedAtUtc, c.UpdatedAtUtc, c.ProviderId, c.ModelId,
                                c.IsPinned, c.IsArchived, c.MessageCount, c.LastMessagePreview
                FROM Conversations c
                LEFT JOIN Messages m ON c.Id = m.ConversationId
                WHERE (c.Title LIKE ? OR c.LastMessagePreview LIKE ? OR m.Content LIKE ?)
                  AND c.IsArchived = 0
                ORDER BY c.IsPinned DESC, c.UpdatedAtUtc DESC;
            ";

            return await _db.QueryAsync(sql, MapRowToEntity, pattern, pattern, pattern).ConfigureAwait(false);
        }

        public async Task<bool> UpdateAsync(ConversationEntity conversation)
        {
            if (conversation == null) throw new ArgumentNullException(nameof(conversation));

            const string sql = @"
                UPDATE Conversations
                SET Title = ?,
                    UpdatedAtUtc = ?,
                    ProviderId = ?,
                    ModelId = ?,
                    IsPinned = ?,
                    IsArchived = ?,
                    MessageCount = ?,
                    LastMessagePreview = ?
                WHERE Id = ?;
            ";

            int rows = await _db.ExecuteNonQueryAsync(
                sql,
                conversation.Title ?? "New Chat",
                conversation.UpdatedAtUtc,
                conversation.ProviderId ?? string.Empty,
                conversation.ModelId ?? string.Empty,
                conversation.IsPinned ? 1 : 0,
                conversation.IsArchived ? 1 : 0,
                conversation.MessageCount,
                conversation.LastMessagePreview ?? string.Empty,
                conversation.Id
            ).ConfigureAwait(false);

            return rows > 0;
        }

        public async Task<bool> SetPinnedAsync(string id, bool isPinned)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            const string sql = @"
                UPDATE Conversations
                SET IsPinned = ?,
                    UpdatedAtUtc = ?
                WHERE Id = ?;
            ";

            int rows = await _db.ExecuteNonQueryAsync(sql, isPinned ? 1 : 0, DateTime.UtcNow, id).ConfigureAwait(false);
            return rows > 0;
        }

        public async Task<bool> SetArchivedAsync(string id, bool isArchived)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            const string sql = @"
                UPDATE Conversations
                SET IsArchived = ?,
                    UpdatedAtUtc = ?
                WHERE Id = ?;
            ";

            int rows = await _db.ExecuteNonQueryAsync(sql, isArchived ? 1 : 0, DateTime.UtcNow, id).ConfigureAwait(false);
            return rows > 0;
        }

        public async Task<bool> DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            bool success = false;
            await _db.RunInTransactionAsync(async db =>
            {
                // Delete messages first to guarantee referential cleanup
                db.ExecuteNonQueryInternal("DELETE FROM Messages WHERE ConversationId = ?;", id);
                int rows = db.ExecuteNonQueryInternal("DELETE FROM Conversations WHERE Id = ?;", id);
                success = rows > 0;
                await Task.CompletedTask;
            }).ConfigureAwait(false);

            return success;
        }

        public async Task<bool> ClearAllAsync()
        {
            await _db.RunInTransactionAsync(async db =>
            {
                db.ExecuteNonQueryInternal("DELETE FROM Messages;");
                db.ExecuteNonQueryInternal("DELETE FROM Conversations;");
                await Task.CompletedTask;
            }).ConfigureAwait(false);

            return true;
        }

        public async Task<int> GetCountAsync(bool includeArchived = false)
        {
            string sql = includeArchived
                ? "SELECT COUNT(*) FROM Conversations;"
                : "SELECT COUNT(*) FROM Conversations WHERE IsArchived = 0;";

            return await _db.ExecuteScalarAsync<int>(sql).ConfigureAwait(false);
        }

        private static ConversationEntity MapRowToEntity(ISqliteRow row)
        {
            return new ConversationEntity
            {
                Id = row.GetString("Id"),
                Title = row.GetString("Title"),
                CreatedAtUtc = row.GetDateTimeUtc("CreatedAtUtc"),
                UpdatedAtUtc = row.GetDateTimeUtc("UpdatedAtUtc"),
                ProviderId = row.GetString("ProviderId"),
                ModelId = row.GetString("ModelId"),
                IsPinned = row.GetBoolean("IsPinned"),
                IsArchived = row.GetBoolean("IsArchived"),
                MessageCount = row.GetInt32("MessageCount"),
                LastMessagePreview = row.GetString("LastMessagePreview")
            };
        }
    }
}
