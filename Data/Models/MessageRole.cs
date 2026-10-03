namespace WinAI.Data.Models
{
    /// <summary>
    /// Represents the role of a message author in a conversation.
    /// Uses explicit integer backing values for stable SQLite persistence.
    /// </summary>
    public enum MessageRole
    {
        User = 1,
        Assistant = 2,
        System = 3
    }
}
