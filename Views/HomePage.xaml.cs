using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.Storage;
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
using WinAI.Services.Providers;

namespace WinAI.Views
{
    public sealed partial class HomePage : Page
    {
        private readonly ThemeService _themeService = ThemeService.Instance;
        private readonly AiProviderRegistry _providerRegistry = AiProviderRegistry.Instance;

        private string _selectedProviderId = "openai";
        private string _selectedModelId = "gpt-4o";
        private string _selectedModelDisplayName = "GPT-4o";

        public HomePage()
        {
            this.InitializeComponent();
            InitializeGreeting();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Hide back button on the Home Screen root
            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
            navManager.BackRequested += HomePage_BackRequested;

            UpdateStatusBar();

            // Load default model from Settings and populate model picker
            LoadDefaultModelFromSettings();
            BuildModelPickerFlyout();

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
                GreetingTitleText.Text = "Good morning.";
            }
            else if (hour < 17)
            {
                GreetingTitleText.Text = "Good afternoon.";
            }
            else
            {
                GreetingTitleText.Text = "Good evening.";
            }
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

        #region Default Model & Provider Loading

        private void LoadDefaultModelFromSettings()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings.Values;
                string defaultProvider = localSettings.ContainsKey("App_DefaultProvider")
                    ? localSettings["App_DefaultProvider"] as string
                    : "OpenAI";
                string defaultModel = localSettings.ContainsKey("App_DefaultModel")
                    ? localSettings["App_DefaultModel"] as string
                    : "GPT-4o";

                string normProvider = AiProviderRegistry.NormalizeProviderId(defaultProvider);
                var descriptor = _providerRegistry.GetModel(normProvider, defaultModel);

                if (descriptor != null)
                {
                    SetSelectedModel(descriptor.ProviderId, descriptor.Id, descriptor.DisplayName);
                }
                else
                {
                    SetSelectedModel(normProvider, defaultModel ?? "gpt-4o", defaultModel ?? "GPT-4o");
                }
            }
            catch
            {
                SetSelectedModel("openai", "gpt-4o", "GPT-4o");
            }
        }

        private void SetSelectedModel(string providerId, string modelId, string displayName)
        {
            _selectedProviderId = providerId ?? "openai";
            _selectedModelId = modelId ?? "gpt-4o";
            _selectedModelDisplayName = displayName ?? "GPT-4o";

            if (HomeSelectedModelText != null)
            {
                HomeSelectedModelText.Text = _selectedModelDisplayName;
            }

            UpdateModelIconVisuals(_selectedProviderId);
        }

        private void UpdateModelIconVisuals(string providerId)
        {
            if (HomeModelIconContainer == null || HomeModelIconGlyph == null) return;

            string norm = AiProviderRegistry.NormalizeProviderId(providerId);
            switch (norm)
            {
                case "google":
                case "gemini":
                    HomeModelIconContainer.Background = new SolidColorBrush(Color.FromArgb(255, 78, 130, 238));
                    HomeModelIconGlyph.Text = "\uE80A";
                    break;
                case "anthropic":
                case "claude":
                    HomeModelIconContainer.Background = new SolidColorBrush(Color.FromArgb(255, 217, 119, 87));
                    HomeModelIconGlyph.Text = "\uE749";
                    break;
                case "deepseek":
                    HomeModelIconContainer.Background = new SolidColorBrush(Color.FromArgb(255, 29, 78, 216));
                    HomeModelIconGlyph.Text = "\uE756";
                    break;
                case "xai":
                case "grok":
                    HomeModelIconContainer.Background = new SolidColorBrush(Color.FromArgb(255, 30, 41, 59));
                    HomeModelIconGlyph.Text = "\uE7C3";
                    break;
                case "perplexity":
                    HomeModelIconContainer.Background = new SolidColorBrush(Color.FromArgb(255, 32, 85, 101));
                    HomeModelIconGlyph.Text = "\uE721";
                    break;
                case "openai":
                default:
                    HomeModelIconContainer.Background = new SolidColorBrush(Color.FromArgb(255, 16, 163, 127));
                    HomeModelIconGlyph.Text = "\uE8BD";
                    break;
            }
        }

        private void BuildModelPickerFlyout()
        {
            if (HomeModelPickerFlyout == null) return;

            HomeModelPickerFlyout.Items.Clear();

            var allModels = _providerRegistry.GetAllModels();
            if (allModels == null || allModels.Count == 0) return;

            string lastProvider = null;
            foreach (var m in allModels)
            {
                if (m.ProviderName != lastProvider)
                {
                    lastProvider = m.ProviderName;
                    var headerItem = new MenuFlyoutItem
                    {
                        Text = $"— {m.ProviderName} —",
                        IsEnabled = false,
                        FontSize = 12
                    };
                    HomeModelPickerFlyout.Items.Add(headerItem);
                }

                var item = new MenuFlyoutItem
                {
                    Text = m.DisplayName,
                    Tag = m
                };
                item.Click += (s, args) =>
                {
                    if (s is MenuFlyoutItem clicked && clicked.Tag is AiModelDescriptor desc)
                    {
                        SetSelectedModel(desc.ProviderId, desc.Id, desc.DisplayName);
                    }
                };
                HomeModelPickerFlyout.Items.Add(item);
            }
        }

        #endregion

        #region Prompt Submission & Navigation

        private void HomePromptTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                var shiftState = CoreWindow.GetForCurrentThread().GetKeyState(Windows.System.VirtualKey.Shift);
                bool isShift = (shiftState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
                if (!isShift)
                {
                    e.Handled = true;
                    SubmitPrompt();
                }
            }
        }

        private void HomePromptTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Optional: update UI state if needed
        }

        private void HomeSendButton_Click(object sender, RoutedEventArgs e)
        {
            SubmitPrompt();
        }

        private void SubmitPrompt()
        {
            string prompt = HomePromptTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(prompt)) return;

            HomePromptTextBox.Text = string.Empty;

            // Direct transition into Chat screen with prompt and active model
            Frame.Navigate(typeof(MainPage), new ChatLaunchArgs(prompt, _selectedProviderId, _selectedModelId));
        }

        #endregion

        #region Recent Conversations

        private void ConversationItem_Click(object sender, Conversation conv)
        {
            if (conv != null)
            {
                Frame.Navigate(typeof(MainPage), !string.IsNullOrEmpty(conv.Id) ? conv.Id : conv.Title);
            }
        }

        private void ViewAllConversations_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ConversationsPage));
        }

        private async Task RefreshRecentConversationsAsync()
        {
            try
            {
                var convs = await Data.Services.ConversationService.Instance.GetConversationsAsync(includeArchived: false);
                if (convs != null && convs.Count > 0)
                {
                    EmptyRecentMessageText.Visibility = Visibility.Collapsed;

                    // Item 1
                    Conv1Item.Conversation = new Conversation(convs[0].Id, convs[0].Title, FormatTimeAgo(convs[0].LocalUpdatedAt), convs[0].LastMessagePreview);
                    Conv1Item.Visibility = Visibility.Visible;

                    // Item 2
                    if (convs.Count > 1)
                    {
                        Conv1Divider.Visibility = Visibility.Visible;
                        Conv2Item.Conversation = new Conversation(convs[1].Id, convs[1].Title, FormatTimeAgo(convs[1].LocalUpdatedAt), convs[1].LastMessagePreview);
                        Conv2Item.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        Conv1Divider.Visibility = Visibility.Collapsed;
                        Conv2Item.Visibility = Visibility.Collapsed;
                    }

                    // Item 3
                    if (convs.Count > 2)
                    {
                        Conv2Divider.Visibility = Visibility.Visible;
                        Conv3Item.Conversation = new Conversation(convs[2].Id, convs[2].Title, FormatTimeAgo(convs[2].LocalUpdatedAt), convs[2].LastMessagePreview);
                        Conv3Item.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        Conv2Divider.Visibility = Visibility.Collapsed;
                        Conv3Item.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    Conv1Item.Visibility = Visibility.Collapsed;
                    Conv1Divider.Visibility = Visibility.Collapsed;
                    Conv2Item.Visibility = Visibility.Collapsed;
                    Conv2Divider.Visibility = Visibility.Collapsed;
                    Conv3Item.Visibility = Visibility.Collapsed;
                    EmptyRecentMessageText.Visibility = Visibility.Visible;
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

        #endregion

        #region Navigation Drawer Actions

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

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            var nextTheme = _themeService.ToggleTheme(this);
            UpdateStatusBar();
        }

        #endregion

        #region Backward Compatibility Stubs

        private void AskAnything_Tapped(object sender, TappedRoutedEventArgs e)
        {
            SubmitPrompt();
        }

        private void AskAnything_PointerEntered(object sender, PointerRoutedEventArgs e) { }

        private void AskAnything_PointerExited(object sender, PointerRoutedEventArgs e) { }

        private void NavigateToChats_Click(object sender, RoutedEventArgs e)
        {
            ConversationsItem_Click(sender, e);
        }

        private void NavigateToVault_Click(object sender, RoutedEventArgs e)
        {
            KeyVaultItem_Click(sender, e);
        }

        private void MoreTab_Click(object sender, RoutedEventArgs e)
        {
            if (NavDrawer != null) NavDrawer.IsPaneOpen = !NavDrawer.IsPaneOpen;
        }

        private void ManageProviders_Click(object sender, RoutedEventArgs e)
        {
            AiProvidersItem_Click(sender, e);
        }

        private void ProviderItem_Click(object sender, AIProvider provider)
        {
            Frame.Navigate(typeof(MainPage), provider?.Id);
        }

        #endregion
    }
}
