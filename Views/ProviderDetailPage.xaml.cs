using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using WinAI.Models;
using WinAI.Services;
using WinAI.Services.Providers;

namespace WinAI.Views
{
    public class ModelRowViewModel : INotifyPropertyChanged
    {
        private bool _isTesting;
        private bool _isAvailable;
        private string _status;
        private string _statusDetails;
        private long? _latencyMs;
        private DateTime? _lastValidatedUtc;

        public string Id { get; set; }
        public string DisplayName { get; set; }

        public bool IsAvailable
        {
            get => _isAvailable;
            set
            {
                if (_isAvailable != value)
                {
                    _isAvailable = value;
                    OnPropertyChanged(nameof(IsAvailable));
                    OnPropertyChanged(nameof(StatusBrush));
                    OnPropertyChanged(nameof(StatusGlyph));
                    OnPropertyChanged(nameof(BadgeBackground));
                    OnPropertyChanged(nameof(BadgeForeground));
                    OnPropertyChanged(nameof(FormattedStatus));
                }
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(FormattedStatus));
                    OnPropertyChanged(nameof(BadgeBackground));
                    OnPropertyChanged(nameof(BadgeForeground));
                }
            }
        }

        public string StatusDetails
        {
            get => _statusDetails;
            set
            {
                if (_statusDetails != value)
                {
                    _statusDetails = value;
                    OnPropertyChanged(nameof(StatusDetails));
                    OnPropertyChanged(nameof(ErrorDetailsVisibility));
                }
            }
        }

        public long? LatencyMs
        {
            get => _latencyMs;
            set
            {
                if (_latencyMs != value)
                {
                    _latencyMs = value;
                    OnPropertyChanged(nameof(LatencyMs));
                    OnPropertyChanged(nameof(FormattedValidationTime));
                }
            }
        }

        public DateTime? LastValidatedUtc
        {
            get => _lastValidatedUtc;
            set
            {
                if (_lastValidatedUtc != value)
                {
                    _lastValidatedUtc = value;
                    OnPropertyChanged(nameof(LastValidatedUtc));
                    OnPropertyChanged(nameof(FormattedValidationTime));
                }
            }
        }

        public bool IsTesting
        {
            get => _isTesting;
            set
            {
                if (_isTesting != value)
                {
                    _isTesting = value;
                    OnPropertyChanged(nameof(IsTesting));
                    OnPropertyChanged(nameof(IsNotTesting));
                    OnPropertyChanged(nameof(TestingVisibility));
                    OnPropertyChanged(nameof(StatusGlyphVisibility));
                    OnPropertyChanged(nameof(FormattedStatus));
                    OnPropertyChanged(nameof(FormattedValidationTime));
                }
            }
        }

        public bool IsNotTesting => !IsTesting;

        public Visibility TestingVisibility => IsTesting ? Visibility.Visible : Visibility.Collapsed;
        public Visibility StatusGlyphVisibility => !IsTesting ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ErrorDetailsVisibility => (!IsAvailable && !string.IsNullOrWhiteSpace(StatusDetails)) 
            ? Visibility.Visible : Visibility.Collapsed;

        public string FormattedStatus
        {
            get
            {
                if (IsTesting) return "Testing...";
                if (!string.IsNullOrWhiteSpace(Status)) return Status;
                return IsAvailable ? "Available" : "Unavailable";
            }
        }

        public string FormattedValidationTime
        {
            get
            {
                if (IsTesting) return "Sending test probe...";
                if (!LastValidatedUtc.HasValue) return "Not tested yet";

                var elapsed = DateTime.UtcNow - LastValidatedUtc.Value;
                string timeStr;
                if (elapsed.TotalSeconds < 60) timeStr = "just now";
                else if (elapsed.TotalMinutes < 60) timeStr = $"{(int)elapsed.TotalMinutes}m ago";
                else if (elapsed.TotalHours < 24) timeStr = $"{(int)elapsed.TotalHours}h ago";
                else timeStr = $"{(int)elapsed.TotalDays}d ago";

                if (LatencyMs.HasValue && LatencyMs > 0)
                {
                    return $"Tested {timeStr} • {LatencyMs}ms";
                }
                return $"Tested {timeStr}";
            }
        }

        public string StatusGlyph => IsAvailable ? "\uE73E" : "\uE783"; // Checkmark vs Warning Exclamation

        public Brush StatusBrush => IsAvailable
            ? new SolidColorBrush(Color.FromArgb(255, 5, 150, 105)) // Green
            : new SolidColorBrush(Color.FromArgb(255, 239, 68, 68)); // Red

        public Brush BadgeBackground
        {
            get
            {
                if (IsTesting)
                    return new SolidColorBrush(Color.FromArgb(40, 0, 120, 215));
                if (IsAvailable)
                    return new SolidColorBrush(Color.FromArgb(40, 5, 150, 105)); // 15% green
                return new SolidColorBrush(Color.FromArgb(40, 239, 68, 68)); // 15% red
            }
        }

        public Brush BadgeForeground
        {
            get
            {
                if (IsTesting)
                    return new SolidColorBrush(Color.FromArgb(255, 0, 120, 215));
                if (IsAvailable)
                    return new SolidColorBrush(Color.FromArgb(255, 5, 150, 105));
                return new SolidColorBrush(Color.FromArgb(255, 239, 68, 68));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed partial class ProviderDetailPage : Page
    {
        private string _providerId = "openai";
        private readonly AiProviderRegistry _providerRegistry = AiProviderRegistry.Instance;
        private readonly ModelService _modelService = ModelService.Instance;
        private readonly CredentialVaultService _vaultService = CredentialVaultService.Instance;
        private readonly ProviderDiagnosticService _diagnosticService = ProviderDiagnosticService.Instance;
        private DispatcherTimer _notificationTimer;

        public ObservableCollection<ModelRowViewModel> ModelsList { get; } = 
            new ObservableCollection<ModelRowViewModel>();

        public ProviderDetailPage()
        {
            this.InitializeComponent();
            SetupNotificationTimer();
            ModelsListView.ItemsSource = ModelsList;
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

            if (e.Parameter is string pid && !string.IsNullOrWhiteSpace(pid))
            {
                _providerId = pid;
            }

            LoadProviderData();

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested += OnBackRequested;
            navManager.AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;

            _modelService.ModelStatusChanged += OnModelStatusChanged;
            _vaultService.VaultStateChanged += OnVaultStateChanged;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested -= OnBackRequested;

            _modelService.ModelStatusChanged -= OnModelStatusChanged;
            _vaultService.VaultStateChanged -= OnVaultStateChanged;

            if (_notificationTimer != null && _notificationTimer.IsEnabled)
            {
                _notificationTimer.Stop();
            }
        }

        private void OnModelStatusChanged(object sender, string providerId)
        {
            if (string.Equals(AiProviderRegistry.NormalizeProviderId(providerId), 
                              AiProviderRegistry.NormalizeProviderId(_providerId), 
                              StringComparison.OrdinalIgnoreCase))
            {
                var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    LoadProviderData();
                });
            }
        }

        private void OnVaultStateChanged(object sender, EventArgs e)
        {
            var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                LoadProviderData();
            });
        }

        private void OnBackRequested(object sender, BackRequestedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                e.Handled = true;
                Frame.GoBack();
            }
            else
            {
                e.Handled = true;
                Frame.Navigate(typeof(AiProvidersPage));
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                Frame.Navigate(typeof(AiProvidersPage));
            }
        }

        private void LoadProviderData()
        {
            var provider = _providerRegistry.GetProvider(_providerId);
            string displayName = provider?.DisplayName ?? _providerId;
            bool isConfigured = provider != null && provider.IsConfigured;

            HeaderTitleText.Text = displayName;
            ProviderNameText.Text = displayName;

            if (ProviderBrandIcon != null)
            {
                ProviderBrandIcon.ProviderId = _providerId;
                ProviderBrandIcon.UpdateVisuals();
            }

            if (isConfigured)
            {
                ProviderStatusDot.Text = "●";
                ProviderStatusDot.Foreground = new SolidColorBrush(Color.FromArgb(255, 5, 150, 105));
                ProviderStatusText.Text = "Configured in Key Vault";
                ProviderStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 5, 150, 105));
            }
            else
            {
                ProviderStatusDot.Text = "○";
                ProviderStatusDot.Foreground = new SolidColorBrush(Color.FromArgb(255, 239, 68, 68));
                ProviderStatusText.Text = "Not configured (Key required)";
                ProviderStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 114, 119, 130));
            }

            // Load models from local cache
            var models = _modelService.GetModelsForProvider(_providerId);

            ModelsList.Clear();
            foreach (var m in models)
            {
                var row = new ModelRowViewModel
                {
                    Id = m.Id,
                    DisplayName = m.DisplayName ?? m.Id,
                    IsAvailable = m.IsAvailable,
                    Status = m.Status,
                    StatusDetails = m.StatusDetails,
                    LatencyMs = m.LatencyMs,
                    LastValidatedUtc = m.LastValidatedUtc,
                    IsTesting = m.IsTesting
                };
                ModelsList.Add(row);
            }

            int availCount = models.Count(m => m.IsAvailable);
            ModelsCountText.Text = $"{models.Count} models ({availCount} available)";
        }

        private async void TestModelRowButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string modelId)
            {
                var modelVm = ModelsList.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
                if (modelVm != null)
                {
                    modelVm.IsTesting = true;
                }

                var result = await _diagnosticService.TestModelAsync(_providerId, modelId);

                if (modelVm != null)
                {
                    modelVm.IsTesting = false;
                    modelVm.IsAvailable = result.Success;
                    modelVm.Status = result.Status;
                    modelVm.StatusDetails = result.Message;
                    modelVm.LatencyMs = result.LatencyMs;
                    modelVm.LastValidatedUtc = result.Timestamp;
                }

                // Update count text
                int availCount = ModelsList.Count(m => m.IsAvailable);
                ModelsCountText.Text = $"{ModelsList.Count} models ({availCount} available)";

                ShowNotification(result.Success 
                    ? $"✓ {modelId}: {result.Message}" 
                    : $"✕ {modelId}: {result.Message}");
            }
        }

        private async void TestAllButton_Click(object sender, RoutedEventArgs e)
        {
            var provider = _providerRegistry.GetProvider(_providerId);
            if (provider == null || !provider.IsConfigured)
            {
                ShowNotification($"Configure an API key for {ProviderNameText.Text} first.");
                return;
            }

            TestAllButton.IsEnabled = false;
            ShowNotification("Testing models sequentially...");

            int successCount = 0;
            int total = ModelsList.Count;

            foreach (var model in ModelsList.ToList())
            {
                model.IsTesting = true;
                var res = await _diagnosticService.TestModelAsync(_providerId, model.Id);
                model.IsTesting = false;
                model.IsAvailable = res.Success;
                model.Status = res.Status;
                model.StatusDetails = res.Message;
                model.LatencyMs = res.LatencyMs;
                model.LastValidatedUtc = res.Timestamp;

                if (res.Success) successCount++;
            }

            TestAllButton.IsEnabled = true;
            int avail = ModelsList.Count(m => m.IsAvailable);
            ModelsCountText.Text = $"{total} models ({avail} available)";

            ShowNotification($"Diagnostic complete: {successCount}/{total} models working.");
        }

        private async void RefreshModelsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshModelsButton.IsEnabled = false;
            ShowNotification($"Fetching fresh models from {ProviderNameText.Text} API...");

            var res = await _modelService.RefreshModelsForProviderAsync(_providerId);
            RefreshModelsButton.IsEnabled = true;

            LoadProviderData();

            if (res.Success)
            {
                ShowNotification($"✓ Discovered {res.Models.Count} models and updated local cache.");
            }
            else
            {
                ShowNotification($"✕ Refresh failed: {res.Message}");
            }
        }

        private void ConfigureKey_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(KeyVaultPage));
        }

        private void ShowNotification(string message)
        {
            NotificationText.Text = message;
            NotificationBanner.Visibility = Visibility.Visible;

            _notificationTimer.Stop();
            _notificationTimer.Start();
        }

        private string GetProviderIconGlyph(string providerId)
        {
            string id = providerId?.ToLowerInvariant() ?? "";
            if (id.Contains("gemini") || id.Contains("google")) return "\uE80A";
            if (id.Contains("claude") || id.Contains("anthropic")) return "\uE749";
            if (id.Contains("deepseek")) return "\uE756";
            if (id.Contains("xai") || id.Contains("grok")) return "\uE7C3";
            if (id.Contains("openrouter") || id.Contains("router")) return "\uE968";
            if (id.Contains("perplexity")) return "\uE721";
            if (id.Contains("custom")) return "\uE713";
            return "\uE8BD"; // OpenAI
        }

        private Brush GetProviderIconBrush(string providerId)
        {
            string id = providerId?.ToLowerInvariant() ?? "";
            if (id.Contains("gemini") || id.Contains("google"))
                return new SolidColorBrush(Color.FromArgb(255, 78, 130, 238));
            if (id.Contains("claude") || id.Contains("anthropic"))
                return new SolidColorBrush(Color.FromArgb(255, 217, 119, 87));
            if (id.Contains("deepseek"))
                return new SolidColorBrush(Color.FromArgb(255, 29, 78, 216));
            if (id.Contains("xai") || id.Contains("grok"))
                return new SolidColorBrush(Color.FromArgb(255, 30, 41, 59));
            if (id.Contains("openrouter") || id.Contains("router"))
                return new SolidColorBrush(Color.FromArgb(255, 99, 102, 241));
            if (id.Contains("perplexity"))
                return new SolidColorBrush(Color.FromArgb(255, 32, 85, 101));
            if (id.Contains("custom"))
                return new SolidColorBrush(Color.FromArgb(255, 124, 58, 237));
            return new SolidColorBrush(Color.FromArgb(255, 16, 163, 127)); // OpenAI green
        }
    }
}
