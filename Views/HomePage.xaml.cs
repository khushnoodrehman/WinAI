using System;
using System.Collections.Generic;
using System.Linq;
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
using WinAI.Controls;
using WinAI.Models;
using WinAI.Services;
using WinAI.Services.Providers;

namespace WinAI.Views
{
    public sealed partial class HomePage : Page
    {
        private readonly ThemeService _themeService = ThemeService.Instance;
        private readonly AiProviderRegistry _providerRegistry = AiProviderRegistry.Instance;

        private static readonly bool _hasMenuFlyoutIcon = 
            ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Controls.MenuFlyoutItem", "Icon");

        private string _selectedProviderId = "openai";
        private string _selectedModelId = "gpt-4o";
        private string _selectedModelDisplayName = "GPT-4o";

        private string _pendingImageBase64;
        private string _pendingImageMimeType;
        private string _pendingImageFileName;
        private readonly WinAIAttachmentFlyout _attachmentFlyout = new WinAIAttachmentFlyout();

        private DispatcherTimer _voiceTimer;
        private int _voiceSeconds;
        private bool _isRecognizingVoice;
        private readonly VoiceService _voiceService = VoiceService.Instance;

        public HomePage()
        {
            this.InitializeComponent();
            InitializeGreeting();
            LoadDefaultModelFromSettings();

            _attachmentFlyout.ImageAttached += OnHomeAttachmentImageAttached;
            _attachmentFlyout.AttachmentError += OnHomeAttachmentError;
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
            AppSettingsService.Instance.DefaultModelChanged += OnDefaultModelChanged;

            await RefreshRecentConversationsAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            AppSettingsService.Instance.DefaultModelChanged -= OnDefaultModelChanged;

            _voiceTimer?.Stop();
            if (_isRecognizingVoice)
            {
                UnbindVoiceEvents();
                var ignore = _voiceService.CancelRecordingAsync();
                _isRecognizingVoice = false;
            }

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested -= HomePage_BackRequested;
        }

        private void OnDefaultModelChanged(object sender, EventArgs e)
        {
            LoadDefaultModelFromSettings();
            BuildModelPickerFlyout();
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
                var descriptor = AppSettingsService.Instance.GetDefaultModelDescriptor();
                if (descriptor != null)
                {
                    SetSelectedModel(descriptor.ProviderId, descriptor.Id, descriptor.DisplayName);
                }
                else
                {
                    string providerId = AppSettingsService.Instance.DefaultProviderId;
                    string modelId = AppSettingsService.Instance.DefaultModelId;
                    SetSelectedModel(providerId, modelId, modelId);
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

            if (!string.IsNullOrEmpty(_pendingImageBase64))
            {
                var desc = _providerRegistry.GetModel(_selectedProviderId, _selectedModelId);
                if (desc != null && !desc.SupportsVision)
                {
                    ShowHomeVoiceErrorBanner("This model does not support image input.");
                }
            }
        }

        private void UpdateModelIconVisuals(string providerId)
        {
            if (HomeModelProviderIcon == null) return;
            HomeModelProviderIcon.ProviderId = providerId;
            HomeModelProviderIcon.UpdateVisuals();
        }

        private void BuildModelPickerFlyout()
        {
            if (HomeModelPickerFlyout == null) return;

            HomeModelPickerFlyout.Items.Clear();

            var allModels = _providerRegistry.GetAllModels();
            if (allModels == null || allModels.Count == 0) return;

            // Requirement 10: If user's configured model becomes unavailable, do NOT silently switch.
            // Instead, show a clear message: "Your selected model is unavailable."
            bool isCurrentModelAvailable = allModels.Any(m => 
                string.Equals(m.ProviderId, _selectedProviderId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.Id, _selectedModelId, StringComparison.OrdinalIgnoreCase) &&
                m.IsAvailable);

            if (!isCurrentModelAvailable)
            {
                if (HomeModelUnavailableBanner != null)
                {
                    HomeModelUnavailableBanner.Visibility = Visibility.Visible;
                    HomeModelUnavailableText.Text = $"Selected model '{_selectedModelDisplayName}' is unavailable. Please select another model.";
                }
            }
            else
            {
                if (HomeModelUnavailableBanner != null)
                {
                    HomeModelUnavailableBanner.Visibility = Visibility.Collapsed;
                }
            }

            // Requirement 9: Do NOT show unavailable models in Home model selector
            var availableModels = allModels.Where(m => m.IsAvailable).ToList();

            string lastProvider = null;
            bool isFirstSection = true;
            foreach (var m in availableModels)
            {
                if (m.ProviderName != lastProvider)
                {
                    lastProvider = m.ProviderName;
                    if (!isFirstSection)
                    {
                        HomeModelPickerFlyout.Items.Add(new MenuFlyoutSeparator());
                    }
                    isFirstSection = false;

                    var headerItem = new MenuFlyoutItem
                    {
                        Text = m.ProviderName,
                        IsEnabled = false
                    };
                    if (Application.Current.Resources.TryGetValue("ModernMenuFlyoutHeaderStyle", out object headerStyle) && headerStyle is Style hStyle)
                    {
                        headerItem.Style = hStyle;
                    }
                    HomeModelPickerFlyout.Items.Add(headerItem);
                }

                bool isSelected = string.Equals(m.ProviderId, _selectedProviderId, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(m.Id, _selectedModelId, StringComparison.OrdinalIgnoreCase);

                var item = new MenuFlyoutItem
                {
                    Tag = m
                };
                if (Application.Current.Resources.TryGetValue("ModernMenuFlyoutItemStyle", out object itemStyle) && itemStyle is Style iStyle)
                {
                    item.Style = iStyle;
                }

                var accentBrush = Application.Current.Resources["AppAccentBrush"] as SolidColorBrush;
                if (_hasMenuFlyoutIcon)
                {
                    item.Text = m.DisplayName;
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
                    item.Text = isSelected ? $"\uE73E  {m.DisplayName}" : $"    {m.DisplayName}";
                    if (isSelected && accentBrush != null)
                    {
                        item.Foreground = accentBrush;
                    }
                }

                item.Click += (s, args) =>
                {
                    if (s is MenuFlyoutItem clicked && clicked.Tag is AiModelDescriptor desc)
                    {
                        SetSelectedModel(desc.ProviderId, desc.Id, desc.DisplayName);
                        AppSettingsService.Instance.SetDefaultModel(desc.ProviderId, desc.Id, desc.DisplayName);
                        if (HomeModelUnavailableBanner != null)
                        {
                            HomeModelUnavailableBanner.Visibility = Visibility.Collapsed;
                        }
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
            string prompt = HomePromptTextBox.Text?.Trim() ?? string.Empty;
            bool hasImage = !string.IsNullOrEmpty(_pendingImageBase64);

            if (string.IsNullOrWhiteSpace(prompt) && !hasImage) return;

            var descriptor = _providerRegistry.GetModel(_selectedProviderId, _selectedModelId);
            if (hasImage && descriptor != null && !descriptor.SupportsVision)
            {
                ShowHomeVoiceErrorBanner("This model does not support image input.");
                return;
            }

            string sendingImageBase64 = _pendingImageBase64;
            string sendingImageMime = _pendingImageMimeType;
            string sendingImageFileName = _pendingImageFileName;

            HomePromptTextBox.Text = string.Empty;
            ClearHomePendingAttachment();

            // Direct transition into Chat screen with prompt, active model, and attachment
            Frame.Navigate(typeof(MainPage), new ChatLaunchArgs(prompt, _selectedProviderId, _selectedModelId, sendingImageBase64, sendingImageMime, sendingImageFileName));
        }

        #endregion

        #region Attachments

        private void HomeAttachButton_Click(object sender, RoutedEventArgs e)
        {
            var descriptor = _providerRegistry.GetModel(_selectedProviderId, _selectedModelId);
            bool supportsVision = descriptor?.SupportsVision ?? true;
            _attachmentFlyout.ShowAt((FrameworkElement)sender, supportsVision);
        }

        private void OnHomeAttachmentImageAttached(object sender, AttachmentResult result)
        {
            if (result == null) return;

            _pendingImageBase64 = result.Base64Data;
            _pendingImageMimeType = result.MimeType;
            _pendingImageFileName = result.FileName;
            if (HomeAttachmentThumbnailImage != null) HomeAttachmentThumbnailImage.Source = result.Thumbnail;
            if (HomeAttachmentFileNameText != null) HomeAttachmentFileNameText.Text = result.FileName;
            if (HomeAttachmentPreviewBar != null) HomeAttachmentPreviewBar.Visibility = Visibility.Visible;
        }

        private void OnHomeAttachmentError(object sender, string errorMessage)
        {
            ShowHomeVoiceErrorBanner(errorMessage);
        }

        private void HomeRemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            ClearHomePendingAttachment();
        }

        private void ClearHomePendingAttachment()
        {
            _pendingImageBase64 = null;
            _pendingImageMimeType = null;
            _pendingImageFileName = null;
            if (HomeAttachmentThumbnailImage != null) HomeAttachmentThumbnailImage.Source = null;
            if (HomeAttachmentPreviewBar != null) HomeAttachmentPreviewBar.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Voice Recording & Transcription

        private DispatcherTimer _voiceBannerTimer;

        private void ShowHomeVoiceErrorBanner(string message)
        {
            if (HomeVoiceStatusBanner == null || HomeVoiceStatusBannerText == null) return;
            HomeVoiceStatusBanner.Background = (Brush)Application.Current.Resources["WinAIErrorBrush"];
            HomeVoiceStatusBannerIcon.Text = "\uE783";
            HomeVoiceStatusBannerText.Text = message;
            HomeVoiceStatusBanner.Visibility = Visibility.Visible;

            if (_voiceBannerTimer == null)
            {
                _voiceBannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                _voiceBannerTimer.Tick += (s, e) =>
                {
                    _voiceBannerTimer.Stop();
                    if (HomeVoiceStatusBanner != null)
                    {
                        HomeVoiceStatusBanner.Visibility = Visibility.Collapsed;
                    }
                };
            }

            _voiceBannerTimer.Stop();
            _voiceBannerTimer.Start();
        }

        private void BindVoiceEvents()
        {
            _voiceService.HypothesisReceived -= OnVoiceHypothesisReceived;
            _voiceService.StatusChanged -= OnVoiceStatusChanged;
            _voiceService.SessionCompleted -= OnVoiceSessionCompleted;

            _voiceService.HypothesisReceived += OnVoiceHypothesisReceived;
            _voiceService.StatusChanged += OnVoiceStatusChanged;
            _voiceService.SessionCompleted += OnVoiceSessionCompleted;
        }

        private void UnbindVoiceEvents()
        {
            _voiceService.HypothesisReceived -= OnVoiceHypothesisReceived;
            _voiceService.StatusChanged -= OnVoiceStatusChanged;
            _voiceService.SessionCompleted -= OnVoiceSessionCompleted;
        }

        private async void OnVoiceHypothesisReceived(object sender, string combinedHypothesis)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (!_isRecognizingVoice) return;
                if (HomeVoiceHypothesisText != null)
                {
                    if (string.IsNullOrWhiteSpace(combinedHypothesis))
                    {
                        HomeVoiceHypothesisText.Text = "Speak now...";
                        HomeVoiceHypothesisText.Foreground = (Brush)Application.Current.Resources["AppTextSecondaryBrush"];
                    }
                    else
                    {
                        HomeVoiceHypothesisText.Text = $"\"{combinedHypothesis}\"";
                        HomeVoiceHypothesisText.Foreground = (Brush)Application.Current.Resources["AppTextPrimaryBrush"];
                    }
                }
            });
        }

        private async void OnVoiceStatusChanged(object sender, string status)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (!_isRecognizingVoice) return;
                if (HomeVoiceRecordingStatusText != null && !string.IsNullOrWhiteSpace(status))
                {
                    HomeVoiceRecordingStatusText.Text = status;
                }
            });
        }

        private async void OnVoiceSessionCompleted(object sender, VoiceRecognitionResult result)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (!_isRecognizingVoice) return;
                FinishVoiceRecognition(result);
            });
        }

        private async void HomeVoiceRecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecognizingVoice) return;
            _isRecognizingVoice = true;

            HomeNormalInputGrid.Visibility = Visibility.Collapsed;
            HomeVoiceRecordingGrid.Visibility = Visibility.Visible;
            _voiceSeconds = 0;
            HomeVoiceRecordingTimerText.Text = "0:00";
            HomeVoiceRecordingStatusText.Text = "Listening...";
            HomeVoiceHypothesisText.Text = "Speak now...";
            HomeVoiceHypothesisText.Foreground = (Brush)Application.Current.Resources["AppTextSecondaryBrush"];
            HomeDoneVoiceRecordingButton.IsEnabled = true;

            if (_voiceTimer == null)
            {
                _voiceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _voiceTimer.Tick += (s, args) =>
                {
                    _voiceSeconds++;
                    int totalSeconds = _voiceSeconds / 2;
                    HomeVoiceRecordingTimerText.Text = $"{totalSeconds / 60}:{(totalSeconds % 60):D2}";

                    if (HomeVoicePulseDot != null)
                    {
                        HomeVoicePulseDot.Opacity = (_voiceSeconds % 2 == 0) ? 0.40 : 0.15;
                    }
                    if (HomeVoiceWave1 != null && HomeVoiceWave2 != null && HomeVoiceWave3 != null && HomeVoiceWave4 != null)
                    {
                        int step = _voiceSeconds % 4;
                        HomeVoiceWave1.Height = step == 0 ? 12 : (step == 1 ? 6 : 9);
                        HomeVoiceWave2.Height = step == 1 ? 16 : (step == 2 ? 8 : 13);
                        HomeVoiceWave3.Height = step == 2 ? 14 : (step == 3 ? 7 : 10);
                        HomeVoiceWave4.Height = step == 3 ? 10 : (step == 0 ? 5 : 8);
                    }
                };
            }
            _voiceTimer.Start();

            BindVoiceEvents();
            var startResult = await _voiceService.StartRecordingAsync();
            if (!startResult.Success)
            {
                _voiceTimer?.Stop();
                UnbindVoiceEvents();
                HomeVoiceRecordingGrid.Visibility = Visibility.Collapsed;
                HomeNormalInputGrid.Visibility = Visibility.Visible;
                _isRecognizingVoice = false;

                ShowHomeVoiceErrorBanner(startResult.ErrorMessage);
            }
        }

        private async void HomeCancelVoiceRecordingButton_Click(object sender, RoutedEventArgs e)
        {
            _voiceTimer?.Stop();
            UnbindVoiceEvents();
            await _voiceService.CancelRecordingAsync();

            HomeVoiceRecordingGrid.Visibility = Visibility.Collapsed;
            HomeNormalInputGrid.Visibility = Visibility.Visible;
            _isRecognizingVoice = false;

            HomePromptTextBox.Focus(FocusState.Programmatic);
        }

        private async void HomeDoneVoiceRecordingButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRecognizingVoice) return;

            _voiceTimer?.Stop();
            HomeVoiceRecordingStatusText.Text = "Transcribing...";
            HomeDoneVoiceRecordingButton.IsEnabled = false;

            var result = await _voiceService.StopRecordingAndTranscribeAsync();
            FinishVoiceRecognition(result);
        }

        private void FinishVoiceRecognition(VoiceRecognitionResult result)
        {
            _voiceTimer?.Stop();
            UnbindVoiceEvents();

            HomeVoiceRecordingGrid.Visibility = Visibility.Collapsed;
            HomeNormalInputGrid.Visibility = Visibility.Visible;
            _isRecognizingVoice = false;
            HomeDoneVoiceRecordingButton.IsEnabled = true;

            if (result != null && result.Success && !string.IsNullOrWhiteSpace(result.Text))
            {
                string textToInsert = result.Text.Trim();
                if (string.IsNullOrWhiteSpace(HomePromptTextBox.Text))
                {
                    HomePromptTextBox.Text = textToInsert;
                }
                else
                {
                    HomePromptTextBox.Text = (HomePromptTextBox.Text.Trim() + " " + textToInsert).Trim();
                }

                HomePromptTextBox.SelectionStart = HomePromptTextBox.Text.Length;
                HomePromptTextBox.Focus(FocusState.Programmatic);
            }
            else if (result != null && !result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                ShowHomeVoiceErrorBanner(result.ErrorMessage);
                HomePromptTextBox.Focus(FocusState.Programmatic);
            }
            else
            {
                ShowHomeVoiceErrorBanner("Could not understand the recording.");
                HomePromptTextBox.Focus(FocusState.Programmatic);
            }
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
            Frame.Navigate(typeof(AiProvidersPage));
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
