using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

namespace WinAI.Controls
{
    /// <summary>
    /// Unified reusable provider brand icon control (Phase 3 Requirement 6).
    /// Renders official provider brand assets with proper aspect ratio and Light/Dark theme adaptations.
    /// Inputs: ProviderId, Theme.
    /// Output: Correct local brand asset (ms-appx:///Assets/Providers/...).
    /// </summary>
    public sealed partial class WinAIProviderIcon : UserControl
    {
        public static readonly DependencyProperty ProviderIdProperty =
            DependencyProperty.Register(
                nameof(ProviderId),
                typeof(string),
                typeof(WinAIProviderIcon),
                new PropertyMetadata("openai", OnVisualPropertyChanged));

        public static readonly DependencyProperty ThemeProperty =
            DependencyProperty.Register(
                nameof(Theme),
                typeof(ElementTheme),
                typeof(WinAIProviderIcon),
                new PropertyMetadata(ElementTheme.Default, OnVisualPropertyChanged));

        public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.Register(
                nameof(IconSize),
                typeof(double),
                typeof(WinAIProviderIcon),
                new PropertyMetadata(28.0, OnVisualPropertyChanged));

        public static readonly DependencyProperty BadgeCornerRadiusProperty =
            DependencyProperty.Register(
                nameof(BadgeCornerRadius),
                typeof(CornerRadius),
                typeof(WinAIProviderIcon),
                new PropertyMetadata(new CornerRadius(6), OnVisualPropertyChanged));

        public static readonly DependencyProperty ShowBackgroundProperty =
            DependencyProperty.Register(
                nameof(ShowBackground),
                typeof(bool),
                typeof(WinAIProviderIcon),
                new PropertyMetadata(true, OnVisualPropertyChanged));

        public static readonly DependencyProperty BrandBrushProperty =
            DependencyProperty.Register(
                nameof(BrandBrush),
                typeof(Brush),
                typeof(WinAIProviderIcon),
                new PropertyMetadata(null, OnVisualPropertyChanged));

        public static readonly DependencyProperty IsDimmedProperty =
            DependencyProperty.Register(
                nameof(IsDimmed),
                typeof(bool),
                typeof(WinAIProviderIcon),
                new PropertyMetadata(false, OnVisualPropertyChanged));

        public string ProviderId
        {
            get => (string)GetValue(ProviderIdProperty);
            set => SetValue(ProviderIdProperty, value);
        }

        public ElementTheme Theme
        {
            get => (ElementTheme)GetValue(ThemeProperty);
            set => SetValue(ThemeProperty, value);
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

        public bool ShowBackground
        {
            get => (bool)GetValue(ShowBackgroundProperty);
            set => SetValue(ShowBackgroundProperty, value);
        }

        public Brush BrandBrush
        {
            get => (Brush)GetValue(BrandBrushProperty);
            set => SetValue(BrandBrushProperty, value);
        }

        public bool IsDimmed
        {
            get => (bool)GetValue(IsDimmedProperty);
            set => SetValue(IsDimmedProperty, value);
        }

        public WinAIProviderIcon()
        {
            this.InitializeComponent();
            this.Loaded += (s, e) => UpdateVisuals();
            UpdateVisuals();
        }

        private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WinAIProviderIcon control)
            {
                control.UpdateVisuals();
            }
        }

        public void UpdateVisuals()
        {
            if (RootBorder == null || ProviderImage == null) return;

            string normalizedId = NormalizeProviderId(ProviderId);
            bool isDark = IsEffectiveDarkTheme();

            RootBorder.Width = IconSize;
            RootBorder.Height = IconSize;
            RootBorder.CornerRadius = BadgeCornerRadius;
            RootBorder.Opacity = IsDimmed ? 0.45 : 1.0;

            if (ShowBackground)
            {
                RootBorder.Background = BrandBrush ?? GetBrandBrush(normalizedId);
            }
            else
            {
                RootBorder.Background = new SolidColorBrush(Colors.Transparent);
            }

            // Inner image dimensions preserving aspect ratio
            double innerSize = Math.Round(IconSize * (ShowBackground ? 0.60 : 0.85));
            innerSize = Math.Max(12.0, innerSize);
            ProviderImage.Width = innerSize;
            ProviderImage.Height = innerSize;

            // Select appropriate theme asset:
            // When ShowBackground is true, the white monochrome logo on brand background is optimal.
            // When ShowBackground is false, Light theme gets dark/neutral logo and Dark theme gets light logo.
            string folder = (ShowBackground || isDark) ? "dark" : "light";
            string assetUri = $"ms-appx:///Assets/Providers/{folder}/{normalizedId}.png";

            try
            {
                ProviderImage.Visibility = Visibility.Visible;
                if (VectorFallbackPresenter != null)
                {
                    VectorFallbackPresenter.Visibility = Visibility.Collapsed;
                }
                ProviderImage.Source = new BitmapImage(new Uri(assetUri));
            }
            catch
            {
                ShowVectorFallback(normalizedId, isDark, innerSize);
            }

            AutomationProperties.SetName(this, $"{normalizedId} provider icon");
        }

        private void ProviderImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            string normalizedId = NormalizeProviderId(ProviderId);
            bool isDark = IsEffectiveDarkTheme();
            double innerSize = Math.Round(IconSize * (ShowBackground ? 0.60 : 0.85));
            ShowVectorFallback(normalizedId, isDark, innerSize);
        }

        private void ShowVectorFallback(string providerId, bool isDark, double size)
        {
            if (ProviderImage != null) ProviderImage.Visibility = Visibility.Collapsed;
            if (VectorFallbackPresenter == null) return;

            VectorFallbackPresenter.Visibility = Visibility.Visible;
            Color iconColor = (ShowBackground || isDark) ? Colors.White : Color.FromArgb(255, 24, 27, 32);
            VectorFallbackPresenter.Content = CreateVectorGraphic(providerId, iconColor, size);
        }

        private bool IsEffectiveDarkTheme()
        {
            if (Theme == ElementTheme.Light) return false;
            if (Theme == ElementTheme.Dark) return true;

            // ElementTheme.Default -> inspect app requested theme
            try
            {
                return Application.Current.RequestedTheme != ApplicationTheme.Light;
            }
            catch
            {
                return true;
            }
        }

        public static string NormalizeProviderId(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return "custom";

            string id = providerId.Trim().ToLowerInvariant();
            switch (id)
            {
                case "openai":
                case "chatgpt":
                    return "openai";
                case "gemini":
                case "google":
                    return "gemini";
                case "claude":
                case "anthropic":
                    return "claude";
                case "deepseek":
                    return "deepseek";
                case "xai":
                case "grok":
                    return "xai";
                case "perplexity":
                    return "perplexity";
                case "openrouter":
                    return "openrouter";
                default:
                    return "custom";
            }
        }

        public static Brush GetBrandBrush(string providerId)
        {
            switch (NormalizeProviderId(providerId))
            {
                case "openai":
                    return new SolidColorBrush(Color.FromArgb(255, 16, 163, 127)); // #10A37F Emerald
                case "gemini":
                    return new SolidColorBrush(Color.FromArgb(255, 66, 133, 244)); // #4285F4 Google Blue
                case "claude":
                    return new SolidColorBrush(Color.FromArgb(255, 217, 119, 6));  // #D97706 Warm Amber
                case "deepseek":
                    return new SolidColorBrush(Color.FromArgb(255, 0, 102, 255));  // #0066FF DeepSeek Blue
                case "xai":
                    return new SolidColorBrush(Color.FromArgb(255, 17, 17, 17));   // #111111 xAI Black
                case "perplexity":
                    return new SolidColorBrush(Color.FromArgb(255, 32, 178, 170)); // #20B2AA Teal
                case "openrouter":
                    return new SolidColorBrush(Color.FromArgb(255, 99, 102, 241)); // #6366F1 OpenRouter Indigo
                default:
                    return new SolidColorBrush(Color.FromArgb(255, 0, 120, 215));  // #0078D7 Windows Blue
            }
        }

        public static FrameworkElement CreateVectorGraphic(string providerId, Color color, double size = 18)
        {
            var brush = new SolidColorBrush(color);
            switch (NormalizeProviderId(providerId))
            {
                case "openai":
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M12,2 C10.9,2 10,2.9 10,4 C10,4.4 10.1,4.7 10.3,5 C9.8,5.1 9.4,5.4 9.1,5.8 C8.4,5.3 7.5,5 6.5,5 C4.6,5 3,6.6 3,8.5 C3,9.1 3.2,9.7 3.5,10.2 C2.6,10.6 2,11.5 2,12.5 C2,13.9 3.1,15 4.5,15 C4.9,15 5.2,14.9 5.5,14.7 C5.8,15.5 6.6,16 7.5,16 C8.4,16 9.2,15.5 9.5,14.7 C9.8,15.5 10.6,16 11.5,16 C12.9,16 14,14.9 14,13.5 C14,12.9 13.8,12.3 13.5,11.8 C14.4,11.4 15,10.5 15,9.5 C15,8.1 13.9,7 12.5,7 C12.1,7 11.8,7.1 11.5,7.3 C11.2,6.5 10.4,6 9.5,6 C9.3,6 9.2,6 9,6.1 C9,5.4 9.4,4.8 10,4.4 C10.3,4.8 10.8,5 11.3,5 C12.4,5 13.3,4.1 13.3,3 C13.3,2.4 12.7,2 12,2 Z"),
                        Fill = brush,
                        Stretch = Stretch.Uniform,
                        Width = size,
                        Height = size
                    };

                case "gemini":
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M12,2 C12,7.5 16.5,12 22,12 C16.5,12 12,16.5 12,22 C12,16.5 7.5,12 2,12 C7.5,12 12,7.5 12,2 Z"),
                        Fill = brush,
                        Stretch = Stretch.Uniform,
                        Width = size,
                        Height = size
                    };

                case "claude":
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M11,2 L13,2 L13,8 L17.2,3.8 L18.6,5.2 L14.4,9.4 L20,9.4 L20,11.4 L14.4,11.4 L18.6,15.6 L17.2,17 L13,12.8 L13,19 L11,19 L11,12.8 L6.8,17 L5.4,15.6 L9.6,11.4 L4,11.4 L4,9.4 L9.6,9.4 L5.4,5.2 L6.8,3.8 L11,8 Z"),
                        Fill = brush,
                        Stretch = Stretch.Uniform,
                        Width = size,
                        Height = size
                    };

                case "deepseek":
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M4,15 C4,10 8,6 14,6 C19,6 22,9 22,12 C22,15 19,18 14,18 C11,18 9,17.5 7,16 L3,18 L4,15 Z M16,10 C15.4,10 15,10.4 15,11 C15,11.6 15.4,12 16,12 C16.6,12 17,11.6 17,11 C17,10.4 16.6,10 16,10 Z"),
                        Fill = brush,
                        Stretch = Stretch.Uniform,
                        Width = size,
                        Height = size
                    };

                case "xai":
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M3,3 L8,3 L15,14 L15,3 L18,3 L18,21 L13,21 L6,10 L6,21 L3,21 Z"),
                        Fill = brush,
                        Stretch = Stretch.Uniform,
                        Width = size,
                        Height = size
                    };

                case "perplexity":
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M11,3 L13,3 L13,7 L17,7 L17,9 L13,9 L13,13 L17,13 L17,15 L13,15 L13,19 L11,19 L11,15 L7,15 L7,13 L11,13 L11,9 L7,9 L7,7 L11,7 Z M9,9 L9,13 L11,13 L11,9 Z"),
                        Fill = brush,
                        Stretch = Stretch.Uniform,
                        Width = size,
                        Height = size
                    };

                case "openrouter":
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M12,2 L19,6 L19,18 L12,22 L5,18 L5,6 Z M12,4.5 L7,7.5 L12,10.5 L17,7.5 Z M6.5,9.2 L6.5,16.5 L11,19 L11,11.8 Z M13,11.8 L13,19 L17.5,16.5 L17.5,9.2 Z"),
                        Fill = brush,
                        Stretch = Stretch.Uniform,
                        Width = size,
                        Height = size
                    };

                default:
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M5,4 L19,4 L19,20 L5,20 Z M8,2 L10,2 L10,4 L8,4 Z M14,2 L16,2 L16,4 L14,4 Z M8,20 L10,20 L10,22 L8,22 Z M14,20 L16,20 L16,22 L14,22 Z M2,8 L4,8 L4,10 L2,10 Z M2,14 L4,14 L4,16 L2,16 Z M20,8 L22,8 L22,10 L20,10 Z M20,14 L22,14 L22,16 L20,16 Z M9,9 L15,9 L15,15 L9,15 Z"),
                        Fill = brush,
                        Stretch = Stretch.Uniform,
                        Width = size,
                        Height = size
                    };
            }
        }
    }
}
