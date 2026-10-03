using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation.Metadata;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using WinAI.Controls;
using WinAI.Data.Models;
using WinAI.Data.Services;
using WinAI.Models;
using WinAI.Services;
using WinAI.Services.Providers;
using WinAI.Views;

namespace WinAI
{
    public sealed partial class MainPage : Page
    {
        private readonly ApiKeyService _apiKeyService = ApiKeyService.Instance;
        private readonly ChatHistoryService _historyService = ChatHistoryService.Instance;
        private readonly ConversationService _conversationService = ConversationService.Instance;
        private readonly IConversationContextBuilder _contextBuilder = new ConversationContextBuilder();
        private readonly ModelService _modelService = ModelService.Instance;
        private readonly VoiceService _voiceService = VoiceService.Instance;
        private readonly AiChatService _chatService = AiChatService.Instance;
        private readonly IAiProviderRegistry _providerRegistry = AiProviderRegistry.Instance;

        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();

        private ChatSession _currentSession;
        private AiModelItem _selectedModel;
        private AiModelDescriptor _selectedModelDescriptor;
        private string _currentProviderId = "openai";
        private string _currentModelId = "gpt-4o";
        private bool _isRecognizingVoice;
        private bool _isSending;
        private DispatcherTimer _toastTimer;

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
            navManager.AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;

            UpdateStatusBar();

            // Load or initialize models and session
            await InitializeChatAsync(e.Parameter as string);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested -= MainPage_BackRequested;
        }

        private void MainPage_BackRequested(object sender, BackRequestedEventArgs e)
        {
            e.Handled = true;
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                Frame.Navigate(typeof(HomePage));
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

        private async Task InitializeChatAsync(string parameter)
        {
            // 1. Load models
            await LoadModelsAsync();

            // 2. If parameter specifies an existing conversation ID or search title
            if (!string.IsNullOrEmpty(parameter) && !parameter.Equals("new", StringComparison.OrdinalIgnoreCase))
            {
                // Check if parameter is a valid conversation ID in SQLite
                var convById = await _conversationService.GetConversationAsync(parameter);
                if (convById != null)
                {
                    await LoadConversationFromDatabaseAsync(convById.Id);
                    UpdateSendButtonState();
                    return;
                }

                // Check if parameter matches a conversation title in SQLite
                var titleMatches = await _conversationService.SearchConversationsAsync(parameter);
                if (titleMatches != null && titleMatches.Count > 0)
                {
                    await LoadConversationFromDatabaseAsync(titleMatches[0].Id);
                    UpdateSendButtonState();
                    return;
                }

                // Check if parameter specifies a model / provider
                HandleNavigationParameter(parameter);
            }

            // 3. If parameter is "new" or a provider, start a clean in-memory session (no DB record until first message)
            if (string.Equals(parameter, "new", StringComparison.OrdinalIgnoreCase))
            {
                var def = GetApplicationDefaultModel();
                SelectModelById(def.Item1, def.Item2);
                _currentSession = await _historyService.CreateNewSessionAsync(_selectedModel?.Id);
                Messages.Clear();
                UpdateSendButtonState();
                return;
            }

            // 4. Default launch: Load the most recent conversation from SQLite if one exists
            if (_currentSession == null)
            {
                var existingConversations = await _conversationService.GetConversationsAsync(includeArchived: false);
                if (existingConversations != null && existingConversations.Count > 0)
                {
                    await LoadConversationFromDatabaseAsync(existingConversations[0].Id);
                }
                else
                {
                    // Fresh clean session waiting for user's first prompt
                    var def = GetApplicationDefaultModel();
                    SelectModelById(def.Item1, def.Item2);
                    _currentSession = await _historyService.CreateNewSessionAsync(_selectedModel?.Id);
                    Messages.Clear();
                }
            }

            UpdateSendButtonState();
        }

        private async Task LoadConversationFromDatabaseAsync(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return;

            var conv = await _conversationService.GetConversationAsync(conversationId);
            if (conv == null) return;

            List<MessageEntity> entities;
            if (conv.MessageCount > 60)
            {
                // Large conversation optimization: incrementally load recent messages to guarantee smooth 60fps UI
                entities = await _conversationService.GetRecentMessagesAsync(conversationId, 60);
            }
            else
            {
                entities = await _conversationService.GetMessagesAsync(conversationId);
            }

            _currentSession = new ChatSession
            {
                Id = conv.Id,
                Title = conv.Title,
                ProviderId = conv.ProviderId,
                SelectedModelId = conv.ModelId,
                ModelDisplayName = conv.ModelId,
                CreatedAt = conv.LocalCreatedAt,
                UpdatedAt = conv.LocalUpdatedAt,
                IsPinned = conv.IsPinned,
                CustomPreview = conv.LastMessagePreview,
                Messages = new List<ChatMessage>()
            };

            Messages.Clear();
            if (entities != null)
            {
                foreach (var entity in entities)
                {
                    bool isUser = entity.Role == MessageRole.User;
                    string sender = "Assistant";
                    if (isUser)
                    {
                        sender = "You";
                    }
                    else if (!string.IsNullOrWhiteSpace(entity.ModelId))
                    {
                        var desc = _providerRegistry.GetModel(entity.ProviderId, entity.ModelId);
                        sender = desc?.DisplayName ?? entity.ModelId;
                    }

                    var chatMsg = new ChatMessage(entity.Content, isUser, sender, entity.ProviderId, entity.ModelId)
                    {
                        Id = entity.Id,
                        Timestamp = entity.LocalCreatedAt
                    };

                    Messages.Add(chatMsg);
                    _currentSession.Messages.Add(chatMsg);
                }
            }

            // Select the model previously remembered for this conversation
            if (!string.IsNullOrWhiteSpace(conv.ModelId))
            {
                SelectModelById(conv.ProviderId ?? "openai", conv.ModelId);
            }

            ScrollToLatestMessage();
        }

        private void HandleNavigationParameter(string parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return;

            string lower = parameter.ToLowerInvariant();
            if (lower.Contains("gemini"))
            {
                SelectModelById("gemini", "gemini-1.5-flash", "Gemini 1.5 Flash");
            }
            else if (lower.Contains("claude"))
            {
                SelectModelById("claude", "claude-3-5-sonnet-20241022", "Claude 3.5 Sonnet");
            }
            else if (lower.Contains("perplexity"))
            {
                SelectModelById("perplexity", "sonar", "Sonar");
            }
            else if (lower.Contains("deepseek"))
            {
                SelectModelById("deepseek", "deepseek-chat", "DeepSeek Chat");
            }
            else
            {
                SelectModelById("openai", "gpt-4o", "GPT-4o");
            }
        }

        private async Task LoadModelsAsync(bool forceRefresh = false)
        {
            try
            {
                var models = await _modelService.GetAvailableModelsAsync(forceRefresh);
                PopulateSwitchAiFlyout();

                if (models.Count > 0)
                {
                    var match = models.FirstOrDefault(m => m.Id == _currentSession?.SelectedModelId) ?? models[0];
                    _selectedModel = match;
                    string prov = !string.IsNullOrEmpty(_currentProviderId) ? _currentProviderId : AiProviderRegistry.NormalizeProviderId(match.ProviderName);
                    var desc = _providerRegistry.GetModel(prov, match.Id) ?? match.ToDescriptor(prov);
                    if (desc != null)
                    {
                        _selectedModelDescriptor = desc;
                        UpdateModelHeaderVisuals(desc);
                    }
                    else
                    {
                        SelectedModelHeaderText.Text = match.DisplayName;
                    }
                }
                else
                {
                    _selectedModel = new AiModelItem("gpt-4o", "GPT-4o", "OpenAI", "gpt-4o");
                    var desc = _providerRegistry.GetModel("openai", "gpt-4o");
                    if (desc != null)
                    {
                        _selectedModelDescriptor = desc;
                        UpdateModelHeaderVisuals(desc);
                    }
                    else
                    {
                        SelectedModelHeaderText.Text = "GPT-4o";
                    }
                }
            }
            catch
            {
                _selectedModel = new AiModelItem("gpt-4o", "GPT-4o", "OpenAI", "gpt-4o");
                var desc = _providerRegistry.GetModel("openai", "gpt-4o");
                if (desc != null)
                {
                    _selectedModelDescriptor = desc;
                    UpdateModelHeaderVisuals(desc);
                }
                else
                {
                    SelectedModelHeaderText.Text = "GPT-4o";
                }
            }
        }

        private void SwitchAiFlyout_Opening(object sender, object e)
        {
            PopulateSwitchAiFlyout();
        }

        private void PopulateSwitchAiFlyout()
        {
            if (SwitchAiModelList == null) return;

            try
            {
                SwitchAiModelList.Children.Clear();

                var providers = _providerRegistry.GetProviders();
                foreach (var provider in providers)
                {
                    var models = provider.GetModels();
                    if (models == null || models.Count == 0) continue;

                    foreach (var m in models)
                    {
                        bool isCurrent = string.Equals(m.ProviderId, _currentProviderId, StringComparison.OrdinalIgnoreCase) &&
                                         string.Equals(m.Id, _currentModelId, StringComparison.OrdinalIgnoreCase);

                        var row = new ModelSwitcherRowItem();
                        row.BindModel(m, isCurrent);
                        row.ModelTapped += OnModelSelectedFromFlyout;

                        SwitchAiModelList.Children.Add(row);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] Error populating Switch AI flyout: {ex.Message}");
            }
        }

        private void UpdateModelHeaderVisuals(AiModelDescriptor model)
        {
            if (model == null) return;

            if (HeaderProviderIcon != null)
            {
                HeaderProviderIcon.ProviderId = model.ProviderId;
                HeaderProviderIcon.UpdateVisuals();
            }

            if (SelectedModelHeaderText != null)
            {
                SelectedModelHeaderText.Text = model.DisplayName;
            }

            if (SelectedProviderHeaderText != null)
            {
                SelectedProviderHeaderText.Text = model.ProviderName;
            }

            if (KeyRequiredHeaderWarning != null)
            {
                bool isConfigured = _providerRegistry.IsProviderConfigured(model.ProviderId);
                KeyRequiredHeaderWarning.Visibility = isConfigured ? Visibility.Collapsed : Visibility.Visible;
            }

            if (ModelSelectorButton != null)
            {
                AutomationProperties.SetName(ModelSelectorButton, "Switch AI model");
                AutomationProperties.SetHelpText(ModelSelectorButton, $"Current model: {model.DisplayName}, {model.ProviderName}");
            }
        }

        private void ShowSwitchFeedback(AiModelDescriptor model)
        {
            if (model == null || ModelSwitchToast == null) return;

            if (SwitchToastIcon != null)
            {
                SwitchToastIcon.ProviderId = model.ProviderId;
                SwitchToastIcon.UpdateVisuals();
            }

            if (SwitchToastText != null)
            {
                SwitchToastText.Text = $"Switched to {model.DisplayName}";
            }

            ModelSwitchToast.Visibility = Visibility.Visible;

            if (_toastTimer == null)
            {
                _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                _toastTimer.Tick += (s, e) =>
                {
                    _toastTimer.Stop();
                    if (ModelSwitchToast != null)
                    {
                        ModelSwitchToast.Visibility = Visibility.Collapsed;
                    }
                };
            }

            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private async void OnModelSelectedFromFlyout(object sender, AiModelDescriptor targetModel)
        {
            SwitchAiFlyout?.Hide();
            if (targetModel == null) return;

            if (!targetModel.IsConfigured)
            {
                var dialog = new ContentDialog
                {
                    Title = $"{targetModel.ProviderName} Not Configured",
                    Content = $"{targetModel.ProviderName} requires an API key before you can use this model in this conversation. Would you like to add your key in Key Vault now?",
                    PrimaryButtonText = "Add API Key",
                    SecondaryButtonText = "Cancel"
                };

                var res = await dialog.ShowAsync();
                if (res == ContentDialogResult.Primary)
                {
                    Frame.Navigate(typeof(KeyVaultPage));
                }
                return;
            }

            bool isDifferent = !string.Equals(_currentProviderId, targetModel.ProviderId, StringComparison.OrdinalIgnoreCase) ||
                               !string.Equals(_currentModelId, targetModel.Id, StringComparison.OrdinalIgnoreCase);

            var switchResult = _providerRegistry.TrySwitchModel(
                _currentProviderId,
                _currentModelId,
                targetModel.ProviderId,
                targetModel.Id
            );

            if (!switchResult.Success)
            {
                if (switchResult.RequiresConfiguration)
                {
                    var dialog = new ContentDialog
                    {
                        Title = $"{switchResult.TargetProviderName} Not Configured",
                        Content = $"{switchResult.TargetProviderName} requires an API key before you can use this model in this conversation. Would you like to add your key in Key Vault now?",
                        PrimaryButtonText = "Add API Key",
                        SecondaryButtonText = "Cancel"
                    };

                    var res = await dialog.ShowAsync();
                    if (res == ContentDialogResult.Primary)
                    {
                        Frame.Navigate(typeof(KeyVaultPage));
                    }
                }
                else
                {
                    var dialog = new Windows.UI.Popups.MessageDialog(switchResult.ErrorMessage ?? "Unable to switch model.", "Switch AI");
                    await dialog.ShowAsync();
                }
                return;
            }

            // Model switch succeeded within the SAME conversation!
            _currentProviderId = switchResult.ProviderId;
            _currentModelId = switchResult.ModelId;
            _selectedModelDescriptor = switchResult.SelectedModel;
            _selectedModel = switchResult.SelectedModel.ToModelItem();

            UpdateModelHeaderVisuals(switchResult.SelectedModel);

            if (_currentSession != null)
            {
                _currentSession.ProviderId = _currentProviderId;
                _currentSession.SelectedModelId = _currentModelId;
                _currentSession.ModelDisplayName = switchResult.SelectedModel.DisplayName;

                // Persist the current provider and model to local SQLite for this conversation
                await _conversationService.UpdateConversationModelAsync(_currentSession.Id, _currentProviderId, _currentModelId);
            }

            if (isDifferent)
            {
                ShowSwitchFeedback(switchResult.SelectedModel);

                // Inline divider for model change within conversation
                if (Messages.Count > 0)
                {
                    var divider = new ChatMessage
                    {
                        Text = $"Switched to {switchResult.SelectedModel.DisplayName}",
                        IsUser = false,
                        IsSystemNotification = true,
                        ProviderId = switchResult.SelectedModel.ProviderId,
                        ModelId = switchResult.SelectedModel.Id,
                        Timestamp = DateTime.Now
                    };
                    Messages.Add(divider);
                }
            }
        }

        private void ManageKeysFlyoutButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchAiFlyout?.Hide();
            Frame.Navigate(typeof(KeyVaultPage));
        }

        private Tuple<string, string> GetApplicationDefaultModel()
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
                    return Tuple.Create(descriptor.ProviderId, descriptor.Id);
                }
                return Tuple.Create(normProvider, defaultModel ?? "gpt-4o");
            }
            catch
            {
                return Tuple.Create("openai", "gpt-4o");
            }
        }

        private void SelectModelById(string providerKey, string modelId, string displayName = null)
        {
            string pKey = AiProviderRegistry.NormalizeProviderId(providerKey);
            var descriptor = _providerRegistry.GetModel(pKey, modelId);

            if (descriptor != null)
            {
                _currentProviderId = pKey;
                _currentModelId = descriptor.Id;
                _selectedModelDescriptor = descriptor;
                _selectedModel = descriptor.ToModelItem();
            }
            else
            {
                _currentProviderId = pKey;
                _currentModelId = modelId ?? "gpt-4o";
                string dName = displayName ?? modelId ?? "GPT-4o";
                _selectedModelDescriptor = new AiModelDescriptor(_currentModelId, dName, _currentProviderId, GetProviderDisplayName(_currentProviderId));
                _selectedModel = _selectedModelDescriptor.ToModelItem();
            }

            UpdateModelHeaderVisuals(_selectedModelDescriptor);

            if (_currentSession != null)
            {
                _currentSession.SelectedModelId = _currentModelId;
                _currentSession.ModelDisplayName = SelectedModelHeaderText?.Text ?? _selectedModelDescriptor.DisplayName;
                _currentSession.ProviderId = _currentProviderId;
            }
        }

        private string GetProviderDisplayName(string providerKey)
        {
            var provider = _providerRegistry.GetProvider(providerKey);
            return provider?.DisplayName ?? "OpenAI";
        }

        private string GetCurrentProviderId()
        {
            return _currentProviderId ?? _currentSession?.ProviderId ?? "openai";
        }

        private async void SelectModel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string tag)
            {
                var parts = tag.Split('|');
                string targetProvider = parts.Length > 0 ? parts[0] : "openai";
                string targetModel = parts.Length > 1 ? parts[1] : "gpt-4o";

                bool isDifferent = !string.Equals(_currentProviderId, targetProvider, StringComparison.OrdinalIgnoreCase) ||
                                   !string.Equals(_currentModelId, targetModel, StringComparison.OrdinalIgnoreCase);

                var switchResult = _providerRegistry.TrySwitchModel(
                    _currentProviderId, 
                    _currentModelId, 
                    targetProvider, 
                    targetModel
                );

                if (!switchResult.Success)
                {
                    if (switchResult.RequiresConfiguration)
                    {
                        var dialog = new ContentDialog
                        {
                            Title = $"{switchResult.TargetProviderName} Not Configured",
                            Content = $"{switchResult.TargetProviderName} requires an API key before you can use this model in this conversation. Would you like to add your key in Key Vault now?",
                            PrimaryButtonText = "Add API Key",
                            SecondaryButtonText = "Cancel"
                        };

                        var res = await dialog.ShowAsync();
                        if (res == ContentDialogResult.Primary)
                        {
                            Frame.Navigate(typeof(KeyVaultPage));
                        }
                    }
                    else
                    {
                        var dialog = new Windows.UI.Popups.MessageDialog(switchResult.ErrorMessage ?? "Unable to switch model.", "Switch AI");
                        await dialog.ShowAsync();
                    }
                    return;
                }

                // Model switch succeeded within the SAME conversation!
                _currentProviderId = switchResult.ProviderId;
                _currentModelId = switchResult.ModelId;
                _selectedModelDescriptor = switchResult.SelectedModel;
                _selectedModel = switchResult.SelectedModel.ToModelItem();

                UpdateModelHeaderVisuals(switchResult.SelectedModel);

                if (_currentSession != null)
                {
                    _currentSession.ProviderId = _currentProviderId;
                    _currentSession.SelectedModelId = _currentModelId;
                    _currentSession.ModelDisplayName = switchResult.SelectedModel.DisplayName;

                    // Persist the current provider and model to local SQLite for this conversation
                    await _conversationService.UpdateConversationModelAsync(_currentSession.Id, _currentProviderId, _currentModelId);
                }

                if (isDifferent)
                {
                    ShowSwitchFeedback(switchResult.SelectedModel);

                    if (Messages.Count > 0)
                    {
                        var divider = new ChatMessage
                        {
                            Text = $"Switched to {switchResult.SelectedModel.DisplayName}",
                            IsUser = false,
                            IsSystemNotification = true,
                            ProviderId = switchResult.SelectedModel.ProviderId,
                            ModelId = switchResult.SelectedModel.Id,
                            Timestamp = DateTime.Now
                        };
                        Messages.Add(divider);
                    }
                }
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

            ScrollToLatestMessage();
        }

        private async void ApiKeyService_KeysChanged(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                await LoadModelsAsync();
                if (_selectedModelDescriptor != null)
                {
                    UpdateModelHeaderVisuals(_selectedModelDescriptor);
                }
            });
        }

        private async void HistoryService_SessionsUpdated(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // Refresh if needed
            });
        }

        #region Messaging & Auto-Scroll

        private void Messages_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
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
                // Ignore layout race conditions
            }
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

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        private async void SendMessage()
        {
            // 0. Double-tap duplicate protection
            if (_isSending) return;
            _isSending = true;

            string text = InputTextBox.Text?.Trim() ?? string.Empty;
            bool hasImage = !string.IsNullOrEmpty(_pendingImageBase64);

            if (string.IsNullOrWhiteSpace(text) && !hasImage)
            {
                _isSending = false;
                return;
            }

            // 1. Validate internet connectivity before making API call
            if (!_chatService.HasInternetConnection())
            {
                _isSending = false;
                var netDialog = new ContentDialog
                {
                    Title = "No Internet Connection",
                    Content = "Please check your Wi-Fi or cellular network connection and try again.",
                    PrimaryButtonText = "OK"
                };
                await netDialog.ShowAsync();
                return;
            }

            // 2. Snapshot request parameters to protect against race conditions during in-flight request
            string requestProviderId = _currentProviderId ?? "openai";
            string requestModelId = _currentModelId ?? _selectedModel?.Id ?? "gpt-4o";
            var requestModel = _selectedModelDescriptor ?? _providerRegistry.GetModel(requestProviderId, requestModelId);

            // 3. Ensure current session
            if (_currentSession == null)
            {
                _currentSession = await _historyService.CreateNewSessionAsync(requestModelId);
                _currentSession.ProviderId = requestProviderId;
                _currentSession.SelectedModelId = requestModelId;
            }

            string sendingText = text;
            string sendingImageBase64 = _pendingImageBase64;
            string sendingImageMime = _pendingImageMimeType;

            // 4. Clear input, clear attachment & lock controls immediately to prevent duplicates
            InputTextBox.Text = string.Empty;
            ClearPendingAttachment();
            SendButton.IsEnabled = false;
            InputTextBox.IsEnabled = false;
            if (ModelSelectorButton != null) ModelSelectorButton.IsEnabled = false;

            // 5. Add User message to in-memory UI collection
            var userMsg = new ChatMessage(sendingText, isUser: true)
            {
                ImageBase64 = sendingImageBase64,
                ImageMimeType = sendingImageMime,
                Timestamp = DateTime.Now
            };
            Messages.Add(userMsg);
            _currentSession.Messages.Add(userMsg);

            // 6. PERSIST USER MESSAGE LOCALLY IN SQLITE IMMEDIATELY
            // If the provider request fails or app closes, this user message is durable!
            try
            {
                var userEntity = await _conversationService.AddUserMessageAsync(
                    conversationId: _currentSession.Id,
                    content: sendingText,
                    providerId: requestProviderId,
                    modelId: requestModelId
                );

                if (userEntity != null)
                {
                    userMsg.Id = userEntity.Id;
                    userMsg.Timestamp = userEntity.LocalCreatedAt;
                }

                // If local title generation updated the title, synchronize in-memory session
                var conv = await _conversationService.GetConversationAsync(_currentSession.Id);
                if (conv != null)
                {
                    _currentSession.Title = conv.Title;
                    _currentSession.UpdatedAt = conv.LocalUpdatedAt;
                }
            }
            catch (Exception dbEx)
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] SQLite save user message error: {dbEx.Message}");
            }

            // 7. Show native typing indicator
            string modelName = requestModel?.DisplayName ?? _selectedModel?.DisplayName ?? "AI";
            TypingIndicatorText.Text = $"{modelName} is thinking...";
            TypingProgressRing.IsActive = true;
            TypingIndicatorBubble.Visibility = Visibility.Visible;
            ScrollToLatestMessage();

            try
            {
                // 8. Build context for AI provider request from local SQLite database
                var contextMessages = await _contextBuilder.BuildContextAsync(_currentSession.Id, maxRecentMessages: 20);

                // Fallback to in-memory session messages if context is empty
                if (contextMessages == null || contextMessages.Count == 0)
                {
                    contextMessages = _currentSession.Messages.Where(m => !m.IsSystemNotification).ToList();
                }

                // Attach vision image data to the active user prompt in context if present
                if (!string.IsNullOrEmpty(sendingImageBase64))
                {
                    var lastUserContext = contextMessages.LastOrDefault(m => m.IsUser);
                    if (lastUserContext != null)
                    {
                        lastUserContext.ImageBase64 = sendingImageBase64;
                        lastUserContext.ImageMimeType = sendingImageMime;
                    }
                }

                // 9. Execute async REST request to active AI provider
                string replyText = await _chatService.SendMessageAsync(
                    requestModel, 
                    contextMessages
                );

                // 10. Persist Assistant response to SQLite
                var aiEntity = await _conversationService.AddAssistantMessageAsync(
                    conversationId: _currentSession.Id,
                    content: replyText,
                    providerId: requestProviderId,
                    modelId: requestModelId,
                    isError: false
                );

                // 11. Add AI response to feed
                var aiMsg = new ChatMessage(
                    text: replyText,
                    isUser: false,
                    senderName: requestModel?.DisplayName ?? _selectedModel?.DisplayName ?? "Assistant",
                    providerId: requestProviderId,
                    modelId: requestModelId
                )
                {
                    Id = aiEntity?.Id ?? Guid.NewGuid().ToString("D"),
                    Timestamp = aiEntity?.LocalCreatedAt ?? DateTime.Now
                };

                Messages.Add(aiMsg);
                _currentSession.Messages.Add(aiMsg);
            }
            catch (Exception ex)
            {
                // Note: The user message was ALREADY persisted to SQLite in Step 6.
                // It will NOT be lost!
                string msg = ex.Message ?? "";
                bool isKeyMissing = msg.IndexOf("key is missing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    msg.IndexOf("not configured", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    msg.IndexOf("No API key", StringComparison.OrdinalIgnoreCase) >= 0;

                bool isKeyInvalid = msg.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    msg.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    msg.IndexOf("invalid api key", StringComparison.OrdinalIgnoreCase) >= 0;

                bool isForbidden = msg.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   msg.IndexOf("forbidden", StringComparison.OrdinalIgnoreCase) >= 0;

                bool isRateLimited = msg.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     msg.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     msg.IndexOf("quota", StringComparison.OrdinalIgnoreCase) >= 0;

                bool isNetworkError = msg.IndexOf("connect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      msg.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      msg.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;

                string providerName = requestModel?.ProviderName ?? GetProviderDisplayName(requestProviderId);
                string dialogTitle;
                string dialogContent;

                if (isKeyMissing)
                {
                    dialogTitle = $"{providerName} isn't configured";
                    dialogContent = $"{providerName} requires an API key before sending requests. Please configure your key in Key Vault.";
                }
                else if (isKeyInvalid)
                {
                    dialogTitle = $"{providerName} API Key Invalid";
                    dialogContent = $"The {providerName} API key was rejected (401 Unauthorized). Please check or update it in Key Vault.";
                }
                else if (isForbidden)
                {
                    dialogTitle = $"{providerName} Access Forbidden";
                    dialogContent = $"Access was denied by {providerName} (403 Forbidden). Please check your account permissions or billing tier.";
                }
                else if (isRateLimited)
                {
                    dialogTitle = $"{providerName} Rate Limit Reached";
                    dialogContent = $"The {providerName} API rate limit or quota has been reached.\n\nYou can wait a moment and retry, or switch to another AI model.";
                }
                else if (isNetworkError)
                {
                    dialogTitle = "Connection Error";
                    dialogContent = $"Couldn't connect to {providerName}. Please check your connection and try again.";
                }
                else
                {
                    dialogTitle = $"{providerName} Error";
                    dialogContent = ex.Message;
                }

                bool isKeyIssue = isKeyMissing || isKeyInvalid;
                var errorDialog = new ContentDialog
                {
                    Title = dialogTitle,
                    Content = dialogContent,
                    PrimaryButtonText = isKeyIssue ? "Open Key Vault" : "Retry",
                    SecondaryButtonText = isKeyIssue ? "Cancel" : "Switch AI"
                };

                var dialogResult = await errorDialog.ShowAsync();
                if (isKeyIssue && dialogResult == ContentDialogResult.Primary)
                {
                    Frame.Navigate(typeof(KeyVaultPage));
                }
                else if (!isKeyIssue)
                {
                    if (dialogResult == ContentDialogResult.Primary)
                    {
                        SendMessage();
                    }
                    else if (dialogResult == ContentDialogResult.Secondary)
                    {
                        SwitchAiFlyout?.ShowAt(ModelSelectorButton);
                    }
                }
            }
            finally
            {
                TypingProgressRing.IsActive = false;
                TypingIndicatorBubble.Visibility = Visibility.Collapsed;
                InputTextBox.IsEnabled = true;
                if (ModelSelectorButton != null) ModelSelectorButton.IsEnabled = true;
                _isSending = false;
                UpdateSendButtonState();
                InputTextBox.Focus(FocusState.Programmatic);
                ScrollToLatestMessage();
            }
        }

        #endregion

        #region Attachments & Voice

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

                string ext = file.FileType.ToLowerInvariant();
                string mimeType = "image/jpeg";
                if (ext == ".png") mimeType = "image/png";
                else if (ext == ".webp") mimeType = "image/webp";
                else if (ext == ".bmp") mimeType = "image/bmp";

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
                    PrimaryButtonText = "OK"
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

        private async void VoiceDictation_Click(object sender, RoutedEventArgs e)
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

        #region Actions & Navigation

        private void HeaderBackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                Frame.Navigate(typeof(HomePage));
            }
        }

        private void NavigateToHome_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(HomePage));
        }

        private void NavigateToChats_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ConversationsPage));
        }

        private void NavigateToVault_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(KeyVaultPage));
        }

        private void MoreTab_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SettingsPage));
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SettingsPage));
        }

        private async void NewChat_Click(object sender, RoutedEventArgs e)
        {
            Messages.Clear();
            _currentSession = await _historyService.CreateNewSessionAsync(_selectedModel?.Id);
            InputTextBox.Text = string.Empty;
            ClearPendingAttachment();
            UpdateSendButtonState();
            InputTextBox.Focus(FocusState.Programmatic);
        }

        private async void ClearChat_Click(object sender, RoutedEventArgs e)
        {
            if (Messages.Count == 0) return;

            var dialog = new ContentDialog
            {
                Title = "Clear Current Chat?",
                Content = "Are you sure you want to clear all messages in this conversation?",
                PrimaryButtonText = "Clear",
                SecondaryButtonText = "Cancel"
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (_currentSession != null)
                {
                    await _conversationService.DeleteConversationAsync(_currentSession.Id);
                    _currentSession = await _historyService.CreateNewSessionAsync(_selectedModel?.Id);
                }
                Messages.Clear();
                UpdateSendButtonState();
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

        private void CopyMessage_Click(object sender, RoutedEventArgs e)
        {
            string content = null;
            if (sender is Button btn && btn.Tag is string tag)
            {
                content = tag;
            }

            if (!string.IsNullOrEmpty(content))
            {
                var package = new DataPackage();
                package.SetText(content);
                Clipboard.SetContent(package);
            }
        }

        private void LikeMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.Opacity = 0.5;
            }
        }

        private void DislikeMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.Opacity = 0.5;
            }
        }

        private void MoreMessageAction_Click(object sender, RoutedEventArgs e)
        {
            CopyMessage_Click(sender, e);
        }

        #endregion
    }
}
