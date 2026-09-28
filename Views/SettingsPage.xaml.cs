using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using WinAI.Services;

namespace WinAI.Views
{
    public sealed partial class SettingsPage : Page
    {
        private readonly ApiKeyService _apiKeyService = ApiKeyService.Instance;
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

            LoadKeysFromStorage();

            // Hook Windows 10 Mobile hardware/system back button
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
            if (Frame.CanGoBack)
            {
                e.Handled = true;
                Frame.GoBack();
            }
        }

        private void LoadKeysFromStorage()
        {
            OpenAiKeyBox.Password = _apiKeyService.OpenAiKey;
            GeminiKeyBox.Password = _apiKeyService.GeminiKey;
            ClaudeKeyBox.Password = _apiKeyService.ClaudeKey;
            DeepSeekKeyBox.Password = _apiKeyService.DeepSeekKey;
            PerplexityKeyBox.Password = _apiKeyService.PerplexityKey;
            GroqKeyBox.Password = _apiKeyService.GroqKey;
            OpenRouterKeyBox.Password = _apiKeyService.OpenRouterKey;
            MistralKeyBox.Password = _apiKeyService.MistralKey;
            XAiKeyBox.Password = _apiKeyService.XAiKey;
            CustomBaseUrlBox.Text = _apiKeyService.CustomBaseUrl;
            CustomKeyBox.Password = _apiKeyService.CustomKey;

            UpdateAllStatusBadges();
        }

        private void UpdateAllStatusBadges()
        {
            UpdateBadge(OpenAiStatusBadge, OpenAiKeyBox.Password);
            UpdateBadge(GeminiStatusBadge, GeminiKeyBox.Password);
            UpdateBadge(ClaudeStatusBadge, ClaudeKeyBox.Password);
            UpdateBadge(DeepSeekStatusBadge, DeepSeekKeyBox.Password);
            UpdateBadge(PerplexityStatusBadge, PerplexityKeyBox.Password);
            UpdateBadge(GroqStatusBadge, GroqKeyBox.Password);
            UpdateBadge(OpenRouterStatusBadge, OpenRouterKeyBox.Password);
            UpdateBadge(MistralStatusBadge, MistralKeyBox.Password);
            UpdateBadge(XAiStatusBadge, XAiKeyBox.Password);
        }

        private void UpdateBadge(TextBlock badge, string key)
        {
            if (badge == null) return;

            bool isSet = !string.IsNullOrWhiteSpace(key);
            badge.Text = isSet ? "✓ Configured" : "Not Set";

            if (isSet)
            {
                badge.Foreground = Application.Current.Resources["SystemControlForegroundAccentBrush"] as Brush;
                badge.FontWeight = Windows.UI.Text.FontWeights.SemiBold;
            }
            else
            {
                badge.Foreground = Application.Current.Resources["SystemControlForegroundBaseMediumBrush"] as Brush;
                badge.FontWeight = Windows.UI.Text.FontWeights.Normal;
            }
        }

        private void KeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdateAllStatusBadges();
        }

        private void CustomBaseUrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Status update for custom url if needed
        }

        #region Save & Reload Actions

        private void SaveAll_Click(object sender, RoutedEventArgs e)
        {
            _apiKeyService.SaveKeys(
                openAi: OpenAiKeyBox.Password,
                gemini: GeminiKeyBox.Password,
                claude: ClaudeKeyBox.Password,
                deepSeek: DeepSeekKeyBox.Password,
                perplexity: PerplexityKeyBox.Password,
                groq: GroqKeyBox.Password,
                openRouter: OpenRouterKeyBox.Password,
                mistral: MistralKeyBox.Password,
                xAi: XAiKeyBox.Password,
                customBaseUrl: CustomBaseUrlBox.Text,
                customKey: CustomKeyBox.Password
            );

            UpdateAllStatusBadges();

            int count = _apiKeyService.GetConfiguredCount();
            ShowNotification(count > 0
                ? $"Saved! {count} provider{(count > 1 ? "s" : "")} configured."
                : "All keys cleared.");
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            LoadKeysFromStorage();
            ShowNotification("Settings reloaded from storage.");
        }

        private async void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Clear All Keys?",
                Content = "Are you sure you want to remove all saved API keys from this phone?",
                PrimaryButtonText = "Clear",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _apiKeyService.ClearAll();
                LoadKeysFromStorage();
                ShowNotification("All keys have been removed.");
            }
        }

        private void ShowNotification(string message)
        {
            NotificationText.Text = message;
            NotificationBanner.Visibility = Visibility.Visible;

            _notificationTimer.Stop();
            _notificationTimer.Start();
        }

        private async void About_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "WinAI for Windows 10 Mobile",
                Content = "Version 1.0 (Build 15254 Fall Creators Update)\n\nNative Bring-Your-Own-Key (BYOK) AI Chat Client.\nAll credentials remain strictly private and stored offline in local app storage.",
                CloseButtonText = "OK"
            };

            await dialog.ShowAsync();
        }

        #endregion

        #region Clipboard Paste Helpers

        private async Task PasteToPasswordBoxAsync(PasswordBox box)
        {
            try
            {
                var package = Clipboard.GetContent();
                if (package != null && package.Contains(StandardDataFormats.Text))
                {
                    string text = await package.GetTextAsync();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        box.Password = text.Trim();
                        ShowNotification("Key pasted from clipboard.");
                    }
                }
            }
            catch
            {
                // Clipboard access may fail gracefully if empty or unavailable
            }
        }

        private async void PasteOpenAi_Click(object sender, RoutedEventArgs e) => await PasteToPasswordBoxAsync(OpenAiKeyBox);
        private async void PasteGemini_Click(object sender, RoutedEventArgs e) => await PasteToPasswordBoxAsync(GeminiKeyBox);
        private async void PasteClaude_Click(object sender, RoutedEventArgs e) => await PasteToPasswordBoxAsync(ClaudeKeyBox);
        private async void PasteDeepSeek_Click(object sender, RoutedEventArgs e) => await PasteToPasswordBoxAsync(DeepSeekKeyBox);
        private async void PastePerplexity_Click(object sender, RoutedEventArgs e) => await PasteToPasswordBoxAsync(PerplexityKeyBox);
        private async void PasteGroq_Click(object sender, RoutedEventArgs e) => await PasteToPasswordBoxAsync(GroqKeyBox);
        private async void PasteOpenRouter_Click(object sender, RoutedEventArgs e) => await PasteToPasswordBoxAsync(OpenRouterKeyBox);
        private async void PasteMistral_Click(object sender, RoutedEventArgs e) => await PasteToPasswordBoxAsync(MistralKeyBox);
        private async void PasteXAi_Click(object sender, RoutedEventArgs e) => await PasteToPasswordBoxAsync(XAiKeyBox);

        #endregion
    }
}
