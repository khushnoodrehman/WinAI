using System;

namespace WinAI.Models
{
    /// <summary>
    /// Represents a recent conversation item displayed on the Home Screen.
    /// </summary>
    public class Conversation
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string TimeAgo { get; set; }
        public string LastMessagePreview { get; set; }

        public Conversation()
        {
        }

        public Conversation(string id, string title, string timeAgo, string lastMessagePreview = null)
        {
            Id = id;
            Title = title;
            TimeAgo = timeAgo;
            LastMessagePreview = lastMessagePreview;
        }
    }
}
