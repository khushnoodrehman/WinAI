using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using WinAI.Services;

namespace WinAI.Views
{
    public sealed partial class SettingsPage : Page
    {
        private const string SettingFontSizeKey = "App_FontSize";
        private const string SettingLanguageKey = "App_Language";
        private const string SettingDefaultProviderKey = "App_DefaultProvider";
        private const string SettingDefaultModelKey = "App_DefaultModel";

        private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;
        private readonly ThemeService _themeService = ThemeService.Instance;
        private readonly CredentialVaultService _vaultService = CredentialVaultService.Instance;
        private readonly ChatHistoryService _chatHistoryService = ChatHistoryService.Instance;
        private DispatcherTimer _notificationTimer;

        public SettingsPage()
        {
            this.InitializeComponent();
            SetupNotificationTimer();
        }

        private void SetupNotificationTimer()
        {
            _notificationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _notificationTimer.Tick += (s, e) =>
            {
                _notificationTimer.Stop();
                NotificationBanner.Visibility = Visibility.Collapsed;
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            LoadCurrentSettings();

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested += OnBackRequested;
            navManager.AppViewBackButtonVisibility = Frame.CanGoBack
                ? AppViewBackButtonVisibility.Visible
                : AppViewBackButtonVisibility.Collapsed;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested -= OnBackRequested;

            if (_notificationTimer != null && _notificationTimer.IsEnabled)
            {
                _notificationTimer.Stop();
            }
        }

        private void OnBackRequested(object sender, BackRequestedEventArgs e)
        {
            if (NavDrawer.IsPaneOpen)
            {
                e.Handled = true;
                NavDrawer.IsPaneOpen = false;
                return;
            }

            if (Frame.CanGoBack)
            {
                e.Handled = true;
                Frame.GoBack();
            }
            else
            {
                e.Handled = true;
                Frame.Navigate(typeof(HomePage));
            }
        }

        private void LoadCurrentSettings()
        {
            // 1. Theme
            UpdateThemeDisplayText();

            // 2. Font Size
            string fontSize = GetSetting(SettingFontSizeKey, "Medium");
            FontSizeValueText.Text = fontSize;

            // 3. Language
            string language = GetSetting(SettingLanguageKey, "English");
            LanguageValueText.Text = language;

            // 4. Default Provider
            string provider = GetSetting(SettingDefaultProviderKey, "OpenAI");
            DefaultProviderValueText.Text = provider;

            // 5. Default Model
            string model = GetSetting(SettingDefaultModelKey, "GPT-4o");
            DefaultModelValueText.Text = model;
        }

        private void UpdateThemeDisplayText()
        {
            switch (_themeService.CurrentTheme)
            {
                case ElementTheme.Light:
                    ThemeValueText.Text = "Light";
                    break;
                case ElementTheme.Dark:
                    ThemeValueText.Text = "Dark";
                    break;
                default:
                    ThemeValueText.Text = "System";
                    break;
            }
        }

        private string GetSetting(string key, string defaultValue)
        {
            if (_localSettings.Values.TryGetValue(key, out object val) && val is string str && !string.IsNullOrWhiteSpace(str))
            {
                return str;
            }
            return defaultValue;
        }

        private void SetSetting(string key, string value)
        {
            _localSettings.Values[key] = value;
        }

        #region Appearance Section Interactions

        private async void ThemeRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Choose Theme",
                PrimaryButtonText = "Apply",
                SecondaryButtonText = "Cancel"
            };

            var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

            var combo = new ComboBox
            {
                Header = "App Theme",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            combo.Items.Add(new ComboBoxItem { Content = "Dark Theme", Tag = ElementTheme.Dark });
            combo.Items.Add(new ComboBoxItem { Content = "Light Theme", Tag = ElementTheme.Light });
            combo.Items.Add(new ComboBoxItem { Content = "Use System Setting", Tag = ElementTheme.Default });

            switch (_themeService.CurrentTheme)
            {
                case ElementTheme.Dark:
                    combo.SelectedIndex = 0;
                    break;
                case ElementTheme.Light:
                    combo.SelectedIndex = 1;
                    break;
                default:
                    combo.SelectedIndex = 2;
                    break;
            }

            stack.Children.Add(combo);
            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && combo.SelectedItem is ComboBoxItem selectedItem)
            {
                var selectedTheme = (ElementTheme)selectedItem.Tag;
                _themeService.SetTheme(selectedTheme);
                UpdateThemeDisplayText();
                ShowNotification($"Theme set to {ThemeValueText.Text}.");
            }
        }

        private async void FontSizeRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Font Size",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Cancel"
            };

            var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

            var combo = new ComboBox
            {
                Header = "Reading Size",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            combo.Items.Add(new ComboBoxItem { Content = "Small", Tag = "Small" });
            combo.Items.Add(new ComboBoxItem { Content = "Medium (Default)", Tag = "Medium" });
            combo.Items.Add(new ComboBoxItem { Content = "Large", Tag = "Large" });

            string current = GetSetting(SettingFontSizeKey, "Medium");
            if (current == "Small") combo.SelectedIndex = 0;
            else if (current == "Large") combo.SelectedIndex = 2;
            else combo.SelectedIndex = 1;

            stack.Children.Add(combo);
            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && combo.SelectedItem is ComboBoxItem selectedItem)
            {
                string choice = selectedItem.Tag as string ?? "Medium";
                SetSetting(SettingFontSizeKey, choice);
                FontSizeValueText.Text = choice;
                ShowNotification($"Font size updated to {choice}.");
            }
        }

        private async void LanguageRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "App Language",
                Content = new TextBlock
                {
                    Text = "English is currently the active application language.\n\nAdditional language packs will become available in upcoming releases of WinAI.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13.5
                },
                PrimaryButtonText = "OK"
            };

            await dialog.ShowAsync();
        }

        #endregion

        #region AI & Chat Section Interactions

        private async void DefaultProviderRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Default AI Provider",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Cancel"
            };

            var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

            var combo = new ComboBox
            {
                Header = "Preferred Provider",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var providers = new[]
            {
                new { Id = "openai", Name = "OpenAI" },
                new { Id = "gemini", Name = "Gemini" },
                new { Id = "claude", Name = "Claude" },
                new { Id = "perplexity", Name = "Perplexity" },
                new { Id = "deepseek", Name = "DeepSeek" },
                new { Id = "xai", Name = "xAI" }
            };

            string current = GetSetting(SettingDefaultProviderKey, "OpenAI");
            int selectedIndex = 0;

            for (int i = 0; i < providers.Length; i++)
            {
                bool isConfigured = _vaultService.HasApiKey(providers[i].Id);
                string suffix = isConfigured ? " (Configured)" : " (No Key)";
                combo.Items.Add(new ComboBoxItem
                {
                    Content = providers[i].Name + suffix,
                    Tag = providers[i].Name
                });

                if (string.Equals(providers[i].Name, current, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                }
            }

            combo.SelectedIndex = selectedIndex;
            stack.Children.Add(combo);

            var note = new TextBlock
            {
                Text = "To configure API keys for any provider, open Key Vault from the side drawer.",
                FontSize = 12,
                Foreground = Application.Current.Resources["AppTextSecondaryBrush"] as Windows.UI.Xaml.Media.Brush,
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(note);

            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && combo.SelectedItem is ComboBoxItem selectedItem)
            {
                string provider = selectedItem.Tag as string ?? "OpenAI";
                SetSetting(SettingDefaultProviderKey, provider);
                DefaultProviderValueText.Text = provider;

                // Set a sensible default model matching this provider
                string model = GetDefaultModelForProvider(provider);
                SetSetting(SettingDefaultModelKey, model);
                DefaultModelValueText.Text = model;

                ShowNotification($"Default provider set to {provider}.");
            }
        }

        private async void DefaultModelRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            string currentProvider = GetSetting(SettingDefaultProviderKey, "OpenAI");

            var dialog = new ContentDialog
            {
                Title = $"Default Model ({currentProvider})",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Cancel"
            };

            var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

            var combo = new ComboBox
            {
                Header = "Model for New Chats",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var cached = ModelService.Instance.GetModelsForProvider(currentProvider);
            string currentModel = GetSetting(SettingDefaultModelKey, cached.Count > 0 ? cached[0].Id : "gpt-4o");
            int selectedIndex = 0;

            if (cached != null && cached.Count > 0)
            {
                for (int i = 0; i < cached.Count; i++)
                {
                    combo.Items.Add(new ComboBoxItem { Content = cached[i].DisplayName, Tag = cached[i].Id });
                    if (string.Equals(cached[i].Id, currentModel, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                    }
                }
            }
            else
            {
                var fallbackModels = GetModelsForProvider(currentProvider);
                for (int i = 0; i < fallbackModels.Length; i++)
                {
                    combo.Items.Add(new ComboBoxItem { Content = fallbackModels[i], Tag = fallbackModels[i] });
                    if (string.Equals(fallbackModels[i], currentModel, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                    }
                }
            }

            combo.SelectedIndex = selectedIndex;
            stack.Children.Add(combo);

            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && combo.SelectedItem is ComboBoxItem selectedItem)
            {
                string model = selectedItem.Tag as string ?? (cached.Count > 0 ? cached[0].Id : "gpt-4o");
                SetSetting(SettingDefaultModelKey, model);
                DefaultModelValueText.Text = selectedItem.Content as string ?? model;
                ShowNotification($"Default model set to {DefaultModelValueText.Text}.");
            }
        }

        private string[] GetModelsForProvider(string provider)
        {
            var cached = ModelService.Instance.GetModelsForProvider(provider);
            if (cached != null && cached.Count > 0)
            {
                return cached.Select(m => m.Id).ToArray();
            }

            switch (provider?.ToLowerInvariant())
            {
                case "gemini":
                    return new[] { "gemini-1.5-flash", "gemini-2.0-flash", "gemini-1.5-pro" };
                case "claude":
                    return new[] { "claude-3-5-sonnet-20241022", "claude-3-5-haiku-20241022", "claude-3-opus" };
                case "perplexity":
                    return new[] { "sonar", "sonar-pro", "sonar-reasoning" };
                case "deepseek":
                    return new[] { "deepseek-chat", "deepseek-reasoner" };
                case "xai":
                    return new[] { "grok-2-1212", "grok-2-vision-1212", "grok-beta" };
                default:
                    return new[] { "gpt-4o", "gpt-4o-mini", "o1-mini", "gpt-3.5-turbo" };
            }
        }

        private string GetDefaultModelForProvider(string provider)
        {
            var list = GetModelsForProvider(provider);
            return list.Length > 0 ? list[0] : "gpt-4o";
        }

        private void ChatHistoryRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Frame.Navigate(typeof(ConversationsPage));
        }

        private async void ClearConversationsRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Clear All Conversations?",
                Content = "All saved conversations will be permanently deleted from this device. This action cannot be undone.",
                PrimaryButtonText = "Clear",
                SecondaryButtonText = "Cancel"
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await _chatHistoryService.ClearAllSessionsAsync();
                ShowNotification("All chat conversations have been cleared.");
            }
        }

        #endregion

        #region Privacy & Security Section Interactions

        private void KeyVaultRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Frame.Navigate(typeof(KeyVaultPage));
        }

        private void PrivacyCenterRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Frame.Navigate(typeof(PrivacyCenterPage));
        }

        #endregion

        #region Navigation & Notifications

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            NavDrawer.IsPaneOpen = !NavDrawer.IsPaneOpen;
        }

        private void ShowNotification(string message)
        {
            NotificationText.Text = message;
            NotificationBanner.Visibility = Visibility.Visible;

            _notificationTimer.Stop();
            _notificationTimer.Start();
        }

        // Drawer Menu Item Handlers
        private void DrawerHome_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(HomePage));
        private void DrawerNewChat_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(MainPage));
        private void DrawerConversations_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ConversationsPage));
        private void DrawerProviders_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(KeyVaultPage));
        private void DrawerVault_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(KeyVaultPage));
        private void DrawerPromptKit_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(PromptKitPage));
        private void DrawerSettings_Click(object sender, RoutedEventArgs e) => NavDrawer.IsPaneOpen = false;
        private void DrawerPrivacy_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(PrivacyCenterPage));
        private void DrawerAbout_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(AboutPage));

        // Bottom Navigation Bar Handlers
        private void BottomHome_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(HomePage));
        private void BottomChats_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ConversationsPage));
        private void BottomVault_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(KeyVaultPage));
        private void BottomMore_Click(object sender, RoutedEventArgs e) => NavDrawer.IsPaneOpen = false;

        #endregion
    }
}
