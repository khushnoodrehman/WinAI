using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using WinAI.Services;

namespace WinAI.Controls
{
    public sealed partial class VaultProviderItem : UserControl
    {
        private bool _isRevealed = false;
        private string _actualKey = null;

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

        public static readonly DependencyProperty BrandBrushProperty =
            DependencyProperty.Register(
                nameof(BrandBrush),
                typeof(Brush),
                typeof(VaultProviderItem),
                new PropertyMetadata(null, OnProviderChanged));

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

        public Brush BrandBrush
        {
            get => (Brush)GetValue(BrandBrushProperty);
            set => SetValue(BrandBrushProperty, value);
        }

        public event EventHandler<VaultProviderItem> EyeClicked;
        public event EventHandler<VaultProviderItem> EditRequested;
        public event EventHandler<VaultProviderItem> TestRequested;
        public event EventHandler<VaultProviderItem> DeleteRequested;

        public VaultProviderItem()
        {
            this.InitializeComponent();
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
            ProviderNameText.Text = ProviderName ?? string.Empty;

            if (BrandBrush != null)
            {
                IconBorder.Background = BrandBrush;
            }
            else
            {
                IconBorder.Background = GetDefaultBrandBrush(ProviderId);
            }

            IconContentPresenter.Content = CreateProviderIcon(ProviderId);

            if (IsConfigured)
            {
                StatusDot.Foreground = new SolidColorBrush(Color.FromArgb(255, 16, 163, 127)); // #10A37F
                StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 16, 163, 127));
                StatusText.Text = "Connected";
                MaskedKeyText.Opacity = 0.85;
                EyeButton.Opacity = 1.0;
                EyeButton.IsEnabled = true;
                DeleteMenuItem.IsEnabled = true;
            }
            else
            {
                var mutedBrush = new SolidColorBrush(Color.FromArgb(255, 138, 143, 152)); // Gray
                StatusDot.Foreground = mutedBrush;
                StatusText.Foreground = mutedBrush;
                StatusText.Text = "Not configured";
                MaskedKeyText.Opacity = 0.45;
                EyeButton.Opacity = 0.6;
                DeleteMenuItem.IsEnabled = false;
            }

            if (!_isRevealed)
            {
                MaskedKeyText.Text = "••••••••••••";
                EyeIcon.Text = "\uE890"; // Windows 10 Mobile compatible View / Eye symbol
            }
        }

        public void SetActualKey(string key)
        {
            _actualKey = key;
        }

        public void SetRevealed(bool revealed)
        {
            _isRevealed = revealed;
            if (_isRevealed && !string.IsNullOrEmpty(_actualKey))
            {
                // Show masked snippet: e.g. "sk-••••1234" or actual key
                if (_actualKey.Length > 8)
                {
                    MaskedKeyText.Text = _actualKey.Substring(0, 4) + "••••" + _actualKey.Substring(_actualKey.Length - 4);
                }
                else
                {
                    MaskedKeyText.Text = _actualKey;
                }
                EyeIcon.Text = "\uE10A"; // Windows 10 Mobile compatible Clear / Hide symbol
            }
            else
            {
                MaskedKeyText.Text = "••••••••••••";
                EyeIcon.Text = "\uE890"; // Windows 10 Mobile compatible View / Eye symbol
            }
        }

        public bool IsRevealed => _isRevealed;

        public void SetStatus(string status, bool isSuccess)
        {
            if (isSuccess)
            {
                StatusDot.Foreground = new SolidColorBrush(Color.FromArgb(255, 16, 163, 127));
                StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 16, 163, 127));
            }
            else
            {
                StatusDot.Foreground = new SolidColorBrush(Color.FromArgb(255, 224, 64, 64)); // Error red
                StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 224, 64, 64));
            }
            StatusText.Text = status;
        }

        private Brush GetDefaultBrandBrush(string providerId)
        {
            switch (providerId?.ToLowerInvariant())
            {
                case "openai":
                    return new SolidColorBrush(Color.FromArgb(255, 16, 163, 127)); // #10A37F
                case "gemini":
                    return new SolidColorBrush(Color.FromArgb(255, 78, 130, 238)); // #4E82EE
                case "claude":
                    return new SolidColorBrush(Color.FromArgb(255, 217, 119, 87)); // #D97757
                case "perplexity":
                    return new SolidColorBrush(Color.FromArgb(255, 32, 85, 101));  // #205565
                case "deepseek":
                    return new SolidColorBrush(Color.FromArgb(255, 30, 64, 175));  // #1E40AF
                case "xai":
                    return new SolidColorBrush(Color.FromArgb(255, 17, 17, 17));   // #111111
                default:
                    return new SolidColorBrush(Color.FromArgb(255, 0, 120, 215));  // Windows Blue
            }
        }

        private FrameworkElement CreateProviderIcon(string providerId)
        {
            switch (providerId?.ToLowerInvariant())
            {
                case "openai":
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M12,2 C10.9,2 10,2.9 10,4 C10,4.4 10.1,4.7 10.3,5 C9.8,5.1 9.4,5.4 9.1,5.8 C8.4,5.3 7.5,5 6.5,5 C4.6,5 3,6.6 3,8.5 C3,9.1 3.2,9.7 3.5,10.2 C2.6,10.6 2,11.5 2,12.5 C2,13.9 3.1,15 4.5,15 C4.9,15 5.2,14.9 5.5,14.7 C5.8,15.5 6.6,16 7.5,16 C8.4,16 9.2,15.5 9.5,14.7 C9.8,15.5 10.6,16 11.5,16 C12.9,16 14,14.9 14,13.5 C14,12.9 13.8,12.3 13.5,11.8 C14.4,11.4 15,10.5 15,9.5 C15,8.1 13.9,7 12.5,7 C12.1,7 11.8,7.1 11.5,7.3 C11.2,6.5 10.4,6 9.5,6 C9.3,6 9.2,6 9,6.1 C9,5.4 9.4,4.8 10,4.4 C10.3,4.8 10.8,5 11.3,5 C12.4,5 13.3,4.1 13.3,3 C13.3,2.4 12.7,2 12,2 Z"),
                        Fill = new SolidColorBrush(Colors.White),
                        Stretch = Stretch.Uniform,
                        Width = 20,
                        Height = 20
                    };

                case "gemini":
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M12,2 C12,7.5 16.5,12 22,12 C16.5,12 12,16.5 12,22 C12,16.5 7.5,12 2,12 C7.5,12 12,7.5 12,2 Z"),
                        Fill = new SolidColorBrush(Colors.White),
                        Stretch = Stretch.Uniform,
                        Width = 18,
                        Height = 18
                    };

                case "claude":
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M11,2 L13,2 L13,8 L17.2,3.8 L18.6,5.2 L14.4,9.4 L20,9.4 L20,11.4 L14.4,11.4 L18.6,15.6 L17.2,17 L13,12.8 L13,19 L11,19 L11,12.8 L6.8,17 L5.4,15.6 L9.6,11.4 L4,11.4 L4,9.4 L9.6,9.4 L5.4,5.2 L6.8,3.8 L11,8 Z"),
                        Fill = new SolidColorBrush(Colors.White),
                        Stretch = Stretch.Uniform,
                        Width = 19,
                        Height = 19
                    };

                case "perplexity":
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M11,3 L13,3 L13,7 L17,7 L17,9 L13,9 L13,13 L17,13 L17,15 L13,15 L13,19 L11,19 L11,15 L7,15 L7,13 L11,13 L11,9 L7,9 L7,7 L11,7 Z M9,9 L9,13 L11,13 L11,9 Z"),
                        Fill = new SolidColorBrush(Colors.White),
                        Stretch = Stretch.Uniform,
                        Width = 18,
                        Height = 18
                    };

                case "deepseek":
                    // DeepSeek whale icon
                    return new Path
                    {
                        Data = (Geometry)Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                            typeof(Geometry),
                            "M4,15 C4,10 8,6 14,6 C19,6 22,9 22,12 C22,15 19,18 14,18 C11,18 9,17.5 7,16 L3,18 L4,15 Z M16,10 C15.4,10 15,10.4 15,11 C15,11.6 15.4,12 16,12 C16.6,12 17,11.6 17,11 C17,10.4 16.6,10 16,10 Z"),
                        Fill = new SolidColorBrush(Colors.White),
                        Stretch = Stretch.Uniform,
                        Width = 19,
                        Height = 19
                    };

                case "xai":
                    // xAI stylized 'XI' / 'X' icon
                    return new TextBlock
                    {
                        Text = "xI",
                        FontFamily = new FontFamily("Segoe UI"),
                        FontWeight = Windows.UI.Text.FontWeights.Bold,
                        FontSize = 16,
                        Foreground = new SolidColorBrush(Colors.White),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                default:
                    return new TextBlock
                    {
                        Text = "\uE8D7", // Key icon
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 16,
                        Foreground = new SolidColorBrush(Colors.White),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
            }
        }

        private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (Application.Current.Resources.TryGetValue("AppItemPressedBrush", out object brush))
            {
                RootGrid.Background = brush as Brush;
            }
        }

        private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            RootGrid.Background = new SolidColorBrush(Colors.Transparent);
        }

        private void EyeButton_Click(object sender, RoutedEventArgs e)
        {
            EyeClicked?.Invoke(this, this);
        }

        private void MenuEdit_Click(object sender, RoutedEventArgs e)
        {
            EditRequested?.Invoke(this, this);
        }

        private void MenuTest_Click(object sender, RoutedEventArgs e)
        {
            TestRequested?.Invoke(this, this);
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteRequested?.Invoke(this, this);
        }
    }
}
