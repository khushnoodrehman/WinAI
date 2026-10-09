using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WinAI.Data.Database;
using WinAI.Data.Models;
using WinAI.Data.Repositories;

namespace WinAI.Data.Services
{
    /// <summary>
    /// Coordinates high-level conversation business logic, atomic transactions,
    /// automatic local title generation, and message memory persistence.
    /// Operates strictly on the local device with zero external cloud dependencies.
    /// </summary>
    public sealed class ConversationService
    {
        private static readonly Lazy<ConversationService> _instance = 
            new Lazy<ConversationService>(() => new ConversationService());

        public static ConversationService Instance => _instance.Value;

        private readonly WinAIDatabase _db;
        private readonly IConversationRepository _conversationRepo;
        private readonly IMessageRepository _messageRepo;
        private readonly IAttachmentRepository _attachmentRepo;

        public event EventHandler<string> ConversationChanged;
        public event EventHandler ConversationsListChanged;

        public ConversationService(
            WinAIDatabase database = null,
            IConversationRepository conversationRepo = null,
            IMessageRepository messageRepo = null,
            IAttachmentRepository attachmentRepo = null)
        {
            _db = database ?? DatabaseInitializer.Database;
            _conversationRepo = conversationRepo ?? new ConversationRepository(_db);
            _messageRepo = messageRepo ?? new MessageRepository(_db);
            _attachmentRepo = attachmentRepo ?? new AttachmentRepository(_db);
        }

        #region Conversation Operations

        public async Task<ConversationEntity> CreateConversationAsync(
            string initialTitle = null,
            string providerId = null,
            string modelId = null,
            string conversationId = null)
        {
            var conversation = new ConversationEntity
            {
                Id = string.IsNullOrWhiteSpace(conversationId) ? Guid.NewGuid().ToString("D") : conversationId.Trim(),
                Title = string.IsNullOrWhiteSpace(initialTitle) ? "New Chat" : initialTitle.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                ProviderId = string.IsNullOrWhiteSpace(providerId) ? "openai" : providerId.Trim(),
                ModelId = string.IsNullOrWhiteSpace(modelId) ? "gpt-4o" : modelId.Trim(),
                IsPinned = false,
                IsArchived = false,
                MessageCount = 0,
                LastMessagePreview = string.Empty
            };

            await _conversationRepo.CreateAsync(conversation).ConfigureAwait(false);
            ConversationsListChanged?.Invoke(this, EventArgs.Empty);
            return conversation;
        }

        public async Task<ConversationEntity> GetConversationAsync(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return null;
            return await _conversationRepo.GetByIdAsync(conversationId).ConfigureAwait(false);
        }

        public async Task<List<ConversationEntity>> GetConversationsAsync(bool includeArchived = false)
        {
            return await _conversationRepo.GetAllAsync(includeArchived).ConfigureAwait(false);
        }

        public async Task<List<ConversationEntity>> GetPinnedConversationsAsync()
        {
            return await _conversationRepo.GetPinnedAsync().ConfigureAwait(false);
        }

        public async Task<List<ConversationEntity>> SearchConversationsAsync(string query)
        {
            return await _conversationRepo.SearchAsync(query).ConfigureAwait(false);
        }

        public async Task<List<MessageEntity>> GetMessagesAsync(string conversationId)
        {
            return await GetMessagesAsync(conversationId, 0, 0).ConfigureAwait(false);
        }

        public async Task<List<MessageEntity>> GetMessagesAsync(string conversationId, int limit, int offset)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return new List<MessageEntity>();
            return await _messageRepo.GetByConversationIdAsync(conversationId, limit, offset).ConfigureAwait(false);
        }

        public async Task<List<MessageEntity>> GetRecentMessagesAsync(string conversationId, int limit = 50)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return new List<MessageEntity>();
            return await _messageRepo.GetRecentMessagesAsync(conversationId, limit).ConfigureAwait(false);
        }

        public async Task<List<MessageEntity>> GetMessagesBeforeSequenceAsync(string conversationId, int beforeSequence, int limit = 50)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return new List<MessageEntity>();
            return await _messageRepo.GetMessagesBeforeSequenceAsync(conversationId, beforeSequence, limit).ConfigureAwait(false);
        }

        public async Task<bool> RecalculateConversationMetadataAsync(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return false;

            var conv = await _conversationRepo.GetByIdAsync(conversationId).ConfigureAwait(false);
            if (conv == null) return false;

            int count = await _messageRepo.GetCountForConversationAsync(conversationId).ConfigureAwait(false);
            var lastMsg = await _messageRepo.GetLastMessageAsync(conversationId).ConfigureAwait(false);

            conv.MessageCount = count;
            conv.LastMessagePreview = lastMsg != null ? FormatPreview(lastMsg.Content) : string.Empty;
            conv.UpdatedAtUtc = lastMsg != null ? lastMsg.CreatedAtUtc : conv.CreatedAtUtc;

            bool ok = await _conversationRepo.UpdateAsync(conv).ConfigureAwait(false);
            if (ok)
            {
                ConversationChanged?.Invoke(this, conversationId);
                ConversationsListChanged?.Invoke(this, EventArgs.Empty);
            }
            return ok;
        }

        public async Task<bool> UpdateConversationTitleAsync(string conversationId, string newTitle)
        {
            if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(newTitle)) return false;

            var conv = await _conversationRepo.GetByIdAsync(conversationId).ConfigureAwait(false);
            if (conv == null) return false;

            conv.Title = SanitizeTitle(newTitle);
            conv.UpdatedAtUtc = DateTime.UtcNow;

            bool ok = await _conversationRepo.UpdateAsync(conv).ConfigureAwait(false);
            if (ok)
            {
                ConversationChanged?.Invoke(this, conversationId);
                ConversationsListChanged?.Invoke(this, EventArgs.Empty);
            }
            return ok;
        }

        public async Task<bool> UpdateConversationModelAsync(string conversationId, string providerId, string modelId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return false;

            var conv = await _conversationRepo.GetByIdAsync(conversationId).ConfigureAwait(false);
            if (conv == null) return false;

            if (!string.IsNullOrWhiteSpace(providerId)) conv.ProviderId = providerId.Trim();
            if (!string.IsNullOrWhiteSpace(modelId)) conv.ModelId = modelId.Trim();
            conv.UpdatedAtUtc = DateTime.UtcNow;

            bool ok = await _conversationRepo.UpdateAsync(conv).ConfigureAwait(false);
            if (ok)
            {
                ConversationChanged?.Invoke(this, conversationId);
                ConversationsListChanged?.Invoke(this, EventArgs.Empty);
            }
            return ok;
        }

        public async Task<bool> PinConversationAsync(string conversationId, bool isPinned)
        {
            bool ok = await _conversationRepo.SetPinnedAsync(conversationId, isPinned).ConfigureAwait(false);
            if (ok)
            {
                ConversationChanged?.Invoke(this, conversationId);
                ConversationsListChanged?.Invoke(this, EventArgs.Empty);
            }
            return ok;
        }

        public async Task<bool> ArchiveConversationAsync(string conversationId, bool isArchived)
        {
            bool ok = await _conversationRepo.SetArchivedAsync(conversationId, isArchived).ConfigureAwait(false);
            if (ok)
            {
                ConversationChanged?.Invoke(this, conversationId);
                ConversationsListChanged?.Invoke(this, EventArgs.Empty);
            }
            return ok;
        }

        public async Task<bool> DeleteConversationAsync(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return false;

            // 1. Fetch attachments to safely clean up physical media files
            List<AttachmentEntity> attachments = null;
            try
            {
                attachments = await _attachmentRepo.GetByConversationIdAsync(conversationId).ConfigureAwait(false);
            }
            catch { }

            // 2. Delete database records (attachments cascade or delete explicitly)
            try
            {
                await _attachmentRepo.DeleteByConversationIdAsync(conversationId).ConfigureAwait(false);
            }
            catch { }

            bool ok = await _conversationRepo.DeleteAsync(conversationId).ConfigureAwait(false);
            if (ok)
            {
                ConversationsListChanged?.Invoke(this, EventArgs.Empty);
            }

            // 3. Clean up physical media files on disk without holding database locks
            if (attachments != null && attachments.Count > 0)
            {
                foreach (var att in attachments)
                {
                    try
                    {
                        await WinAI.Services.ImageStorageService.Instance.DeleteAttachmentFilesAsync(
                            att.LocalFileName, att.ThumbnailFileName).ConfigureAwait(false);
                    }
                    catch { }
                }
            }

            return ok;
        }

        public async Task<bool> ClearAllConversationsAsync()
        {
            // 1. Fetch all attachments to safely clean up physical files
            List<AttachmentEntity> attachments = null;
            try
            {
                attachments = await _attachmentRepo.GetAllAsync(limit: 0, offset: 0).ConfigureAwait(false);
            }
            catch { }

            try
            {
                var allFileNames = await _attachmentRepo.GetAllLocalFileNamesAsync().ConfigureAwait(false);
            }
            catch { }

            bool ok = await _conversationRepo.ClearAllAsync().ConfigureAwait(false);
            if (ok)
            {
                ConversationsListChanged?.Invoke(this, EventArgs.Empty);
            }

            // 2. Clean up media files
            if (attachments != null)
            {
                foreach (var att in attachments)
                {
                    try
                    {
                        await WinAI.Services.ImageStorageService.Instance.DeleteAttachmentFilesAsync(
                            att.LocalFileName, att.ThumbnailFileName).ConfigureAwait(false);
                    }
                    catch { }
                }
            }

            return ok;
        }

        #endregion

        #region Attachment Operations

        public async Task<List<AttachmentEntity>> GetAttachmentsForMessageAsync(string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId)) return new List<AttachmentEntity>();
            return await _attachmentRepo.GetByMessageIdAsync(messageId).ConfigureAwait(false);
        }

        public async Task<List<AttachmentEntity>> GetAttachmentsForConversationAsync(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return new List<AttachmentEntity>();
            return await _attachmentRepo.GetByConversationIdAsync(conversationId).ConfigureAwait(false);
        }

        public async Task<List<AttachmentEntity>> GetAllAttachmentsAsync(int limit = 100, int offset = 0)
        {
            return await _attachmentRepo.GetAllAsync(limit, offset).ConfigureAwait(false);
        }

        public async Task<int> GetAttachmentCountAsync()
        {
            return await _attachmentRepo.GetCountAsync().ConfigureAwait(false);
        }

        public async Task<bool> DeleteAttachmentAsync(string attachmentId)
        {
            if (string.IsNullOrWhiteSpace(attachmentId)) return false;

            var entity = await _attachmentRepo.GetByIdAsync(attachmentId).ConfigureAwait(false);
            if (entity == null) return false;

            bool ok = await _attachmentRepo.DeleteAsync(attachmentId).ConfigureAwait(false);
            if (ok)
            {
                await WinAI.Services.ImageStorageService.Instance.DeleteAttachmentFilesAsync(
                    entity.LocalFileName, entity.ThumbnailFileName).ConfigureAwait(false);
            }
            return ok;
        }

        public async Task CleanupOrphanedMediaFilesAsync()
        {
            try
            {
                var names = await _attachmentRepo.GetAllLocalFileNamesAsync().ConfigureAwait(false);
                var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
                await WinAI.Services.ImageStorageService.Instance.CleanupOrphanedFilesAsync(set).ConfigureAwait(false);
            }
            catch { }
        }

        #endregion

        #region Message Operations with Atomic Conversation Updates

        public async Task<MessageEntity> AddUserMessageAsync(
            string conversationId,
            string content,
            string providerId = null,
            string modelId = null,
            List<AttachmentEntity> attachments = null)
        {
            return await AddMessageInternalAsync(
                conversationId,
                MessageRole.User,
                content,
                providerId,
                modelId,
                isError: false,
                tokenCount: 0,
                attachments: attachments
            ).ConfigureAwait(false);
        }

        public async Task<MessageEntity> AddAssistantMessageAsync(
            string conversationId,
            string content,
            string providerId = null,
            string modelId = null,
            bool isError = false,
            int tokenCount = 0)
        {
            return await AddMessageInternalAsync(
                conversationId,
                MessageRole.Assistant,
                content,
                providerId,
                modelId,
                isError,
                tokenCount,
                attachments: null
            ).ConfigureAwait(false);
        }

        private async Task<MessageEntity> AddMessageInternalAsync(
            string conversationId,
            MessageRole role,
            string content,
            string providerId,
            string modelId,
            bool isError,
            int tokenCount,
            List<AttachmentEntity> attachments = null)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
                throw new ArgumentException("Conversation ID cannot be empty.", nameof(conversationId));

            MessageEntity createdMessage = null;

            await _db.RunInTransactionAsync(async db =>
            {
                var conv = await _conversationRepo.GetByIdAsync(conversationId).ConfigureAwait(false);
                if (conv == null)
                {
                    // Create conversation if it doesn't exist yet
                    conv = new ConversationEntity
                    {
                        Id = conversationId,
                        Title = "New Chat",
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow,
                        ProviderId = providerId ?? "openai",
                        ModelId = modelId ?? "gpt-4o"
                    };
                    await _conversationRepo.CreateAsync(conv).ConfigureAwait(false);
                }

                int nextSeq = await _messageRepo.GetNextSequenceAsync(conversationId).ConfigureAwait(false);

                createdMessage = new MessageEntity
                {
                    Id = Guid.NewGuid().ToString("D"),
                    ConversationId = conversationId,
                    Role = role,
                    Content = content ?? string.Empty,
                    CreatedAtUtc = DateTime.UtcNow,
                    ProviderId = string.IsNullOrWhiteSpace(providerId) ? conv.ProviderId : providerId,
                    ModelId = string.IsNullOrWhiteSpace(modelId) ? conv.ModelId : modelId,
                    Sequence = nextSeq,
                    TokenCount = tokenCount,
                    IsError = isError
                };

                await _messageRepo.AddAsync(createdMessage).ConfigureAwait(false);

                // Insert attachment metadata transactionally
                if (attachments != null && attachments.Count > 0)
                {
                    foreach (var att in attachments)
                    {
                        att.MessageId = createdMessage.Id;
                        att.ConversationId = conversationId;
                        await _attachmentRepo.AddAsync(att).ConfigureAwait(false);
                    }
                }

                // Update conversation metadata transactionally
                conv.UpdatedAtUtc = DateTime.UtcNow;
                conv.MessageCount += 1;
                conv.LastMessagePreview = FormatPreview(content);

                // If this is the first user message and title is still "New Chat", generate a smart local title!
                if (role == MessageRole.User && (conv.Title == "New Chat" || string.IsNullOrWhiteSpace(conv.Title)))
                {
                    conv.Title = GenerateLocalTitle(content);
                }

                if (!string.IsNullOrWhiteSpace(providerId)) conv.ProviderId = providerId;
                if (!string.IsNullOrWhiteSpace(modelId)) conv.ModelId = modelId;

                await _conversationRepo.UpdateAsync(conv).ConfigureAwait(false);
            }).ConfigureAwait(false);

            ConversationChanged?.Invoke(this, conversationId);
            ConversationsListChanged?.Invoke(this, EventArgs.Empty);

            return createdMessage;
        }

        #endregion

        #region Title & Preview Strategies (100% Offline / Local)

        public static string GenerateLocalTitle(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage)) return "New Chat";

            // Strip line breaks, multiple spaces, and common punctuation prefixes
            string clean = Regex.Replace(userMessage, @"[\r\n\t]+", " ").Trim();
            clean = Regex.Replace(clean, @"\s{2,}", " ");

            // Remove conversational conversational filler like "Please", "Can you", "Tell me about", "How do I"
            string pattern = @"^(can you please|could you please|can you|could you|please|tell me about|explain|how do i|how to|what is|what are)\s+";
            string topic = Regex.Replace(clean, pattern, "", RegexOptions.IgnoreCase).Trim();

            if (string.IsNullOrWhiteSpace(topic))
            {
                topic = clean;
            }

            // Capitalize first letter
            if (topic.Length > 0)
            {
                topic = char.ToUpperInvariant(topic[0]) + (topic.Length > 1 ? topic.Substring(1) : "");
            }

            // Max length: 45 characters
            if (topic.Length > 45)
            {
                int lastSpace = topic.LastIndexOf(' ', 45);
                if (lastSpace > 20)
                {
                    topic = topic.Substring(0, lastSpace) + "...";
                }
                else
                {
                    topic = topic.Substring(0, 45) + "...";
                }
            }

            return SanitizeTitle(topic);
        }

        private static string SanitizeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "New Chat";
            string clean = Regex.Replace(title, @"[\r\n\t]+", " ").Trim();
            return clean.Length > 60 ? clean.Substring(0, 60) : clean;
        }

        private static string FormatPreview(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            string clean = Regex.Replace(content, @"[\r\n\t]+", " ").Trim();
            clean = Regex.Replace(clean, @"\s{2,}", " ");
            return clean.Length > 70 ? clean.Substring(0, 67) + "..." : clean;
        }

        #endregion
    }
}
