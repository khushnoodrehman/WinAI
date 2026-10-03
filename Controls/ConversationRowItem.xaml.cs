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

            IconBorder.Background = session.BrandBrush;
            IconContentPresenter.Content = CreateProviderIcon(session.ProviderId);
        }

        private FrameworkElement CreateProviderIcon(string providerId)
        {
            switch (providerId?.ToLowerInvariant())
            {
                case "gemini":
                    // Gemini 4-point sparkle star
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M12,2 C12,7.5 16.5,12 22,12 C16.5,12 12,16.5 12,22 C12,16.5 7.5,12 2,12 C7.5,12 12,7.5 12,2 Z"),
                        Fill = new SolidColorBrush(Colors.White),
                        Stretch = Stretch.Uniform,
                        Width = 19,
                        Height = 19
                    };

                case "claude":
                    // Claude rays / asterisk symbol
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M11,2 L13,2 L13,8 L17.2,3.8 L18.6,5.2 L14.4,9.4 L20,9.4 L20,11.4 L14.4,11.4 L18.6,15.6 L17.2,17 L13,12.8 L13,19 L11,19 L11,12.8 L6.8,17 L5.4,15.6 L9.6,11.4 L4,11.4 L4,9.4 L9.6,9.4 L5.4,5.2 L6.8,3.8 L11,8 Z"),
                        Fill = new SolidColorBrush(Colors.White),
                        Stretch = Stretch.Uniform,
                        Width = 19,
                        Height = 19
                    };

                case "perplexity":
                    // Perplexity intersecting geometric weave
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M11,3 L13,3 L13,7 L17,7 L17,9 L13,9 L13,13 L17,13 L17,15 L13,15 L13,19 L11,19 L11,15 L7,15 L7,13 L11,13 L11,9 L7,9 L7,7 L11,7 Z M9,9 L9,13 L11,13 L11,9 Z"),
                        Fill = new SolidColorBrush(Colors.White),
                        Stretch = Stretch.Uniform,
                        Width = 19,
                        Height = 19
                    };

                case "openai":
                default:
                    // OpenAI flower / rosette vector icon
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M12,2 C10.9,2 10,2.9 10,4 C10,4.4 10.1,4.7 10.3,5 C9.8,5.1 9.4,5.4 9.1,5.8 C8.4,5.3 7.5,5 6.5,5 C4.6,5 3,6.6 3,8.5 C3,9.1 3.2,9.7 3.5,10.2 C2.6,10.6 2,11.5 2,12.5 C2,13.9 3.1,15 4.5,15 C4.9,15 5.2,14.9 5.5,14.7 C5.8,15.5 6.6,16 7.5,16 C8.4,16 9.2,15.5 9.5,14.7 C9.8,15.5 10.6,16 11.5,16 C12.9,16 14,14.9 14,13.5 C14,12.9 13.8,12.3 13.5,11.8 C14.4,11.4 15,10.5 15,9.5 C15,8.1 13.9,7 12.5,7 C12.1,7 11.8,7.1 11.5,7.3 C11.2,6.5 10.4,6 9.5,6 C9.3,6 9.2,6 9,6.1 C9,5.4 9.4,4.8 10,4.4 C10.3,4.8 10.8,5 11.3,5 C12.4,5 13.3,4.1 13.3,3 C13.3,2.4 12.7,2 12,2 Z"),
                        Fill = new SolidColorBrush(Colors.White),
                        Stretch = Stretch.Uniform,
                        Width = 20,
                        Height = 20
                    };
            }
        }

        private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (Application.Current.Resources.TryGetValue("AppItemPressedBrush", out object brush))
            {
                RootGrid.Background = brush as Brush;
            }
        }

        private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            RootGrid.Background = new SolidColorBrush(Colors.Transparent);
        }

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
