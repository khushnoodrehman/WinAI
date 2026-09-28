using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Newtonsoft.Json;
using WinAI.Models;

namespace WinAI.Services
{
    /// <summary>
    /// Manages persistent local chat sessions in ApplicationData.Current.LocalFolder.
    /// </summary>
    public sealed class ChatHistoryService
    {
        private const string HistoryFileName = "chat_sessions.json";
        private static readonly Lazy<ChatHistoryService> _instance = new Lazy<ChatHistoryService>(() => new ChatHistoryService());
        public static ChatHistoryService Instance => _instance.Value;

        private readonly List<ChatSession> _sessions = new List<ChatSession>();
        private bool _isLoaded;

        public event EventHandler SessionsUpdated;

        private ChatHistoryService()
        {
        }

        public async Task<List<ChatSession>> GetSessionsAsync()
        {
            if (!_isLoaded)
            {
                await LoadFromDiskAsync();
            }
            return _sessions.OrderByDescending(s => s.UpdatedAt).ToList();
        }

        public async Task<ChatSession> CreateNewSessionAsync(string modelId = null)
        {
            if (!_isLoaded)
            {
                await LoadFromDiskAsync();
            }

            var session = new ChatSession
            {
                Title = "New Chat",
                SelectedModelId = modelId,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _sessions.Insert(0, session);
            await SaveToDiskAsync();
            SessionsUpdated?.Invoke(this, EventArgs.Empty);
            return session;
        }

        public async Task SaveSessionAsync(ChatSession session)
        {
            if (session == null) return;

            if (!_isLoaded)
            {
                await LoadFromDiskAsync();
            }

            var existing = _sessions.FirstOrDefault(s => s.Id == session.Id);
            if (existing != null)
            {
                existing.Title = session.Title;
                existing.UpdatedAt = DateTime.Now;
                existing.SelectedModelId = session.SelectedModelId;
                existing.Messages = new List<ChatMessage>(session.Messages);
            }
            else
            {
                session.UpdatedAt = DateTime.Now;
                _sessions.Insert(0, session);
            }

            await SaveToDiskAsync();
            SessionsUpdated?.Invoke(this, EventArgs.Empty);
        }

        public async Task DeleteSessionAsync(string sessionId)
        {
            if (!_isLoaded)
            {
                await LoadFromDiskAsync();
            }

            int removed = _sessions.RemoveAll(s => s.Id == sessionId);
            if (removed > 0)
            {
                await SaveToDiskAsync();
                SessionsUpdated?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task ClearAllSessionsAsync()
        {
            _sessions.Clear();
            await SaveToDiskAsync();
            SessionsUpdated?.Invoke(this, EventArgs.Empty);
        }

        private async Task LoadFromDiskAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var item = await folder.TryGetItemAsync(HistoryFileName);
                if (item is StorageFile file)
                {
                    string json = await FileIO.ReadTextAsync(file);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var list = JsonConvert.DeserializeObject<List<ChatSession>>(json);
                        if (list != null)
                        {
                            _sessions.Clear();
                            _sessions.AddRange(list);
                        }
                    }
                }
            }
            catch
            {
                // Fallback to empty session list
                _sessions.Clear();
            }
            finally
            {
                _isLoaded = true;
            }
        }

        private async Task SaveToDiskAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.CreateFileAsync(HistoryFileName, CreationCollisionOption.ReplaceExisting);
                string json = JsonConvert.SerializeObject(_sessions, Formatting.Indented);
                await FileIO.WriteTextAsync(file, json);
            }
            catch
            {
                // Silently handle I/O issues gracefully
            }
        }
    }
}
