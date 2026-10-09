using System;
using System.Threading.Tasks;

namespace WinAI.Data.Database
{
    /// <summary>
    /// Manages database versioning and non-destructive schema migrations.
    /// Preserves all user conversation history across updates.
    /// </summary>
    public static class DatabaseMigrations
    {
        public const int CurrentSchemaVersion = 2;

        public static async Task ApplyMigrationsAsync(WinAIDatabase database)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));

            int currentVersion = await database.GetSchemaVersionAsync().ConfigureAwait(false);

            if (currentVersion < 1)
            {
                await ApplyMigrationV1Async(database).ConfigureAwait(false);
                currentVersion = 1;
            }

            if (currentVersion < 2)
            {
                await ApplyMigrationV2Async(database).ConfigureAwait(false);
                currentVersion = 2;
            }
        }

        private static async Task ApplyMigrationV1Async(WinAIDatabase database)
        {
            await database.RunInTransactionAsync(async db =>
            {
                // 1. Create Conversations Table
                db.ExecuteNonQueryInternal(@"
                    CREATE TABLE IF NOT EXISTS Conversations (
                        Id TEXT PRIMARY KEY NOT NULL,
                        Title TEXT NOT NULL,
                        CreatedAtUtc TEXT NOT NULL,
                        UpdatedAtUtc TEXT NOT NULL,
                        ProviderId TEXT,
                        ModelId TEXT,
                        IsPinned INTEGER NOT NULL DEFAULT 0,
                        IsArchived INTEGER NOT NULL DEFAULT 0,
                        MessageCount INTEGER NOT NULL DEFAULT 0,
                        LastMessagePreview TEXT
                    );
                ");

                // 2. Create Messages Table with Foreign Key and Sequence
                db.ExecuteNonQueryInternal(@"
                    CREATE TABLE IF NOT EXISTS Messages (
                        Id TEXT PRIMARY KEY NOT NULL,
                        ConversationId TEXT NOT NULL,
                        Role INTEGER NOT NULL,
                        Content TEXT NOT NULL,
                        CreatedAtUtc TEXT NOT NULL,
                        ProviderId TEXT,
                        ModelId TEXT,
                        Sequence INTEGER NOT NULL,
                        TokenCount INTEGER NOT NULL DEFAULT 0,
                        IsError INTEGER NOT NULL DEFAULT 0,
                        FOREIGN KEY (ConversationId) REFERENCES Conversations (Id) ON DELETE CASCADE
                    );
                ");

                // 3. Create Indexes for high-speed queries and ordering
                db.ExecuteNonQueryInternal(@"
                    CREATE INDEX IF NOT EXISTS idx_messages_conv_id 
                    ON Messages (ConversationId);
                ");

                db.ExecuteNonQueryInternal(@"
                    CREATE INDEX IF NOT EXISTS idx_messages_conv_seq 
                    ON Messages (ConversationId, Sequence);
                ");

                db.ExecuteNonQueryInternal(@"
                    CREATE INDEX IF NOT EXISTS idx_conversations_updated 
                    ON Conversations (UpdatedAtUtc DESC);
                ");

                db.ExecuteNonQueryInternal(@"
                    CREATE INDEX IF NOT EXISTS idx_conversations_pinned 
                    ON Conversations (IsPinned);
                ");

                db.ExecuteNonQueryInternal(@"
                    CREATE INDEX IF NOT EXISTS idx_conversations_archived 
                    ON Conversations (IsArchived);
                ");

                db.ExecuteNonQueryInternal(@"
                    CREATE INDEX IF NOT EXISTS idx_conversations_pinned_updated 
                    ON Conversations (IsArchived, IsPinned DESC, UpdatedAtUtc DESC);
                ");

                // 4. Update Schema Version to 1
                db.ExecuteNonQueryInternal("PRAGMA user_version = 1;");

                await Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        private static async Task ApplyMigrationV2Async(WinAIDatabase database)
        {
            await database.RunInTransactionAsync(async db =>
            {
                // 1. Create Attachments Table with Foreign Keys
                db.ExecuteNonQueryInternal(@"
                    CREATE TABLE IF NOT EXISTS Attachments (
                        Id TEXT PRIMARY KEY NOT NULL,
                        MessageId TEXT NOT NULL,
                        ConversationId TEXT NOT NULL,
                        LocalFileName TEXT NOT NULL,
                        ThumbnailFileName TEXT,
                        OriginalFileName TEXT,
                        ContentType TEXT NOT NULL,
                        FileSizeBytes INTEGER NOT NULL DEFAULT 0,
                        ImageWidth INTEGER NOT NULL DEFAULT 0,
                        ImageHeight INTEGER NOT NULL DEFAULT 0,
                        CreatedAtUtc TEXT NOT NULL,
                        FOREIGN KEY (MessageId) REFERENCES Messages (Id) ON DELETE CASCADE,
                        FOREIGN KEY (ConversationId) REFERENCES Conversations (Id) ON DELETE CASCADE
                    );
                ");

                // 2. Create Indexes for Attachment queries
                db.ExecuteNonQueryInternal(@"
                    CREATE INDEX IF NOT EXISTS idx_attachments_msg_id 
                    ON Attachments (MessageId);
                ");

                db.ExecuteNonQueryInternal(@"
                    CREATE INDEX IF NOT EXISTS idx_attachments_conv_id 
                    ON Attachments (ConversationId);
                ");

                db.ExecuteNonQueryInternal(@"
                    CREATE INDEX IF NOT EXISTS idx_attachments_created 
                    ON Attachments (CreatedAtUtc DESC);
                ");

                // 3. Update Schema Version to 2
                db.ExecuteNonQueryInternal("PRAGMA user_version = 2;");

                await Task.CompletedTask;
            }).ConfigureAwait(false);
        }
    }
}
