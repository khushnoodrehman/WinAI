using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using WinAI.Models;

namespace WinAI.Controls
{
    public sealed partial class ConversationRowItem : UserControl
    {
        public static readonly DependencyProperty SessionProperty =
            DependencyProperty.Register(
                nameof(Session),
                typeof(ChatSession),
                typeof(ConversationRowItem),
                new PropertyMetadata(null, OnSessionChanged));

        public ChatSession Session
        {
            get => (ChatSession)GetValue(SessionProperty);
            set => SetValue(SessionProperty, value);
        }

        public event EventHandler<ChatSession> ItemClick;
        public event EventHandler<ChatSession> DeleteClick;
        public event EventHandler<ChatSession> RenameClick;
        public event EventHandler<ChatSession> PinClick;

        public ConversationRowItem()
        {
            this.InitializeComponent();
        }

        private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ConversationRowItem item && e.NewValue is ChatSession session)
            {
                item.UpdateUi(session);
            }
        }

        public void UpdateUi(ChatSession session)
        {
            if (session == null) return;

            TitleText.Text = session.Title ?? "New Chat";
            PreviewText.Text = session.DisplayPreview ?? string.Empty;
            ModelText.Text = session.ModelAndCountDisplay;
            TimeAgoText.Text = session.DisplayTimeAgo ?? string.Empty;

            PinIcon.Visibility = session.IsPinned ? Visibility.Visible : Visibility.Collapsed;
            PinMenuItem.Text = session.IsPinned ? "Unpin" : "Pin";

            if (ConversationProviderIcon != null)
            {
                ConversationProviderIcon.ProviderId = session.ProviderId;
                ConversationProviderIcon.UpdateVisuals();
            }
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
            if (Session != null)
            {
                ItemClick?.Invoke(this, Session);
            }
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            // Handled by MenuFlyout
        }

        private void MenuOpen_Click(object sender, RoutedEventArgs e)
        {
            if (Session != null)
            {
                ItemClick?.Invoke(this, Session);
            }
        }

        private void MenuRename_Click(object sender, RoutedEventArgs e)
        {
            if (Session != null)
            {
                RenameClick?.Invoke(this, Session);
            }
        }

        private void MenuPin_Click(object sender, RoutedEventArgs e)
        {
            if (Session != null)
            {
                PinClick?.Invoke(this, Session);
            }
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            if (Session != null)
            {
                DeleteClick?.Invoke(this, Session);
            }
        }
    }
}
