using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace WinAI.Controls
{
    /// <summary>
    /// Key Vault Provider Row (Phase 3).
    /// - Always masked key (••••••••••••••••).
    /// - Zero plaintext API keys retained in control memory.
    /// - Three-dot overflow menu for configured rows: Copy API Key, Remove API Key, Cancel.
    /// - Seamless integration with unified WinAIProviderIcon.
    /// </summary>
    public sealed partial class VaultProviderItem : UserControl
    {
        public static readonly DependencyProperty ProviderIdProperty =
            DependencyProperty.Register(
                nameof(ProviderId),
                typeof(string),
                typeof(VaultProviderItem),
                new PropertyMetadata(string.Empty, OnProviderChanged));

        public static readonly DependencyProperty ProviderNameProperty =
            DependencyProperty.Register(
                nameof(ProviderName),
                typeof(string),
                typeof(VaultProviderItem),
                new PropertyMetadata(string.Empty, OnProviderChanged));

        public static readonly DependencyProperty IsConfiguredProperty =
            DependencyProperty.Register(
                nameof(IsConfigured),
                typeof(bool),
                typeof(VaultProviderItem),
                new PropertyMetadata(false, OnProviderChanged));

        public string ProviderId
        {
            get => (string)GetValue(ProviderIdProperty);
            set => SetValue(ProviderIdProperty, value);
        }

        public string ProviderName
        {
            get => (string)GetValue(ProviderNameProperty);
            set => SetValue(ProviderNameProperty, value);
        }

        public bool IsConfigured
        {
            get => (bool)GetValue(IsConfiguredProperty);
            set => SetValue(IsConfiguredProperty, value);
        }

        public event EventHandler<VaultProviderItem> CopyRequested;
        public event EventHandler<VaultProviderItem> DeleteRequested;
        public event EventHandler<VaultProviderItem> EditRequested;

        public VaultProviderItem()
        {
            this.InitializeComponent();
            UpdateUi();
        }

        private static void OnProviderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VaultProviderItem item)
            {
                item.UpdateUi();
            }
        }

        public void UpdateUi()
        {
            if (ProviderNameText == null || BrandIcon == null) return;

            ProviderNameText.Text = ProviderName ?? string.Empty;
            BrandIcon.ProviderId = ProviderId;
            BrandIcon.UpdateVisuals();

            if (IsConfigured)
            {
                MaskedKeyText.Visibility = Visibility.Visible;
                MaskedKeyText.Text = "••••••••••••••••";
                MaskedKeyText.Opacity = 0.8;

                var successBrush = Application.Current.Resources["WinAISuccessBrush"] as Brush 
                    ?? new SolidColorBrush(Color.FromArgb(255, 16, 185, 129));
                StatusDot.Text = "●";
                StatusDot.Foreground = successBrush;
                StatusText.Text = "Configured";
                StatusText.Foreground = successBrush;

                MoreButton.Visibility = Visibility.Visible;
                if (AddKeyRowContainer != null) AddKeyRowContainer.Visibility = Visibility.Collapsed;
            }
            else
            {
                MaskedKeyText.Visibility = Visibility.Collapsed;

                var secondaryBrush = Application.Current.Resources["AppTextSecondaryBrush"] as Brush 
                    ?? new SolidColorBrush(Color.FromArgb(255, 142, 146, 155));
                StatusDot.Text = "○";
                StatusDot.Foreground = secondaryBrush;
                StatusText.Text = "Not configured";
                StatusText.Foreground = secondaryBrush;

                MoreButton.Visibility = Visibility.Collapsed;
                if (AddKeyRowContainer != null) AddKeyRowContainer.Visibility = Visibility.Visible;
            }
        }

        private void SetPressedVisual(bool isPressed)
        {
            VisualStateManager.GoToState(this, isPressed ? "Pressed" : "Normal", true);
        }

        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e) => SetPressedVisual(true);
        private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e) => SetPressedVisual(false);
        private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e) => SetPressedVisual(false);
        private void RootGrid_PointerCanceled(object sender, PointerRoutedEventArgs e) => SetPressedVisual(false);
        private void RootGrid_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => SetPressedVisual(false);

        private void RootGrid_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // If row is clicked outside the 3-dot button, allow editing/setting the key
            EditRequested?.Invoke(this, this);
        }

        private void AddKeyRowButton_Click(object sender, RoutedEventArgs e)
        {
            EditRequested?.Invoke(this, this);
        }

        private void MenuCopy_Click(object sender, RoutedEventArgs e)
        {
            CopyRequested?.Invoke(this, this);
        }

        private void MenuRemove_Click(object sender, RoutedEventArgs e)
        {
            DeleteRequested?.Invoke(this, this);
        }

        private void MenuCancel_Click(object sender, RoutedEventArgs e)
        {
            // Simply closes the flyout
        }
    }
}
