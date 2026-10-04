using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public class ProviderListItemViewModel
    {
        public string ProviderId { get; set; }
        public string DisplayName { get; set; }
        public bool IsConfigured { get; set; }
        public string StatusText => IsConfigured ? "Configured" : "Not configured";
        public string StatusDot => "●";
        public Brush StatusBrush { get; set; }
        public string ModelCountText { get; set; }
        public string IconGlyph { get; set; }
        public Brush IconBackground { get; set; }
    }

    public sealed partial class AiProvidersPage : Page
    {
        private readonly AiProviderRegistry _providerRegistry = AiProviderRegistry.Instance;
        private readonly ModelService _modelService = ModelService.Instance;
        private readonly CredentialVaultService _vaultService = CredentialVaultService.Instance;
        private DispatcherTimer _notificationTimer;

        public ObservableCollection<ProviderListItemViewModel> ProvidersList { get; } = 
            new ObservableCollection<ProviderListItemViewModel>();

        public AiProvidersPage()
        {
            this.InitializeComponent();
            SetupNotificationTimer();
            ProvidersListView.ItemsSource = ProvidersList;
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

            LoadProviders();

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested += OnBackRequested;
            navManager.AppViewBackButtonVisibility = Frame.CanGoBack
                ? AppViewBackButtonVisibility.Visible
                : AppViewBackButtonVisibility.Collapsed;

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
            var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                LoadProviders();
            });
        }

        private void OnVaultStateChanged(object sender, EventArgs e)
        {
            var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                LoadProviders();
            });
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

        private void LoadProviders()
        {
            ProvidersList.Clear();

            var providers = _providerRegistry.GetProviders();
            foreach (var provider in providers)
            {
                bool isConfigured = provider.IsConfigured;
                var models = _modelService.GetModelsForProvider(provider.Id);
                int totalCount = models.Count;
                int availCount = models.Count(m => m.IsAvailable);

                string countText;
                if (totalCount == 0)
                {
                    countText = "0 models";
                }
                else if (totalCount == availCount)
                {
                    countText = $"{totalCount} {(totalCount == 1 ? "model" : "models")}";
                }
                else
                {
                    countText = $"{availCount}/{totalCount} available";
                }

                var vm = new ProviderListItemViewModel
                {
                    ProviderId = provider.Id,
                    DisplayName = provider.DisplayName,
                    IsConfigured = isConfigured,
                    ModelCountText = countText,
                    StatusBrush = new SolidColorBrush(isConfigured 
                        ? Color.FromArgb(255, 5, 150, 105)   // Emerald green
                        : Color.FromArgb(255, 114, 119, 130)), // Muted slate
                    IconGlyph = GetProviderIconGlyph(provider.Id),
                    IconBackground = GetProviderIconBrush(provider.Id)
                };

                ProvidersList.Add(vm);
            }
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

        private void ProvidersListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ProviderListItemViewModel vm)
            {
                Frame.Navigate(typeof(ProviderDetailPage), vm.ProviderId);
            }
        }

        private async void RefreshAllButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshAllButton.IsEnabled = false;
            ShowNotification("Refreshing models for configured providers...");

            var providers = _providerRegistry.GetProviders().Where(p => p.IsConfigured).ToList();
            int refreshedCount = 0;

            foreach (var p in providers)
            {
                try
                {
                    var res = await _modelService.RefreshModelsForProviderAsync(p.Id);
                    if (res.Success) refreshedCount++;
                }
                catch { }
            }

            RefreshAllButton.IsEnabled = true;
            LoadProviders();
            ShowNotification(refreshedCount > 0 
                ? $"✓ Updated models for {refreshedCount} configured providers." 
                : "No models updated. Check your Key Vault credentials.");
        }

        private void KeyVaultShortcut_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(KeyVaultPage));
        }

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

        #region Drawer Menu Navigation

        private void DrawerHome_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(HomePage));
        private void DrawerNewChat_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(MainPage), "new");
        private void DrawerConversations_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ConversationsPage));
        private void DrawerProviders_Click(object sender, RoutedEventArgs e) => NavDrawer.IsPaneOpen = false;
        private void DrawerVault_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(KeyVaultPage));
        private void DrawerPromptKit_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(PromptKitPage));
        private void DrawerSettings_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SettingsPage));
        private void DrawerPrivacy_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(PrivacyCenterPage));
        private void DrawerAbout_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(AboutPage));

        #endregion
    }
}
