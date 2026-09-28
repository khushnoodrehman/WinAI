using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using WinAI.Models;
using WinAI.Services;
using WinAI.Views;

namespace WinAI
{
    public sealed partial class MainPage : Page
    {
        private readonly ApiKeyService _apiKeyService = ApiKeyService.Instance;
        private readonly ChatHistoryService _historyService = ChatHistoryService.Instance;
        private readonly ModelService _modelService = ModelService.Instance;
        private readonly VoiceService _voiceService = VoiceService.Instance;

        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();

        private ChatSession _currentSession;
        private AiModelItem _selectedModel;
        private bool _isRecognizingVoice;

        private string _pendingImageBase64;
        private string _pendingImageMimeType;
        private string _pendingImageFileName;

        public MainPage()
        {
            this.InitializeComponent();

            Messages.CollectionChanged += Messages_CollectionChanged;
            _apiKeyService.KeysChanged += ApiKeyService_KeysChanged;
            _historyService.SessionsUpdated += HistoryService_SessionsUpdated;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested += MainPage_BackRequested;
            navManager.AppViewBackButtonVisibility = Frame.CanGoBack
                ? AppViewBackButtonVisibility.Visible
                : AppViewBackButtonVisibility.Collapsed;

            await EvaluateViewStateAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested -= MainPage_BackRequested;
        }

        private void MainPage_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (ChatSplitView != null && ChatSplitView.IsPaneOpen)
            {
                e.Handled = true;
                ChatSplitView.IsPaneOpen = false;
                return;
            }

            if (Frame.CanGoBack)
            {
                e.Handled = true;
                Frame.GoBack();
            }
        }

        private async void ApiKeyService_KeysChanged(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                await EvaluateViewStateAsync();
            });
        }

        private async void HistoryService_SessionsUpdated(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                await LoadRecentSessionsAsync();
            });
        }

        #region View State & Model Initialization

        private async Task EvaluateViewStateAsync()
        {
            bool hasKeys = _apiKeyService.HasAnyKey();

            if (!hasKeys)
            {
                SetupContainer.Visibility = Visibility.Visible;
                ChatSplitView.Visibility = Visibility.Collapsed;
            }
            else
            {
                SetupContainer.Visibility = Visibility.Collapsed;
                ChatSplitView.Visibility = Visibility.Visible;

                if (_currentSession == null)
                {
                    await InitializeCurrentSessionAsync();
                }

                await LoadModelsAsync();
                await LoadRecentSessionsAsync();
                UpdateEmptyState();
            }
        }

        private async Task LoadModelsAsync(bool forceRefresh = false)
        {
            try
            {
                CurrentModelBadgeText.Text = "Loading models...";
                var models = await _modelService.GetAvailableModelsAsync(forceRefresh);

                DynamicModelComboBox.ItemsSource = models;

                if (models.Count > 0)
                {
                    // Select first or match current session model
                    var match = models.FirstOrDefault(m => m.Id == _currentSession?.SelectedModelId) ?? models[0];
                    DynamicModelComboBox.SelectedItem = match;
                    _selectedModel = match;
                    CurrentModelBadgeText.Text = match.FullDisplay;
                }
                else
                {
                    CurrentModelBadgeText.Text = "No models available";
                }
            }
            catch
            {
                CurrentModelBadgeText.Text = "Standard AI (Default)";
            }
        }

        private void DynamicModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DynamicModelComboBox.SelectedItem is AiModelItem item)
            {
                _selectedModel = item;
                CurrentModelBadgeText.Text = item.FullDisplay;
                if (_currentSession != null)
                {
                    _currentSession.SelectedModelId = item.Id;
                }
            }
        }

        private async void RefreshModelsButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadModelsAsync(forceRefresh: true);
        }

        #endregion

        #region Session Management & Drawer

        private async Task InitializeCurrentSessionAsync()
        {
            var sessions = await _historyService.GetSessionsAsync();
            if (sessions.Count > 0)
            {
                LoadSession(sessions[0]);
            }
            else
            {
                _currentSession = await _historyService.CreateNewSessionAsync();
                LoadSession(_currentSession);
            }
        }

        private void LoadSession(ChatSession session)
        {
            _currentSession = session;
            Messages.Clear();

            if (session.Messages != null)
            {
                foreach (var msg in session.Messages)
                {
                    Messages.Add(msg);
                }
            }

            CurrentChatTitleText.Text = string.IsNullOrWhiteSpace(session.Title) ? "New Chat" : session.Title;
            UpdateEmptyState();
            ScrollToLatestMessage();
        }

        private async Task LoadRecentSessionsAsync()
        {
            var sessions = await _historyService.GetSessionsAsync();
            RecentSessionsListView.ItemsSource = sessions;
        }

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            ChatSplitView.IsPaneOpen = !ChatSplitView.IsPaneOpen;
        }

        private void CloseDrawer_Click(object sender, RoutedEventArgs e)
        {
            ChatSplitView.IsPaneOpen = false;
        }

        private async void NewChat_Click(object sender, RoutedEventArgs e)
        {
            await CreateNewChatAsync();
        }

        private async Task CreateNewChatAsync()
        {
            ChatSplitView.IsPaneOpen = false;
            _currentSession = await _historyService.CreateNewSessionAsync(_selectedModel?.Id);
            LoadSession(_currentSession);
        }

        private void RecentSession_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ChatSession session)
            {
                ChatSplitView.IsPaneOpen = false;
                LoadSession(session);
            }
        }

        private async void DeleteSession_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sessionId)
            {
                await _historyService.DeleteSessionAsync(sessionId);

                if (_currentSession != null && _currentSession.Id == sessionId)
                {
                    var sessions = await _historyService.GetSessionsAsync();
                    if (sessions.Count > 0)
                    {
                        LoadSession(sessions[0]);
                    }
                    else
                    {
                        await CreateNewChatAsync();
                    }
                }
            }
        }

        private async void ClearAllHistory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Clear All Chats?",
                Content = "Are you sure you want to delete all saved conversations from this device?",
                PrimaryButtonText = "Clear All",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await _historyService.ClearAllSessionsAsync();
                await CreateNewChatAsync();
            }
        }

        #endregion

        #region Messaging & Auto-Scroll

        private void Messages_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateEmptyState();

            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null && e.NewItems.Count > 0)
            {
                var newMsg = e.NewItems[0] as ChatMessage;
                var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    ScrollToLatestMessage(newMsg);
                });
            }
        }

        private void ScrollToLatestMessage(ChatMessage message = null)
        {
            try
            {
                if (ChatListView != null && Messages.Count > 0)
                {
                    ChatListView.UpdateLayout();
                    var target = message ?? Messages[Messages.Count - 1];
                    ChatListView.ScrollIntoView(target);
                }
            }
            catch
            {
                // Fallback during layout virtualization passes
            }
        }

        private void UpdateEmptyState()
        {
            if (EmptyStatePanel == null) return;
            EmptyStatePanel.Visibility = Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateSendButtonState()
        {
            if (SendButton != null)
            {
                bool hasText = !string.IsNullOrWhiteSpace(InputTextBox?.Text);
                bool hasImage = !string.IsNullOrEmpty(_pendingImageBase64);
                SendButton.IsEnabled = hasText || hasImage;
            }
        }

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSendButtonState();
        }

        private void InputTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                var shiftState = CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Shift);
                bool isShiftDown = (shiftState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

                if (!isShiftDown && (!string.IsNullOrWhiteSpace(InputTextBox.Text) || !string.IsNullOrEmpty(_pendingImageBase64)))
                {
                    e.Handled = true;
                    SendMessage();
                }
            }
        }

        private readonly AiChatService _chatService = AiChatService.Instance;

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        #region Vision / Image Attachment

        private async void AttachImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.Thumbnail,
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary
                };
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".bmp");
                picker.FileTypeFilter.Add(".webp");

                StorageFile file = await picker.PickSingleFileAsync();
                if (file == null) return;

                // Determine MIME type
                string ext = file.FileType.ToLowerInvariant();
                string mimeType = "image/jpeg";
                if (ext == ".png") mimeType = "image/png";
                else if (ext == ".webp") mimeType = "image/webp";
                else if (ext == ".bmp") mimeType = "image/bmp";

                // Read bytes into Base64
                using (var stream = await file.OpenReadAsync())
                {
                    var bytes = new byte[stream.Size];
                    using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                    {
                        await reader.LoadAsync((uint)stream.Size);
                        reader.ReadBytes(bytes);
                    }

                    _pendingImageBase64 = Convert.ToBase64String(bytes);
                    _pendingImageMimeType = mimeType;
                    _pendingImageFileName = file.Name;

                    // Load thumbnail into preview bar
                    var bmp = new BitmapImage();
                    stream.Seek(0);
                    await bmp.SetSourceAsync(stream);
                    AttachmentThumbnailImage.Source = bmp;
                    AttachmentFileNameText.Text = file.Name;
                    AttachmentPreviewBar.Visibility = Visibility.Visible;

                    UpdateSendButtonState();
                }
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "Attachment Error",
                    Content = $"Could not open image: {ex.Message}",
                    CloseButtonText = "OK"
                };
                await dialog.ShowAsync();
            }
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            ClearPendingAttachment();
        }

        private void ClearPendingAttachment()
        {
            _pendingImageBase64 = null;
            _pendingImageMimeType = null;
            _pendingImageFileName = null;
            if (AttachmentThumbnailImage != null) AttachmentThumbnailImage.Source = null;
            if (AttachmentPreviewBar != null) AttachmentPreviewBar.Visibility = Visibility.Collapsed;
            UpdateSendButtonState();
        }

        #endregion

        private async void SendMessage()
        {
            string text = InputTextBox.Text?.Trim() ?? string.Empty;
            bool hasImage = !string.IsNullOrEmpty(_pendingImageBase64);

            if (string.IsNullOrWhiteSpace(text) && !hasImage) return;

            // 1. Validate internet connectivity
            if (!_chatService.HasInternetConnection())
            {
                var netDialog = new ContentDialog
                {
                    Title = "No Internet Connection",
                    Content = "Please check your Wi-Fi or cellular network connection and try again.",
                    CloseButtonText = "OK"
                };
                await netDialog.ShowAsync();
                return;
            }

            // 2. Validate selected model
            if (_selectedModel == null)
            {
                var modelDialog = new ContentDialog
                {
                    Title = "No Model Selected",
                    Content = "Please select an AI model from the dropdown above the input box.",
                    CloseButtonText = "OK"
                };
                await modelDialog.ShowAsync();
                return;
            }

            // 3. Ensure current session
            if (_currentSession == null)
            {
                _currentSession = await _historyService.CreateNewSessionAsync(_selectedModel.Id);
            }

            // Update title from first prompt if "New Chat"
            if (_currentSession.Title == "New Chat" && Messages.Count == 0)
            {
                string titlePrompt = !string.IsNullOrWhiteSpace(text) ? text : $"Image: {_pendingImageFileName ?? "Attachment"}";
                string newTitle = titlePrompt.Length > 28 ? titlePrompt.Substring(0, 25) + "..." : titlePrompt;
                _currentSession.Title = newTitle;
                CurrentChatTitleText.Text = newTitle;
            }

            // 4. Add User message with optional image attachment
            var userMsg = new ChatMessage(text, isUser: true)
            {
                ImageBase64 = _pendingImageBase64,
                ImageMimeType = _pendingImageMimeType
            };
            Messages.Add(userMsg);
            _currentSession.Messages.Add(userMsg);

            // 5. Clear Input, clear attachment & lock controls
            InputTextBox.Text = string.Empty;
            ClearPendingAttachment();
            SendButton.IsEnabled = false;
            InputTextBox.IsEnabled = false;

            // 6. Save Session state
            await _historyService.SaveSessionAsync(_currentSession);

            // 7. Show native ProgressRing typing indicator
            TypingIndicatorText.Text = $"{_selectedModel.DisplayName} is thinking...";
            TypingProgressRing.IsActive = true;
            TypingIndicatorBubble.Visibility = Visibility.Visible;
            ScrollToLatestMessage();

            try
            {
                // 8. Execute async REST request to active AI provider
                string replyText = await _chatService.SendMessageAsync(_selectedModel, _currentSession.Messages);

                // 9. Add AI response to feed
                var aiMsg = new ChatMessage(
                    text: replyText,
                    isUser: false,
                    senderName: _selectedModel.DisplayName
                );

                Messages.Add(aiMsg);
                _currentSession.Messages.Add(aiMsg);
                await _historyService.SaveSessionAsync(_currentSession);
            }
            catch (Exception ex)
            {
                // Handle errors gracefully with native ContentDialog
                bool isKeyMissing = ex.Message.Contains("No API key") || ex.Message.Contains("unauthorized");

                var errorDialog = new ContentDialog
                {
                    Title = isKeyMissing ? "API Key Needed" : "Unable to Send Message",
                    Content = ex.Message,
                    CloseButtonText = "OK"
                };

                if (isKeyMissing)
                {
                    errorDialog.PrimaryButtonText = "Open Settings";
                }

                var dialogResult = await errorDialog.ShowAsync();
                if (dialogResult == ContentDialogResult.Primary && isKeyMissing)
                {
                    OpenSettings_Click(this, null);
                }
            }
            finally
            {
                // 10. Hide progress indicator & re-enable input
                TypingProgressRing.IsActive = false;
                TypingIndicatorBubble.Visibility = Visibility.Collapsed;
                InputTextBox.IsEnabled = true;
                InputTextBox.Focus(FocusState.Programmatic);
                ScrollToLatestMessage();
            }
        }

        private void Suggestion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content != null)
            {
                string text = btn.Content.ToString();
                int firstSpace = text.IndexOf(' ');
                if (firstSpace > 0 && firstSpace <= 3)
                {
                    text = text.Substring(firstSpace + 1).Trim();
                }

                InputTextBox.Text = text;
                InputTextBox.Focus(FocusState.Programmatic);
                InputTextBox.Select(InputTextBox.Text.Length, 0);
            }
        }

        #endregion

        #region Voice Input (Speech-to-Text)

        private async void VoiceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecognizingVoice) return;

            _isRecognizingVoice = true;
            VoiceStatusBanner.Visibility = Visibility.Visible;

            try
            {
                string speechText = await _voiceService.RecognizeSpeechAsync();
                if (!string.IsNullOrWhiteSpace(speechText))
                {
                    string existing = InputTextBox.Text;
                    InputTextBox.Text = string.IsNullOrWhiteSpace(existing)
                        ? speechText
                        : existing.TrimEnd() + " " + speechText;

                    InputTextBox.Focus(FocusState.Programmatic);
                    InputTextBox.Select(InputTextBox.Text.Length, 0);
                }
            }
            finally
            {
                VoiceStatusBanner.Visibility = Visibility.Collapsed;
                _isRecognizingVoice = false;
            }
        }

        #endregion

        #region Navigation & Actions

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (ChatSplitView != null && ChatSplitView.IsPaneOpen)
            {
                ChatSplitView.IsPaneOpen = false;
            }
            Frame.Navigate(typeof(SettingsPage));
        }

        private async void ClearChat_Click(object sender, RoutedEventArgs e)
        {
            if (Messages.Count == 0) return;

            var dialog = new ContentDialog
            {
                Title = "Clear Current Chat?",
                Content = "Are you sure you want to clear all messages in this conversation?",
                PrimaryButtonText = "Clear",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                Messages.Clear();
                if (_currentSession != null)
                {
                    _currentSession.Messages.Clear();
                    await _historyService.SaveSessionAsync(_currentSession);
                }
            }
        }

        private void CopyConversation_Click(object sender, RoutedEventArgs e)
        {
            if (Messages.Count == 0) return;

            var sb = new StringBuilder();
            foreach (var msg in Messages)
            {
                sb.AppendLine($"[{(msg.IsUser ? "You" : msg.SenderName)}] ({msg.FormattedTime}):");
                sb.AppendLine(msg.Text);
                sb.AppendLine();
            }

            var package = new DataPackage();
            package.SetText(sb.ToString());
            Clipboard.SetContent(package);
        }

        private async void About_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "WinAI for Windows 10 Mobile",
                Content = "Version 1.0 (Build 15254 Fall Creators Update)\n\nA native Bring-Your-Own-Key (BYOK) AI chat client designed exclusively for Windows 10 Mobile.",
                CloseButtonText = "OK"
            };

            await dialog.ShowAsync();
        }

        #endregion
    }
}
