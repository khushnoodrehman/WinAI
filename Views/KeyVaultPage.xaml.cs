using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using WinAI.Controls;
using WinAI.Services;

namespace WinAI.Views
{
    public sealed partial class KeyVaultPage : Page
    {
        private readonly CredentialVaultService _vaultService = CredentialVaultService.Instance;
        private readonly ApiKeyService _apiKeyService = ApiKeyService.Instance;
        private DispatcherTimer _notificationTimer;

        public KeyVaultPage()
        {
            this.InitializeComponent();
            SetupNotificationTimer();
            HookProviderEvents();
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

        private void HookProviderEvents()
        {
            VaultProviderItem[] items = { OpenAiItem, GeminiItem, ClaudeItem, OpenRouterItem, PerplexityItem, DeepSeekItem, XAiItem };
            foreach (var item in items)
            {
                item.CopyRequested += ProviderItem_CopyRequested;
                item.DeleteRequested += ProviderItem_DeleteRequested;
                item.EditRequested += ProviderItem_EditRequested;
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            RefreshAllProviders();
            UpdateLockVaultStatus();

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

        private void RefreshAllProviders()
        {
            UpdateProviderRow(OpenAiItem, "openai");
            UpdateProviderRow(GeminiItem, "gemini");
            UpdateProviderRow(ClaudeItem, "claude");
            UpdateProviderRow(OpenRouterItem, "openrouter");
            UpdateProviderRow(PerplexityItem, "perplexity");
            UpdateProviderRow(DeepSeekItem, "deepseek");
            UpdateProviderRow(XAiItem, "xai");
        }

        private void UpdateProviderRow(VaultProviderItem item, string providerId)
        {
            bool hasKey = _vaultService.HasApiKey(providerId);
            item.IsConfigured = hasKey;
            item.UpdateUi();
        }

        private void UpdateLockVaultStatus()
        {
            if (_vaultService.HasPin)
            {
                LockVaultSubtitle.Text = _vaultService.IsVaultLocked()
                    ? "Vault is locked with PIN (Tap to unlock)"
                    : "Vault is PIN-protected (Tap to manage)";
            }
            else
            {
                LockVaultSubtitle.Text = "Set a PIN to protect your keys";
            }
        }

        #region Provider Item Actions

        private async void ProviderItem_CopyRequested(object sender, VaultProviderItem item)
        {
            if (item == null || !item.IsConfigured)
            {
                ShowNotification("No API key configured to copy.");
                return;
            }

            // Phase 3 Requirement 3: Check whether Vault PIN protection is enabled
            if (_vaultService.HasPin)
            {
                // Three dots → Copy API Key → PIN dialog → validate PIN → only then retrieve credential → copy to Clipboard
                bool pinValid = await RequestPinValidationForCopyAsync();
                if (!pinValid)
                {
                    return;
                }
            }

            // Retrieve credential transiently ONLY when needed
            string transientKey = _vaultService.GetApiKey(item.ProviderId);
            try
            {
                if (!string.IsNullOrEmpty(transientKey))
                {
                    var dataPackage = new DataPackage();
                    dataPackage.RequestedOperation = DataPackageOperation.Copy;
                    dataPackage.SetText(transientKey);
                    Clipboard.SetContent(dataPackage);

                    // Phase 3 Requirement 4: subtle confirmation: "API key copied."
                    // Never display the actual key in a dialog or toast.
                    ShowNotification("API key copied.");
                }
                else
                {
                    ShowNotification("Could not retrieve key.");
                }
            }
            finally
            {
                // Clear sensitive values from transient variables as soon as practical
                transientKey = null;
            }
        }

        private async Task<bool> RequestPinValidationForCopyAsync()
        {
            var dialog = new ContentDialog
            {
                Title = "Key Vault Security",
                PrimaryButtonText = "Confirm",
                SecondaryButtonText = "Cancel"
            };

            var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var infoText = new TextBlock
            {
                Text = "Enter your vault PIN to copy the API key to clipboard:",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stack.Children.Add(infoText);

            var pinBox = new PasswordBox
            {
                Header = "Vault PIN",
                PlaceholderText = "Enter PIN",
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(pinBox);

            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                string entered = pinBox.Password;
                pinBox.Password = string.Empty; // Clear promptly
                if (!string.IsNullOrEmpty(entered) && _vaultService.VerifyPin(entered))
                {
                    return true;
                }
                else
                {
                    ShowNotification("Incorrect PIN. API key was not copied.");
                    return false;
                }
            }

            return false;
        }

        private async void ProviderItem_EditRequested(object sender, VaultProviderItem item)
        {
            if (_vaultService.IsVaultLocked())
            {
                bool unlocked = await RequestPinUnlockAsync();
                if (!unlocked) return;
            }

            await ShowAddKeyDialogAsync(item.ProviderId);
        }

        private async void ProviderItem_DeleteRequested(object sender, VaultProviderItem item)
        {
            if (_vaultService.IsVaultLocked())
            {
                bool unlocked = await RequestPinUnlockAsync();
                if (!unlocked) return;
            }

            var dialog = new ContentDialog
            {
                Title = $"Remove {item.ProviderName} Key?",
                Content = $"Are you sure you want to remove the {item.ProviderName} API key from the local Key Vault?",
                PrimaryButtonText = "Remove",
                SecondaryButtonText = "Cancel"
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _vaultService.DeleteApiKey(item.ProviderId);
                RefreshAllProviders();
                ShowNotification($"{item.ProviderName} API key removed from Vault.");
            }
        }

        #endregion

        #region Add / Edit API Key Dialog

        private async void AddKeyButton_Click(object sender, RoutedEventArgs e)
        {
            await ShowAddKeyDialogAsync(null);
        }

        private async Task ShowAddKeyDialogAsync(string preselectedProviderId)
        {
            if (_vaultService.IsVaultLocked())
            {
                bool unlocked = await RequestPinUnlockAsync();
                if (!unlocked) return;
            }

            var dialog = new ContentDialog
            {
                Title = string.IsNullOrEmpty(preselectedProviderId) ? "Add API Key" : $"Edit {GetDisplayName(preselectedProviderId)} Key",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Cancel"
            };

            var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

            var providerCombo = new ComboBox
            {
                Header = "AI Provider",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 12)
            };

            var providers = new[]
            {
                new { Id = "openai", Name = "OpenAI" },
                new { Id = "gemini", Name = "Google Gemini" },
                new { Id = "claude", Name = "Anthropic Claude" },
                new { Id = "openrouter", Name = "OpenRouter (Free & Paid Models)" },
                new { Id = "deepseek", Name = "DeepSeek" },
                new { Id = "perplexity", Name = "Perplexity AI" },
                new { Id = "xai", Name = "xAI" }
            };

            foreach (var p in providers)
            {
                providerCombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
            }

            int selectedIndex = 0;
            if (!string.IsNullOrEmpty(preselectedProviderId))
            {
                for (int i = 0; i < providers.Length; i++)
                {
                    if (string.Equals(providers[i].Id, preselectedProviderId, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }
            providerCombo.SelectedIndex = selectedIndex;
            stack.Children.Add(providerCombo);

            string targetId = preselectedProviderId ?? providers[selectedIndex].Id;
            bool targetHasKey = _vaultService.HasApiKey(targetId);
            var keyBox = new PasswordBox
            {
                Header = "API Key",
                PlaceholderText = targetHasKey ? "Key configured (enter new key to replace)" : "Paste your API key here",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var testBtn = new Button
            {
                Content = "Test Key & Fetch Models",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 4, 0, 8)
            };

            var statusTextBlock = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };

            testBtn.Click += async (s, e) =>
            {
                if (providerCombo.SelectedItem is ComboBoxItem cbiSel && cbiSel.Tag is string testPid)
                {
                    string k = keyBox.Password?.Trim();
                    if (string.IsNullOrEmpty(k) && _vaultService.HasApiKey(testPid))
                    {
                        k = _vaultService.GetApiKey(testPid);
                    }

                    if (string.IsNullOrEmpty(k))
                    {
                        statusTextBlock.Text = "Please paste or enter an API key first.";
                        statusTextBlock.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                        statusTextBlock.Visibility = Visibility.Visible;
                        return;
                    }

                    testBtn.IsEnabled = false;
                    statusTextBlock.Text = "Connecting and fetching models...";
                    statusTextBlock.Foreground = Application.Current.Resources["AppTextSecondaryBrush"] as Windows.UI.Xaml.Media.Brush;
                    statusTextBlock.Visibility = Visibility.Visible;

                    var testRes = await ModelService.Instance.TestAndFetchModelsForProviderAsync(testPid, k);
                    testBtn.IsEnabled = true;

                    if (testRes.Success)
                    {
                        statusTextBlock.Text = $"✓ {testRes.Message}";
                        statusTextBlock.Foreground = new SolidColorBrush(Windows.UI.Colors.LightGreen);
                        if (!string.IsNullOrEmpty(keyBox.Password?.Trim()))
                        {
                            _vaultService.SaveApiKey(testPid, keyBox.Password.Trim());
                            RefreshAllProviders();
                        }
                    }
                    else
                    {
                        statusTextBlock.Text = $"✕ {testRes.Message}";
                        statusTextBlock.Foreground = new SolidColorBrush(Windows.UI.Colors.OrangeRed);
                    }
                }
            };

            providerCombo.SelectionChanged += (s, e) =>
            {
                if (providerCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string pid)
                {
                    statusTextBlock.Visibility = Visibility.Collapsed;
                    keyBox.Password = string.Empty;
                    keyBox.PlaceholderText = _vaultService.HasApiKey(pid) 
                        ? "Key configured (enter new key to replace)" 
                        : "Paste your API key here";
                }
            };

            stack.Children.Add(keyBox);
            stack.Children.Add(testBtn);
            stack.Children.Add(statusTextBlock);

            var note = new TextBlock
            {
                Text = "Stored securely in Windows Credential Locker. Models are cached locally on this device.",
                FontSize = 12,
                Foreground = Application.Current.Resources["AppTextSecondaryBrush"] as Windows.UI.Xaml.Media.Brush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(note);

            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (providerCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string pid)
                {
                    string key = keyBox.Password?.Trim();
                    if (!string.IsNullOrEmpty(key))
                    {
                        _vaultService.SaveApiKey(pid, key);
                        // Trigger background fetch to store models locally on device
                        var _ = ModelService.Instance.TestAndFetchModelsForProviderAsync(pid, key);
                        RefreshAllProviders();
                        ShowNotification($"{GetDisplayName(pid)} key saved & models synced!");
                    }
                    else if (!_vaultService.HasApiKey(pid))
                    {
                        ShowNotification("No key was entered.");
                    }
                }
            }
        }

        private string GetDisplayName(string providerId)
        {
            switch (providerId?.ToLowerInvariant())
            {
                case "openai": return "OpenAI";
                case "gemini": return "Gemini";
                case "claude": return "Claude";
                case "openrouter": return "OpenRouter";
                case "perplexity": return "Perplexity";
                case "deepseek": return "DeepSeek";
                case "xai": return "xAI";
                default: return providerId ?? "AI Provider";
            }
        }

        #endregion

        #region Vault Lock & PIN Handling

        private void LockVaultCard_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            HandleLockVaultInteraction();
        }

        private void LockVaultCard_Click(object sender, RoutedEventArgs e)
        {
            HandleLockVaultInteraction();
        }

        private async void HandleLockVaultInteraction()
        {
            if (!_vaultService.HasPin)
            {
                await ShowSetPinDialogAsync();
            }
            else
            {
                await ShowManagePinDialogAsync();
            }
        }

        private async Task ShowSetPinDialogAsync()
        {
            var dialog = new ContentDialog
            {
                Title = "Set Vault PIN",
                PrimaryButtonText = "Save PIN",
                SecondaryButtonText = "Cancel"
            };

            var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

            var infoText = new TextBlock
            {
                Text = "Create a PIN (4-8 digits) to protect revealing or modifying your API keys.",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stack.Children.Add(infoText);

            var pinBox = new PasswordBox
            {
                Header = "New PIN",
                PlaceholderText = "Enter PIN",
                Margin = new Thickness(0, 0, 0, 10)
            };
            stack.Children.Add(pinBox);

            var confirmPinBox = new PasswordBox
            {
                Header = "Confirm PIN",
                PlaceholderText = "Re-enter PIN",
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(confirmPinBox);

            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                string pin = pinBox.Password;
                string confirm = confirmPinBox.Password;

                if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4)
                {
                    ShowNotification("PIN must be at least 4 digits.");
                    return;
                }

                if (pin != confirm)
                {
                    ShowNotification("PIN confirmation did not match.");
                    return;
                }

                _vaultService.SetPin(pin);
                UpdateLockVaultStatus();
                ShowNotification("Key Vault PIN has been set successfully.");
            }
        }

        private async Task ShowManagePinDialogAsync()
        {
            var dialog = new ContentDialog
            {
                Title = "Vault Security",
                PrimaryButtonText = _vaultService.IsVaultLocked() ? "Unlock" : "Lock Now",
                SecondaryButtonText = "Remove PIN"
            };

            var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

            var infoText = new TextBlock
            {
                Text = "Enter your vault PIN to change security settings or unlock:",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stack.Children.Add(infoText);

            var pinBox = new PasswordBox
            {
                Header = "Current PIN",
                PlaceholderText = "Enter current PIN",
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(pinBox);

            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (_vaultService.IsVaultLocked())
                {
                    if (_vaultService.VerifyPin(pinBox.Password))
                    {
                        _vaultService.UnlockVault(pinBox.Password);
                        UpdateLockVaultStatus();
                        ShowNotification("Vault unlocked.");
                    }
                    else
                    {
                        ShowNotification("Incorrect PIN.");
                    }
                }
                else
                {
                    _vaultService.LockVault();
                    UpdateLockVaultStatus();
                    RefreshAllProviders();
                    ShowNotification("Vault locked.");
                }
            }
            else if (result == ContentDialogResult.Secondary)
            {
                if (_vaultService.VerifyPin(pinBox.Password))
                {
                    _vaultService.RemovePin();
                    UpdateLockVaultStatus();
                    ShowNotification("Vault PIN removed.");
                }
                else
                {
                    ShowNotification("Incorrect PIN. Cannot remove.");
                }
            }
        }

        private async Task<bool> RequestPinUnlockAsync()
        {
            if (!_vaultService.IsVaultLocked()) return true;

            var dialog = new ContentDialog
            {
                Title = "Vault Locked",
                PrimaryButtonText = "Unlock",
                SecondaryButtonText = "Cancel"
            };

            var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var infoText = new TextBlock
            {
                Text = "Enter your PIN to access sensitive vault actions:",
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stack.Children.Add(infoText);

            var pinBox = new PasswordBox
            {
                Header = "Vault PIN",
                PlaceholderText = "Enter PIN",
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(pinBox);

            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (_vaultService.UnlockVault(pinBox.Password))
                {
                    UpdateLockVaultStatus();
                    return true;
                }
                else
                {
                    ShowNotification("Incorrect PIN.");
                    return false;
                }
            }

            return false;
        }

        #endregion

        #region Manage Header Action

        private async void ManageButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Manage Vault",
                PrimaryButtonText = "Clear All Keys",
                SecondaryButtonText = "Cancel"
            };

            dialog.Content = new TextBlock
            {
                Text = "Are you sure you want to remove all configured provider credentials stored on this phone?",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (_vaultService.IsVaultLocked())
                {
                    bool unlocked = await RequestPinUnlockAsync();
                    if (!unlocked) return;
                }

                _vaultService.ClearAllKeys();
                RefreshAllProviders();
                ShowNotification("All API keys removed from Vault.");
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await ShowAddKeyDialogAsync(null);
            }
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
        private void DrawerYourImages_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ImagesGalleryPage));
        private void DrawerProviders_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(AiProvidersPage));
        private void DrawerVault_Click(object sender, RoutedEventArgs e) => NavDrawer.IsPaneOpen = false;
        private void DrawerPromptKit_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(PromptKitPage));
        private void DrawerSettings_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SettingsPage));
        private void DrawerPrivacy_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(PrivacyCenterPage));
        private void DrawerAbout_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(AboutPage));

        // Bottom Navigation Bar Handlers
        private void BottomHome_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(HomePage));
        private void BottomChats_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ConversationsPage));
        private void BottomVault_Click(object sender, RoutedEventArgs e) => NavDrawer.IsPaneOpen = false;
        private void BottomMore_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SettingsPage));

        #endregion
    }
}
