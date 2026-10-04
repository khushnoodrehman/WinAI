using System;
using System.Collections.Generic;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using WinAI.Models;

namespace WinAI.Controls
{
    /// <summary>
    /// Touch-friendly, accessible model row inside the "Switch AI" flyout.
    /// Follows the native Windows 10 Mobile flat design language with clear selection
    /// and unconfigured API key availability indicators.
    /// </summary>
    public sealed partial class ModelSwitcherRowItem : UserControl
    {
        public AiModelDescriptor Descriptor { get; private set; }
        public bool IsCurrent { get; private set; }

        public event EventHandler<AiModelDescriptor> ModelTapped;

        public ModelSwitcherRowItem()
        {
            this.InitializeComponent();
        }

        public void BindModel(AiModelDescriptor descriptor, bool isCurrent)
        {
            Descriptor = descriptor;
            IsCurrent = isCurrent;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (Descriptor == null) return;

            ItemProviderIcon.ProviderId = Descriptor.ProviderId;
            ItemProviderIcon.IsDimmed = !Descriptor.IsConfigured;

            ModelNameText.Text = Descriptor.DisplayName ?? Descriptor.Id;
            ProviderNameText.Text = Descriptor.ProviderName ?? Descriptor.ProviderId;

            string capsLabel = GetCapabilitiesLabel(Descriptor.Capabilities);
            CapabilitiesText.Text = capsLabel;

            string accessibleName;

            if (!Descriptor.IsConfigured)
            {
                UnconfiguredWarningText.Visibility = Visibility.Visible;
                BulletSeparator.Visibility = Visibility.Collapsed;
                CapabilitiesText.Visibility = Visibility.Collapsed;

                KeyRequiredIndicator.Visibility = Visibility.Visible;
                SelectedIndicator.Visibility = Visibility.Collapsed;
                UnselectedIndicator.Visibility = Visibility.Collapsed;

                RootGrid.Opacity = 0.65;
                VisualStateManager.GoToState(this, "Normal", false);
                accessibleName = $"{Descriptor.DisplayName}, {Descriptor.ProviderName}, API key required";
            }
            else if (IsCurrent)
            {
                UnconfiguredWarningText.Visibility = Visibility.Collapsed;
                BulletSeparator.Visibility = Visibility.Visible;
                CapabilitiesText.Visibility = Visibility.Visible;

                SelectedIndicator.Visibility = Visibility.Visible;
                UnselectedIndicator.Visibility = Visibility.Collapsed;
                KeyRequiredIndicator.Visibility = Visibility.Collapsed;

                RootGrid.Opacity = 1.0;
                VisualStateManager.GoToState(this, "Selected", false);
                accessibleName = $"{Descriptor.DisplayName}, {Descriptor.ProviderName}, selected";
            }
            else
            {
                UnconfiguredWarningText.Visibility = Visibility.Collapsed;
                BulletSeparator.Visibility = Visibility.Visible;
                CapabilitiesText.Visibility = Visibility.Visible;

                SelectedIndicator.Visibility = Visibility.Collapsed;
                UnselectedIndicator.Visibility = Visibility.Visible;
                KeyRequiredIndicator.Visibility = Visibility.Collapsed;

                RootGrid.Opacity = 1.0;
                VisualStateManager.GoToState(this, "Normal", false);
                accessibleName = $"{Descriptor.DisplayName}, {Descriptor.ProviderName}, available";
            }

            AutomationProperties.SetName(this, accessibleName);
        }

        private static string GetCapabilitiesLabel(ModelCapabilities caps)
        {
            var parts = new List<string>();
            if (caps.HasFlag(ModelCapabilities.Text)) parts.Add("Text");
            if (caps.HasFlag(ModelCapabilities.Vision)) parts.Add("Vision");
            if (caps.HasFlag(ModelCapabilities.Reasoning)) parts.Add("Reasoning");

            if (parts.Count == 0) return "Text";
            return string.Join(" • ", parts);
        }

        private void SetPressedVisual(bool isPressed)
        {
            if (isPressed)
            {
                VisualStateManager.GoToState(this, IsCurrent ? "SelectedPressed" : "Pressed", true);
            }
            else
            {
                VisualStateManager.GoToState(this, IsCurrent ? "Selected" : "Normal", true);
            }
        }

        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e) => SetPressedVisual(true);
        private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e) => SetPressedVisual(false);
        private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e) => SetPressedVisual(false);
        private void RootGrid_PointerCanceled(object sender, PointerRoutedEventArgs e) => SetPressedVisual(false);
        private void RootGrid_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => SetPressedVisual(false);

        private void RootGrid_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (Descriptor != null)
            {
                ModelTapped?.Invoke(this, Descriptor);
            }
        }
    }
}
