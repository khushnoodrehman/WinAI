using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using WinAI.Models;

namespace WinAI.Controls
{
    public sealed partial class ConversationListItem : UserControl
    {
        public static readonly DependencyProperty ConversationProperty =
            DependencyProperty.Register(
                nameof(Conversation),
                typeof(Conversation),
                typeof(ConversationListItem),
                new PropertyMetadata(null, OnConversationChanged));

        public Conversation Conversation
        {
            get => (Conversation)GetValue(ConversationProperty);
            set => SetValue(ConversationProperty, value);
        }

        public event EventHandler<Conversation> ItemClick;

        public ConversationListItem()
        {
            this.InitializeComponent();
        }

        private static void OnConversationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ConversationListItem item && e.NewValue is Conversation conv)
            {
                item.UpdateUi(conv);
            }
        }

        private void UpdateUi(Conversation conv)
        {
            if (conv == null) return;
            TitleText.Text = conv.Title ?? string.Empty;
            TimeAgoText.Text = conv.TimeAgo ?? string.Empty;
        }

        private void SetPressedVisual(bool isPressed)
        {
            VisualStateManager.GoToState(this, isPressed ? "Pressed" : "Normal", true);
        }

        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e) => SetPressedVisual(true);
        private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e) => SetPressedVisual(false);
        private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e) => SetPressedVisual(false);
        private void RootGrid_PointerCanceled(object sender, PointerRoutedEventArgs e) => SetPressedVisual(false);
        private void RootGrid_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => SetPressedVisual(false);

        private void RootGrid_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (Conversation != null)
            {
                ItemClick?.Invoke(this, Conversation);
            }
        }
    }
}
