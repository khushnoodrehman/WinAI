using System;

namespace WinAI.Data.Models
{
    /// <summary>
    /// Represents the database entity for a single message in a conversation.
    /// Belongs to exactly one ConversationEntity via ConversationId.
    /// </summary>
    public sealed class MessageEntity
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string ConversationId { get; set; }
        public MessageRole Role { get; set; } = MessageRole.User;
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public string ProviderId { get; set; }
        public string ModelId { get; set; }
        public int Sequence { get; set; }
        public int TokenCount { get; set; }
        public bool IsError { get; set; }

        public DateTime LocalCreatedAt => CreatedAtUtc.ToLocalTime();
    }
}
