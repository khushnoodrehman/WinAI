using System;

namespace WinAI.Data.Models
{
    /// <summary>
    /// Represents the database entity for a single media attachment in a message.
    /// Belongs to a MessageEntity and ConversationEntity.
    /// Stores stable relative filenames rather than fragile absolute paths.
    /// </summary>
    public sealed class AttachmentEntity
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string MessageId { get; set; }
        public string ConversationId { get; set; }
        public string LocalFileName { get; set; }
        public string ThumbnailFileName { get; set; }
        public string OriginalFileName { get; set; }
        public string ContentType { get; set; } = "image/jpeg";
        public long FileSizeBytes { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime LocalCreatedAt => CreatedAtUtc.ToLocalTime();
    }
}
