using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace WinAI.Controls
{
    /// <summary>
    /// Backward-compatible adapter for WinAIProviderIcon.
    /// </summary>
    public sealed partial class ProviderIcon : UserControl
    {
        public static readonly DependencyProperty ProviderIdProperty =
            DependencyProperty.Register(
                nameof(ProviderId),
                typeof(string),
                typeof(ProviderIcon),
                new PropertyMetadata("openai", OnVisualPropertyChanged));

        public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.Register(
                nameof(IconSize),
                typeof(double),
                typeof(ProviderIcon),
                new PropertyMetadata(28.0, OnVisualPropertyChanged));

        public static readonly DependencyProperty BadgeCornerRadiusProperty =
            DependencyProperty.Register(
                nameof(BadgeCornerRadius),
                typeof(CornerRadius),
                typeof(ProviderIcon),
                new PropertyMetadata(new CornerRadius(6), OnVisualPropertyChanged));

        public static readonly DependencyProperty IsDimmedProperty =
            DependencyProperty.Register(
                nameof(IsDimmed),
                typeof(bool),
                typeof(ProviderIcon),
                new PropertyMetadata(false, OnVisualPropertyChanged));

        public string ProviderId
        {
            get => (string)GetValue(ProviderIdProperty);
            set => SetValue(ProviderIdProperty, value);
        }

        public double IconSize
        {
            get => (double)GetValue(IconSizeProperty);
            set => SetValue(IconSizeProperty, value);
        }

        public CornerRadius BadgeCornerRadius
        {
            get => (CornerRadius)GetValue(BadgeCornerRadiusProperty);
            set => SetValue(BadgeCornerRadiusProperty, value);
        }

        public bool IsDimmed
        {
            get => (bool)GetValue(IsDimmedProperty);
            set => SetValue(IsDimmedProperty, value);
        }

        public ProviderIcon()
        {
            this.InitializeComponent();
            UpdateVisuals();
        }

        private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ProviderIcon control)
            {
                control.UpdateVisuals();
            }
        }

        public void UpdateVisuals()
        {
            if (InternalIcon == null) return;

            InternalIcon.ProviderId = ProviderId;
            InternalIcon.IconSize = IconSize;
            InternalIcon.BadgeCornerRadius = BadgeCornerRadius;
            InternalIcon.IsDimmed = IsDimmed;
            InternalIcon.UpdateVisuals();
        }

        public static Brush GetBrandBrush(string providerId) => WinAIProviderIcon.GetBrandBrush(providerId);

        public static FrameworkElement CreateIconGraphic(string providerId, double parentSize = 28)
        {
            return WinAIProviderIcon.CreateVectorGraphic(providerId, Windows.UI.Colors.White, parentSize * 0.6);
        }
    }
}
