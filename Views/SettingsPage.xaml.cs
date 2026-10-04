using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.Storage;
using Windows.UI.Core;
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
    public sealed partial class SettingsPage : Page
    {
        private const string SettingFontSizeKey = "App_FontSize";
        private const string SettingLanguageKey = "App_Language";
        private const string SettingDefaultProviderKey = "App_DefaultProvider";
        private const string SettingDefaultModelKey = "App_DefaultModel";

        private static readonly bool _hasMenuFlyoutIcon = 
            ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Controls.MenuFlyoutItem", "Icon");

        private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;
        private readonly ThemeService _themeService = ThemeService.Instance;
        private readonly CredentialVaultService _vaultService = CredentialVaultService.Instance;
        private readonly ChatHistoryService _chatHistoryService = ChatHistoryService.Instance;
        private readonly AiProviderRegistry _providerRegistry = AiProviderRegistry.Instance;
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
            string fontSize = GetSetting(SettingFontSizeKey, "Default");
            if (string.Equals(fontSize, "Medium", StringComparison.OrdinalIgnoreCase))
            {
                fontSize = "Default";
            }
            FontSizeValueText.Text = fontSize;

            // 3. Language
            string language = GetSetting(SettingLanguageKey, "English");
            LanguageValueText.Text = language;

            // 4. Live Tile Style
            TileStyleValueText.Text = LiveTileService.Instance.CurrentTileStyle == LiveTileService.StyleColorful ? "Colorful" : "Transparent";

            // 5. Default Provider
            string providerId = AppSettingsService.Instance.DefaultProviderId;
            var providerObj = AiProviderRegistry.Instance.GetProvider(providerId);
            DefaultProviderValueText.Text = providerObj?.DisplayName ?? providerId;

            // 5. Default Model
            var modelDesc = AppSettingsService.Instance.GetDefaultModelDescriptor();
            DefaultModelValueText.Text = modelDesc?.DisplayName ?? AppSettingsService.Instance.DefaultModelId;
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

        #region Native Selection UI Helpers

        private MenuFlyoutItem CreateHeaderItem(string title)
        {
            var item = new MenuFlyoutItem
            {
                Text = title,
                IsEnabled = false
            };
            if (Application.Current.Resources.TryGetValue("ModernMenuFlyoutHeaderStyle", out object styleObj) && styleObj is Style style)
            {
                item.Style = style;
            }
            return item;
        }

        private MenuFlyoutItem CreateSelectionItem(string text, bool isSelected)
        {
            var item = new MenuFlyoutItem();
            if (Application.Current.Resources.TryGetValue("ModernMenuFlyoutItemStyle", out object styleObj) && styleObj is Style style)
            {
                item.Style = style;
            }

            var accentBrush = Application.Current.Resources["AppAccentBrush"] as SolidColorBrush;

            if (_hasMenuFlyoutIcon)
            {
                item.Text = text;
                if (isSelected)
                {
                    item.Icon = new FontIcon
                    {
                        Glyph = "\uE73E",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        Foreground = accentBrush,
                        FontSize = 13
                    };
                    if (accentBrush != null)
                    {
                        item.Foreground = accentBrush;
                    }
                }
                else
                {
                    item.Icon = new FontIcon
                    {
                        Glyph = "\uE73E",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        Opacity = 0,
                        FontSize = 13
                    };
                }
            }
            else
            {
                item.Text = isSelected ? $"\uE73E  {text}" : $"    {text}";
                if (isSelected && accentBrush != null)
                {
                    item.Foreground = accentBrush;
                }
            }

            return item;
        }

        private MenuFlyout CreateBaseMenuFlyout()
        {
            var flyout = new MenuFlyout();
            if (Application.Current.Resources.TryGetValue("ModernMenuFlyoutStyle", out object pStyleObj) && pStyleObj is Style pStyle)
            {
                flyout.MenuFlyoutPresenterStyle = pStyle;
            }
            return flyout;
        }

        #endregion

        #region Appearance Section Interactions

        private void ThemeRow_Tapped(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement target)) return;

            var flyout = CreateBaseMenuFlyout();

            flyout.Items.Add(CreateHeaderItem("Theme"));
            flyout.Items.Add(new MenuFlyoutSeparator());

            var options = new[]
            {
                new { Label = "System", Theme = ElementTheme.Default },
                new { Label = "Light", Theme = ElementTheme.Light },
                new { Label = "Dark", Theme = ElementTheme.Dark }
            };

            var currentTheme = _themeService.CurrentTheme;

            foreach (var opt in options)
            {
                bool isSelected = opt.Theme == currentTheme;
                var item = CreateSelectionItem(opt.Label, isSelected);
                var chosenTheme = opt.Theme;
                var chosenLabel = opt.Label;

                item.Click += (s, args) =>
                {
                    _themeService.SetTheme(chosenTheme);
                    UpdateThemeDisplayText();
                    ShowNotification($"Theme set to {chosenLabel}.");
                };

                flyout.Items.Add(item);
            }

            flyout.ShowAt(target);
        }

        private void FontSizeRow_Tapped(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement target)) return;

            var flyout = CreateBaseMenuFlyout();

            flyout.Items.Add(CreateHeaderItem("Font Size"));
            flyout.Items.Add(new MenuFlyoutSeparator());

            string current = GetSetting(SettingFontSizeKey, "Default");
            if (string.Equals(current, "Medium", StringComparison.OrdinalIgnoreCase))
            {
                current = "Default";
            }

            var sizes = new[] { "Small", "Default", "Large" };

            foreach (var size in sizes)
            {
                bool isSelected = string.Equals(size, current, StringComparison.OrdinalIgnoreCase);
                var item = CreateSelectionItem(size, isSelected);
                string chosenSize = size;

                item.Click += (s, args) =>
                {
                    SetSetting(SettingFontSizeKey, chosenSize);
                    AppSettingsService.Instance.SetString(AppSettingsService.SettingFontSizeKey, chosenSize);
                    FontSizeValueText.Text = chosenSize;
                    ShowNotification($"Font size updated to {chosenSize}.");
                };

                flyout.Items.Add(item);
            }

            flyout.ShowAt(target);
        }

        private void LanguageRow_Tapped(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement target)) return;

            var flyout = CreateBaseMenuFlyout();

            flyout.Items.Add(CreateHeaderItem("Language"));
            flyout.Items.Add(new MenuFlyoutSeparator());

            string current = GetSetting(SettingLanguageKey, "English");

            var languages = new[] { "English", "Urdu" };

            foreach (var lang in languages)
            {
                bool isSelected = string.Equals(lang, current, StringComparison.OrdinalIgnoreCase);
                var item = CreateSelectionItem(lang, isSelected);
                string chosenLang = lang;

                item.Click += (s, args) =>
                {
                    SetSetting(SettingLanguageKey, chosenLang);
                    AppSettingsService.Instance.SetString(AppSettingsService.SettingLanguageKey, chosenLang);
                    LanguageValueText.Text = chosenLang;
                    ShowNotification($"Language updated to {chosenLang}.");
                };

                flyout.Items.Add(item);
            }

            flyout.ShowAt(target);
        }

        private void TileStyleRow_Tapped(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement target)) return;

            var flyout = CreateBaseMenuFlyout();

            flyout.Items.Add(CreateHeaderItem("Live Tile Style"));
            flyout.Items.Add(new MenuFlyoutSeparator());

            var options = new[]
            {
                new { Label = "Transparent", Style = LiveTileService.StyleTransparent, Subtitle = "Uses phone accent color" },
                new { Label = "Colorful", Style = LiveTileService.StyleColorful, Subtitle = "Branded icon on colored tile" }
            };

            var currentStyle = LiveTileService.Instance.CurrentTileStyle;

            foreach (var opt in options)
            {
                bool isSelected = string.Equals(opt.Style, currentStyle, StringComparison.OrdinalIgnoreCase);
                var item = CreateSelectionItem(opt.Label, isSelected);
                var chosenStyle = opt.Style;
                var chosenLabel = opt.Label;

                item.Click += (s, args) =>
                {
                    LiveTileService.Instance.SetTileStyle(chosenStyle);
                    TileStyleValueText.Text = chosenLabel;
                    ShowNotification($"Start tile set to {chosenLabel}.");
                };

                flyout.Items.Add(item);
            }

            flyout.ShowAt(target);
        }

        #endregion

        #region AI & Chat Section Interactions

        private void DefaultProviderRow_Tapped(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement target)) return;

            var flyout = CreateBaseMenuFlyout();

            flyout.Items.Add(CreateHeaderItem("Default Provider"));
            flyout.Items.Add(new MenuFlyoutSeparator());

            string currentProviderId = AppSettingsService.Instance.DefaultProviderId;
            var providers = _providerRegistry.GetProviders();

            foreach (var provider in providers)
            {
                bool isSelected = string.Equals(provider.Id, currentProviderId, StringComparison.OrdinalIgnoreCase);
                var item = CreateSelectionItem(provider.DisplayName, isSelected);
                var prov = provider;

                item.Click += (s, args) =>
                {
                    string newProvId = prov.Id;
                    var availableModels = _providerRegistry.GetModelsForProvider(newProvId, onlyAvailable: true);
                    string newModelId = (availableModels != null && availableModels.Count > 0) ? availableModels[0].Id : "gpt-4o";
                    string newModelName = (availableModels != null && availableModels.Count > 0) ? availableModels[0].DisplayName : "GPT-4o";

                    AppSettingsService.Instance.SetDefaultModel(newProvId, newModelId, newModelName);
                    DefaultProviderValueText.Text = prov.DisplayName;
                    DefaultModelValueText.Text = newModelName;

                    ShowNotification($"Default provider set to {prov.DisplayName}.");
                };

                flyout.Items.Add(item);
            }

            flyout.ShowAt(target);
        }

        private void DefaultModelRow_Tapped(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement target)) return;

            var flyout = CreateBaseMenuFlyout();

            string currentProviderId = AppSettingsService.Instance.DefaultProviderId;
            var providerObj = _providerRegistry.GetProvider(currentProviderId);
            string providerName = providerObj?.DisplayName ?? "AI";

            flyout.Items.Add(CreateHeaderItem($"Default Model ({providerName})"));
            flyout.Items.Add(new MenuFlyoutSeparator());

            string currentModelId = AppSettingsService.Instance.DefaultModelId;

            // Shared model registry / cache query
            var allModels = _providerRegistry.GetModelsForProvider(currentProviderId);

            // Requirement 7:
            // Only show models that are currently: Available
            // Do NOT show models marked: Unavailable, Deprecated, Not accessible, Missing required credentials
            var eligibleModels = allModels.Where(m =>
                m.IsAvailable &&
                !string.Equals(m.Status, "Unavailable", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(m.Status, "Deprecated", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(m.Status, "Not accessible", StringComparison.OrdinalIgnoreCase) &&
                (!m.RequiresApiKey || m.IsConfigured || _vaultService.HasApiKey(m.ProviderId))
            ).ToList();

            if (eligibleModels.Count == 0)
            {
                var emptyItem = new MenuFlyoutItem
                {
                    Text = "No available models with API key",
                    IsEnabled = false
                };
                if (Application.Current.Resources.TryGetValue("ModernMenuFlyoutItemStyle", out object itemStyleObj) && itemStyleObj is Style iStyle)
                {
                    emptyItem.Style = iStyle;
                }
                flyout.Items.Add(emptyItem);

                var keyVaultHint = new MenuFlyoutItem
                {
                    Text = "Configure in Key Vault →"
                };
                if (Application.Current.Resources.TryGetValue("ModernMenuFlyoutItemStyle", out object kvStyleObj) && kvStyleObj is Style kvStyle)
                {
                    keyVaultHint.Style = kvStyle;
                }
                if (Application.Current.Resources["AppAccentBrush"] is SolidColorBrush accent)
                {
                    keyVaultHint.Foreground = accent;
                }
                keyVaultHint.Click += (s, args) =>
                {
                    Frame.Navigate(typeof(KeyVaultPage));
                };
                flyout.Items.Add(keyVaultHint);
            }
            else
            {
                foreach (var m in eligibleModels)
                {
                    bool isSelected = string.Equals(m.Id, currentModelId, StringComparison.OrdinalIgnoreCase);
                    var item = CreateSelectionItem(m.DisplayName, isSelected);
                    var desc = m;

                    item.Click += (s, args) =>
                    {
                        AppSettingsService.Instance.SetDefaultModel(desc.ProviderId, desc.Id, desc.DisplayName);
                        DefaultModelValueText.Text = desc.DisplayName;
                        ShowNotification($"Default model set to {desc.DisplayName}.");
                    };

                    flyout.Items.Add(item);
                }
            }

            flyout.ShowAt(target);
        }

        private void ChatHistoryRow_Tapped(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ConversationsPage));
        }

        private async void ClearConversationsRow_Tapped(object sender, RoutedEventArgs e)
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

        private void KeyVaultRow_Tapped(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(KeyVaultPage));
        }

        private void PrivacyCenterRow_Tapped(object sender, RoutedEventArgs e)
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
        private void DrawerNewChat_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(MainPage), "new");
        private void DrawerConversations_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ConversationsPage));
        private void DrawerProviders_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(AiProvidersPage));
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
