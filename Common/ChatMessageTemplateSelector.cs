using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using WinAI.Models;

namespace WinAI.Common
{
    /// <summary>
    /// Chooses between User and AI message bubble DataTemplates for the chat ListView.
    /// </summary>
    public class ChatMessageTemplateSelector : DataTemplateSelector
    {
        public DataTemplate UserMessageTemplate { get; set; }
        public DataTemplate AiMessageTemplate { get; set; }
        public DataTemplate SystemMessageTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            return SelectTemplateCore(item);
        }

        protected override DataTemplate SelectTemplateCore(object item)
        {
            if (item is ChatMessage msg)
            {
                if (msg.IsSystemNotification && SystemMessageTemplate != null)
                {
                    return SystemMessageTemplate;
                }
                if (msg.IsUser)
                {
                    return UserMessageTemplate;
                }
            }

            return AiMessageTemplate;
        }
    }
}
