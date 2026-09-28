using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json;

namespace WinAI.Models
{
    /// <summary>
    /// Represents a saved conversation session containing a history of ChatMessages.
    /// </summary>
    public class ChatSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Title { get; set; } = "New Chat";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public string SelectedModelId { get; set; }

        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

        [JsonIgnore]
        public string FormattedDate => UpdatedAt.ToString("MMM d, h:mm tt");

        [JsonIgnore]
        public string LastMessageSnippet
        {
            get
            {
                if (Messages != null && Messages.Count > 0)
                {
                    string text = Messages[Messages.Count - 1].Text;
                    if (string.IsNullOrWhiteSpace(text)) return "Empty message";
                    return text.Length > 45 ? text.Substring(0, 42) + "..." : text;
                }
                return "No messages yet";
            }
        }
    }
}
