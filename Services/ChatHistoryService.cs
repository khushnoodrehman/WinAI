using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Newtonsoft.Json;
using WinAI.Data.Models;
using WinAI.Data.Services;
using WinAI.Models;

namespace WinAI.Services
{
    /// <summary>
    /// Service managing conversation sessions.
    /// Bridges the application UI models with the durable local SQLite ConversationService.
    /// Preserves legacy file migration so no past conversations are ever lost.
    /// </summary>
    public sealed class ChatHistoryService
    {
        private const string HistoryFileName = "chat_sessions.json";
        private static readonly Lazy<ChatHistoryService> _instance = 
            new Lazy<ChatHistoryService>(() => new ChatHistoryService());
        public static ChatHistoryService Instance => _instance.Value;

        private readonly ConversationService _conversationService = ConversationService.Instance;
        private bool _isMigrated;

        public event EventHandler SessionsUpdated;

        private ChatHistoryService()
        {
            _conversationService.ConversationsListChanged += (s, e) =>
            {
                SessionsUpdated?.Invoke(this, EventArgs.Empty);
            };
        }

        public async Task<List<ChatSession>> GetSessionsAsync()
        {
            await EnsureLegacyMigratedAsync().ConfigureAwait(false);

            var entities = await _conversationService.GetConversationsAsync(includeArchived: false).ConfigureAwait(false);
            var sessions = new List<ChatSession>(entities.Count);

            foreach (var entity in entities)
            {
                sessions.Add(EntityToSession(entity));
            }

            return sessions;
        }

        public async Task<ChatSession> GetSessionByIdAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return null;

            await EnsureLegacyMigratedAsync().ConfigureAwait(false);

            var entity = await _conversationService.GetConversationAsync(sessionId).ConfigureAwait(false);
            if (entity == null) return null;

            var session = EntityToSession(entity);

            // Populate messages for this session
            var msgEntities = await _conversationService.GetMessagesAsync(sessionId).ConfigureAwait(false);
            session.Messages = msgEntities.Select(m => new ChatMessage(
                text: m.Content,
                isUser: m.Role == MessageRole.User,
                senderName: m.Role == MessageRole.User ? "You" : (string.IsNullOrWhiteSpace(m.ModelId) ? "Assistant" : m.ModelId)
            )
            {
                Id = m.Id,
                Timestamp = m.LocalCreatedAt
            }).ToList();

            return session;
        }

        public async Task<ChatSession> CreateNewSessionAsync(string modelId = null)
        {
            // Returns an in-memory session; the database record will be created
            // when the user actually sends the first message to avoid empty records.
            var session = new ChatSession
            {
                Id = Guid.NewGuid().ToString("D"),
                Title = "New Chat",
                SelectedModelId = modelId ?? "gpt-4o",
                ProviderId = GetProviderFromModelId(modelId),
                ModelDisplayName = modelId ?? "GPT-4o",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Messages = new List<ChatMessage>()
            };

            await Task.CompletedTask;
            return session;
        }

        public async Task SaveSessionAsync(ChatSession session)
        {
            if (session == null) return;

            var existing = await _conversationService.GetConversationAsync(session.Id).ConfigureAwait(false);
            if (existing == null)
            {
                await _conversationService.CreateConversationAsync(
                    session.Title,
                    session.ProviderId,
                    session.SelectedModelId,
                    session.Id
                ).ConfigureAwait(false);
            }
            else
            {
                existing.Title = session.Title;
                existing.UpdatedAtUtc = session.UpdatedAt.ToUniversalTime();
                existing.ProviderId = session.ProviderId;
                existing.ModelId = session.SelectedModelId;
                existing.IsPinned = session.IsPinned;
                if (!string.IsNullOrWhiteSpace(session.CustomPreview))
                {
                    existing.LastMessagePreview = session.CustomPreview;
                }
            }

            SessionsUpdated?.Invoke(this, EventArgs.Empty);
        }

        public async Task TogglePinSessionAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;

            var entity = await _conversationService.GetConversationAsync(sessionId).ConfigureAwait(false);
            if (entity != null)
            {
                await _conversationService.PinConversationAsync(sessionId, !entity.IsPinned).ConfigureAwait(false);
            }
        }

        public async Task RenameSessionAsync(string sessionId, string newTitle)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(newTitle)) return;
            await _conversationService.UpdateConversationTitleAsync(sessionId, newTitle).ConfigureAwait(false);
        }

        public async Task DeleteSessionAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;
            await _conversationService.DeleteConversationAsync(sessionId).ConfigureAwait(false);
        }

        public async Task ClearAllSessionsAsync()
        {
            await _conversationService.ClearAllConversationsAsync().ConfigureAwait(false);
        }

        private static ChatSession EntityToSession(ConversationEntity entity)
        {
            return new ChatSession
            {
                Id = entity.Id,
                Title = entity.Title,
                CreatedAt = entity.LocalCreatedAt,
                UpdatedAt = entity.LocalUpdatedAt,
                SelectedModelId = entity.ModelId,
                ProviderId = entity.ProviderId,
                ModelDisplayName = FormatModelDisplayName(entity.ModelId, entity.ProviderId),
                IsPinned = entity.IsPinned,
                CustomPreview = entity.LastMessagePreview,
                MessageCount = entity.MessageCount
            };
        }

        private static string FormatModelDisplayName(string modelId, string providerId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                if (!string.IsNullOrWhiteSpace(providerId))
                {
                    return char.ToUpperInvariant(providerId[0]) + providerId.Substring(1);
                }
                return "GPT-4o";
            }

            string lower = modelId.ToLowerInvariant();
            if (lower == "gpt-4o") return "GPT-4o";
            if (lower == "gpt-4o-mini") return "GPT-4o mini";
            if (lower.Contains("gemini-1.5-pro") || lower.Contains("gemini-2.0-flash") || lower.Contains("gemini"))
            {
                if (lower.Contains("1.5-pro")) return "Gemini 1.5 Pro";
                if (lower.Contains("1.5-flash")) return "Gemini 1.5 Flash";
                if (lower.Contains("2.0-flash")) return "Gemini 2.0 Flash";
                return "Gemini";
            }
            if (lower.Contains("claude-3-5-sonnet")) return "Claude 3.5 Sonnet";
            if (lower.Contains("claude-3-5-haiku")) return "Claude 3.5 Haiku";
            if (lower.Contains("claude-3-opus")) return "Claude 3 Opus";
            if (lower.Contains("sonar-large")) return "Sonar Large";
            if (lower.Contains("sonar-reasoning")) return "Sonar Reasoning";
            if (lower.Contains("sonar-pro")) return "Sonar Pro";
            if (lower.Contains("deepseek-reasoner")) return "DeepSeek R1";
            if (lower.Contains("deepseek-chat")) return "DeepSeek V3";

            return modelId;
        }

        private static string GetProviderFromModelId(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return "openai";
            string lower = modelId.ToLowerInvariant();
            if (lower.Contains("gemini")) return "gemini";
            if (lower.Contains("claude")) return "claude";
            if (lower.Contains("sonar") || lower.Contains("perplexity")) return "perplexity";
            if (lower.Contains("deepseek")) return "deepseek";
            if (lower.Contains("grok") || lower.Contains("xai")) return "xai";
            return "openai";
        }

        private async Task EnsureLegacyMigratedAsync()
        {
            if (_isMigrated) return;
            _isMigrated = true;

            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var item = await folder.TryGetItemAsync(HistoryFileName).AsTask().ConfigureAwait(false);
                if (item is StorageFile file)
                {
                    string json = await FileIO.ReadTextAsync(file).AsTask().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var list = JsonConvert.DeserializeObject<List<ChatSession>>(json);
                        if (list != null && list.Count > 0)
                        {
                            foreach (var s in list)
                            {
                                var existing = await _conversationService.GetConversationAsync(s.Id).ConfigureAwait(false);
                                if (existing == null)
                                {
                                    await _conversationService.CreateConversationAsync(
                                        s.Title,
                                        s.ProviderId,
                                        s.SelectedModelId
                                    ).ConfigureAwait(false);

                                    if (s.Messages != null && s.Messages.Count > 0)
                                    {
                                        foreach (var msg in s.Messages)
                                        {
                                            if (msg.IsUser)
                                            {
                                                await _conversationService.AddUserMessageAsync(
                                                    s.Id,
                                                    msg.Text,
                                                    s.ProviderId,
                                                    s.SelectedModelId
                                                ).ConfigureAwait(false);
                                            }
                                            else
                                            {
                                                await _conversationService.AddAssistantMessageAsync(
                                                    s.Id,
                                                    msg.Text,
                                                    s.ProviderId,
                                                    s.SelectedModelId
                                                ).ConfigureAwait(false);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Remove legacy file after migration to keep storage clean
                    try
                    {
                        await file.DeleteAsync().AsTask().ConfigureAwait(false);
                    }
                    catch { }
                }
            }
            catch
            {
                // Safeguard migration
            }
        }
    }
}
