using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Xaml.Media;
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

        public string ProviderId { get; set; } = "openai";

        public string ModelDisplayName { get; set; } = "GPT-4o";

        public bool IsPinned { get; set; }

        public int MessageCount { get; set; }

        public string CustomPreview { get; set; }

        public string CustomTimeAgo { get; set; }

        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

        [JsonIgnore]
        public string ModelAndCountDisplay
        {
            get
            {
                string model = !string.IsNullOrWhiteSpace(ModelDisplayName) ? ModelDisplayName : (!string.IsNullOrWhiteSpace(SelectedModelId) ? SelectedModelId : "GPT-4o");
                if (MessageCount > 0)
                {
                    string msgCountText = MessageCount == 1 ? "1 message" : $"{MessageCount} messages";
                    return $"{model} • {msgCountText}";
                }
                return model;
            }
        }

        [JsonIgnore]
        public string FormattedDate => UpdatedAt.ToString("MMM d, h:mm tt");

        [JsonIgnore]
        public string PinMenuText => IsPinned ? "Unpin" : "Pin";

        [JsonIgnore]
        public string DisplayPreview
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(CustomPreview)) return CustomPreview;
                if (Messages != null && Messages.Count > 0)
                {
                    for (int i = Messages.Count - 1; i >= 0; i--)
                    {
                        if (!string.IsNullOrWhiteSpace(Messages[i].Text))
                        {
                            string text = Messages[i].Text;
                            return text.Length > 52 ? text.Substring(0, 49) + "..." : text;
                        }
                    }
                }
                return "No messages yet";
            }
        }

        [JsonIgnore]
        public string DisplayTimeAgo
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(CustomTimeAgo)) return CustomTimeAgo;
                var diff = DateTime.Now - UpdatedAt;
                if (diff.TotalMinutes < 1) return "Just now";
                if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
                if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} hours ago";
                if (diff.TotalDays < 2) return "Yesterday";
                if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} days ago";
                return UpdatedAt.ToString("MMM d");
            }
        }

        [JsonIgnore]
        public Color BrandColor
        {
            get
            {
                switch (ProviderId?.ToLowerInvariant())
                {
                    case "gemini":
                        return Color.FromArgb(255, 78, 130, 238); // #4E82EE
                    case "claude":
                        return Color.FromArgb(255, 217, 119, 87); // #D97757
                    case "perplexity":
                        return Color.FromArgb(255, 32, 85, 101); // #205565
                    case "openai":
                    default:
                        return Color.FromArgb(255, 16, 163, 127); // #10A37F
                }
            }
        }

        [JsonIgnore]
        public SolidColorBrush BrandBrush => new SolidColorBrush(BrandColor);

        [JsonIgnore]
        public string LastMessageSnippet => DisplayPreview;
    }
}
