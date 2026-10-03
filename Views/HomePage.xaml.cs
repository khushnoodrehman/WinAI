using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using WinAI.Models;
using WinAI.Services;

namespace WinAI.Views
{
    public sealed partial class HomePage : Page
    {
        private readonly ThemeService _themeService = ThemeService.Instance;

        public HomePage()
        {
            this.InitializeComponent();
            InitializeGreeting();
            InitializeMockData();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Hide back button on the Home Screen root
            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
            navManager.BackRequested += HomePage_BackRequested;

            UpdateStatusBar();
            await RefreshRecentConversationsAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested -= HomePage_BackRequested;
        }

        private void HomePage_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (NavDrawer != null && NavDrawer.IsPaneOpen)
            {
                NavDrawer.IsPaneOpen = false;
                e.Handled = true;
                return;
            }

            if (Frame.CanGoBack)
            {
                Frame.GoBack();
                e.Handled = true;
            }
        }

        private void InitializeGreeting()
        {
            int hour = DateTime.Now.Hour;
            if (hour < 12)
            {
                GreetingTitleText.Text = "Good morning,";
            }
            else if (hour < 17)
            {
                GreetingTitleText.Text = "Good afternoon,";
            }
            else
            {
                GreetingTitleText.Text = "Good evening,";
            }
        }

        private void InitializeMockData()
        {
            // 1. Initial Mock AI Providers
            OpenAiItem.Provider = new AIProvider(
                id: "openai",
                name: "OpenAI",
                selectedModel: "GPT-4o",
                brandColor: Color.FromArgb(255, 16, 163, 127), // #10A37F
                iconType: "openai"
            );

            GeminiItem.Provider = new AIProvider(
                id: "gemini",
                name: "Gemini",
                selectedModel: "Gemini 1.5 Pro",
                brandColor: Color.FromArgb(255, 78, 130, 238), // #4E82EE
                iconType: "gemini"
            );

            ClaudeItem.Provider = new AIProvider(
                id: "claude",
                name: "Claude",
                selectedModel: "Claude 3.5 Sonnet",
                brandColor: Color.FromArgb(255, 217, 119, 87), // #D97757
                iconType: "claude"
            );

            PerplexityItem.Provider = new AIProvider(
                id: "perplexity",
                name: "Perplexity",
                selectedModel: "Sonar Large",
                brandColor: Color.FromArgb(255, 32, 85, 101), // #205565
                iconType: "perplexity"
            );

            // 2. Initial Mock Recent Conversations
            Conv1Item.Conversation = new Conversation(
                id: "c1",
                title: "Project Ideas",
                timeAgo: "2 hours ago"
            );

            Conv2Item.Conversation = new Conversation(
                id: "c2",
                title: "Windows 10 Mobile",
                timeAgo: "5 hours ago"
            );

            Conv3Item.Conversation = new Conversation(
                id: "c3",
                title: "React Native Help",
                timeAgo: "Yesterday"
            );
        }

        private void UpdateStatusBar()
        {
            try
            {
                var titleBar = ApplicationView.GetForCurrentView().TitleBar;
                if (titleBar != null)
                {
                    bool isDark = this.ActualTheme == ElementTheme.Dark ||
                                  (this.ActualTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

                    var bg = isDark ? Color.FromArgb(255, 11, 12, 14) : Color.FromArgb(255, 248, 249, 250);
                    var fg = isDark ? Colors.White : Color.FromArgb(255, 26, 29, 32);

                    titleBar.BackgroundColor = bg;
                    titleBar.ForegroundColor = fg;
                    titleBar.ButtonBackgroundColor = Colors.Transparent;
                    titleBar.ButtonForegroundColor = fg;
                }
            }
            catch
            {
                // Fallback
            }
        }

        #region Navigation & Actions

        private void Hamburger_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null)
            {
                NavDrawer.IsPaneOpen = !NavDrawer.IsPaneOpen;
            }
        }

        private void CloseDrawer_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null)
            {
                NavDrawer.IsPaneOpen = false;
            }
        }

        private void HomeItem_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null)
            {
                NavDrawer.IsPaneOpen = false;
            }
        }

        private void NewChatItem_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null)
            {
                NavDrawer.IsPaneOpen = false;
            }
            Frame.Navigate(typeof(MainPage), "new");
        }

        private void ConversationsItem_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null)
            {
                NavDrawer.IsPaneOpen = false;
            }
            Frame.Navigate(typeof(ConversationsPage));
        }

        private void AiProvidersItem_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null)
            {
                NavDrawer.IsPaneOpen = false;
            }
            Frame.Navigate(typeof(KeyVaultPage));
        }

        private void KeyVaultItem_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null)
            {
                NavDrawer.IsPaneOpen = false;
            }
            Frame.Navigate(typeof(KeyVaultPage));
        }

        private void PromptKitItem_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null)
            {
                NavDrawer.IsPaneOpen = false;
            }
            Frame.Navigate(typeof(PromptKitPage));
        }

        private void SettingsItem_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null)
            {
                NavDrawer.IsPaneOpen = false;
            }
            Frame.Navigate(typeof(SettingsPage));
        }

        private void PrivacyCenterItem_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null)
            {
                NavDrawer.IsPaneOpen = false;
            }
            Frame.Navigate(typeof(PrivacyCenterPage));
        }

        private void AboutWinAiItem_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null)
            {
                NavDrawer.IsPaneOpen = false;
            }
            Frame.Navigate(typeof(AboutPage));
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            SettingsItem_Click(sender, e);
        }

        private void AskAnything_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage));
        }

        private void AskAnything_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border b && Application.Current.Resources.TryGetValue("AppItemPressedBrush", out object brush))
            {
                b.Background = brush as Brush;
            }
        }

        private void AskAnything_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border b && Application.Current.Resources.TryGetValue("AppSurfaceBrush", out object brush))
            {
                b.Background = brush as Brush;
            }
        }

        private void NavigateToChats_Click(object sender, RoutedEventArgs e)
        {
            ConversationsItem_Click(sender, e);
        }

        private void NavigateToVault_Click(object sender, RoutedEventArgs e)
        {
            KeyVaultItem_Click(sender, e);
        }

        private void ManageProviders_Click(object sender, RoutedEventArgs e)
        {
            AiProvidersItem_Click(sender, e);
        }

        private void ViewAllConversations_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ConversationsPage));
        }

        private void MoreTab_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null)
            {
                NavDrawer.IsPaneOpen = !NavDrawer.IsPaneOpen;
            }
        }

        private void ProviderItem_Click(object sender, AIProvider provider)
        {
            Frame.Navigate(typeof(MainPage), provider?.Id);
        }

        private void ConversationItem_Click(object sender, Conversation conv)
        {
            if (conv != null)
            {
                Frame.Navigate(typeof(MainPage), !string.IsNullOrEmpty(conv.Id) ? conv.Id : conv.Title);
            }
        }

        private async Task RefreshRecentConversationsAsync()
        {
            try
            {
                var convs = await Data.Services.ConversationService.Instance.GetConversationsAsync(includeArchived: false);
                if (convs != null && convs.Count > 0)
                {
                    if (convs.Count > 0 && Conv1Item != null)
                    {
                        Conv1Item.Conversation = new Conversation(convs[0].Id, convs[0].Title, FormatTimeAgo(convs[0].LocalUpdatedAt), convs[0].LastMessagePreview);
                        Conv1Item.Visibility = Visibility.Visible;
                    }
                    if (convs.Count > 1 && Conv2Item != null)
                    {
                        Conv2Item.Conversation = new Conversation(convs[1].Id, convs[1].Title, FormatTimeAgo(convs[1].LocalUpdatedAt), convs[1].LastMessagePreview);
                        Conv2Item.Visibility = Visibility.Visible;
                    }
                    else if (Conv2Item != null)
                    {
                        Conv2Item.Visibility = Visibility.Collapsed;
                    }

                    if (convs.Count > 2 && Conv3Item != null)
                    {
                        Conv3Item.Conversation = new Conversation(convs[2].Id, convs[2].Title, FormatTimeAgo(convs[2].LocalUpdatedAt), convs[2].LastMessagePreview);
                        Conv3Item.Visibility = Visibility.Visible;
                    }
                    else if (Conv3Item != null)
                    {
                        Conv3Item.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch
            {
                // Fallback gracefully
            }
        }

        private static string FormatTimeAgo(DateTime dt)
        {
            var span = DateTime.Now - dt;
            if (span.TotalMinutes < 1) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 2) return "Yesterday";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return dt.ToString("MMM d");
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            var nextTheme = _themeService.ToggleTheme(this);
            UpdateStatusBar();
        }

        #endregion
    }
}
