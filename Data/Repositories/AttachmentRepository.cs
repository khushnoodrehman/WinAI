using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WinAI.Data.Database;
using WinAI.Data.Models;

namespace WinAI.Data.Repositories
{
    public sealed class AttachmentRepository : IAttachmentRepository
    {
        private readonly WinAIDatabase _db;

        public AttachmentRepository(WinAIDatabase database = null)
        {
            _db = database ?? DatabaseInitializer.Database;
        }

        public async Task<AttachmentEntity> AddAsync(AttachmentEntity attachment)
        {
            if (attachment == null) throw new ArgumentNullException(nameof(attachment));

            if (string.IsNullOrEmpty(attachment.Id))
            {
                attachment.Id = Guid.NewGuid().ToString("D");
            }

            const string sql = @"
                INSERT INTO Attachments (
                    Id, MessageId, ConversationId, LocalFileName, ThumbnailFileName,
                    OriginalFileName, ContentType, FileSizeBytes, ImageWidth, ImageHeight, CreatedAtUtc
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            ";

            await _db.ExecuteNonQueryAsync(
                sql,
                attachment.Id,
                attachment.MessageId,
                attachment.ConversationId,
                attachment.LocalFileName ?? string.Empty,
                attachment.ThumbnailFileName ?? string.Empty,
                attachment.OriginalFileName ?? string.Empty,
                attachment.ContentType ?? "image/jpeg",
                attachment.FileSizeBytes,
                attachment.ImageWidth,
                attachment.ImageHeight,
                attachment.CreatedAtUtc
            ).ConfigureAwait(false);

            return attachment;
        }

        public async Task<AttachmentEntity> GetByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            const string sql = "SELECT * FROM Attachments WHERE Id = ? LIMIT 1;";
            var list = await _db.QueryAsync(sql, MapRowToEntity, id).ConfigureAwait(false);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task<List<AttachmentEntity>> GetByMessageIdAsync(string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId)) return new List<AttachmentEntity>();

            const string sql = @"
                SELECT * FROM Attachments 
                WHERE MessageId = ? 
                ORDER BY CreatedAtUtc ASC;
            ";

            return await _db.QueryAsync(sql, MapRowToEntity, messageId).ConfigureAwait(false);
        }

        public async Task<List<AttachmentEntity>> GetByConversationIdAsync(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return new List<AttachmentEntity>();

            const string sql = @"
                SELECT * FROM Attachments 
                WHERE ConversationId = ? 
                ORDER BY CreatedAtUtc ASC;
            ";

            return await _db.QueryAsync(sql, MapRowToEntity, conversationId).ConfigureAwait(false);
        }

        public async Task<List<AttachmentEntity>> GetAllAsync(int limit = 100, int offset = 0)
        {
            string sql;
            object[] parameters;

            if (limit > 0)
            {
                sql = "SELECT * FROM Attachments ORDER BY CreatedAtUtc DESC, Id DESC LIMIT ? OFFSET ?;";
                parameters = new object[] { limit, offset };
            }
            else
            {
                sql = "SELECT * FROM Attachments ORDER BY CreatedAtUtc DESC, Id DESC;";
                parameters = new object[0];
            }

            return await _db.QueryAsync(sql, MapRowToEntity, parameters).ConfigureAwait(false);
        }

        public async Task<int> GetCountAsync()
        {
            return await _db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Attachments;").ConfigureAwait(false);
        }

        public async Task<bool> DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            int affected = await _db.ExecuteNonQueryAsync("DELETE FROM Attachments WHERE Id = ?;", id).ConfigureAwait(false);
            return affected > 0;
        }

        public async Task<int> DeleteByMessageIdAsync(string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId)) return 0;

            return await _db.ExecuteNonQueryAsync("DELETE FROM Attachments WHERE MessageId = ?;", messageId).ConfigureAwait(false);
        }

        public async Task<int> DeleteByConversationIdAsync(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return 0;

            return await _db.ExecuteNonQueryAsync("DELETE FROM Attachments WHERE ConversationId = ?;", conversationId).ConfigureAwait(false);
        }

        public async Task<List<string>> GetAllLocalFileNamesAsync()
        {
            const string sql = "SELECT LocalFileName FROM Attachments WHERE LocalFileName != '';";
            return await _db.QueryAsync(sql, row => row.GetString("LocalFileName")).ConfigureAwait(false);
        }

        private static AttachmentEntity MapRowToEntity(ISqliteRow row)
        {
            return new AttachmentEntity
            {
                Id = row.GetString("Id"),
                MessageId = row.GetString("MessageId"),
                ConversationId = row.GetString("ConversationId"),
                LocalFileName = row.GetString("LocalFileName"),
                ThumbnailFileName = row.GetString("ThumbnailFileName"),
                OriginalFileName = row.GetString("OriginalFileName"),
                ContentType = row.GetString("ContentType"),
                FileSizeBytes = row.GetInt64("FileSizeBytes"),
                ImageWidth = row.GetInt32("ImageWidth"),
                ImageHeight = row.GetInt32("ImageHeight"),
                CreatedAtUtc = row.GetDateTimeUtc("CreatedAtUtc")
            };
        }
    }
}
