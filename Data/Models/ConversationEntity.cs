using System;

namespace WinAI.Data.Models
{
    /// <summary>
    /// Represents the database entity for a conversation in local SQLite storage.
    /// </summary>
    public sealed class ConversationEntity
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string Title { get; set; } = "New Chat";
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public string ProviderId { get; set; } = "openai";
        public string ModelId { get; set; } = "gpt-4o";
        public bool IsPinned { get; set; }
        public bool IsArchived { get; set; }
        public int MessageCount { get; set; }
        public string LastMessagePreview { get; set; } = string.Empty;

        public DateTime LocalUpdatedAt => UpdatedAtUtc.ToLocalTime();
        public DateTime LocalCreatedAt => CreatedAtUtc.ToLocalTime();
    }
}
