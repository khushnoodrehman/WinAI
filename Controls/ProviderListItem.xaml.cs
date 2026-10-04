using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using WinAI.Models;

namespace WinAI.Controls
{
    public sealed partial class ProviderListItem : UserControl
    {
        public static readonly DependencyProperty ProviderProperty =
            DependencyProperty.Register(
                nameof(Provider),
                typeof(AIProvider),
                typeof(ProviderListItem),
                new PropertyMetadata(null, OnProviderChanged));

        public AIProvider Provider
        {
            get => (AIProvider)GetValue(ProviderProperty);
            set => SetValue(ProviderProperty, value);
        }

        public event EventHandler<AIProvider> ItemClick;

        public ProviderListItem()
        {
            this.InitializeComponent();
        }

        private static void OnProviderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ProviderListItem item && e.NewValue is AIProvider provider)
            {
                item.UpdateUi(provider);
            }
        }

        private void UpdateUi(AIProvider provider)
        {
            if (provider == null) return;

            ProviderNameText.Text = provider.Name ?? string.Empty;
            if (ItemBrandIcon != null)
            {
                ItemBrandIcon.ProviderId = provider.Id ?? provider.IconType;
                ItemBrandIcon.UpdateVisuals();
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
            if (Provider != null)
            {
                ItemClick?.Invoke(this, Provider);
            }
        }
    }
}
