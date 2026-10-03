using System;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WinAI.Services
{
    /// <summary>
    /// Service to manage and toggle Light/Dark themes for WinAI.
    /// Supports persistent theme storage in LocalSettings and immediate live switching.
    /// </summary>
    public sealed class ThemeService
    {
        private static readonly Lazy<ThemeService> _instance = new Lazy<ThemeService>(() => new ThemeService());
        public static ThemeService Instance => _instance.Value;

        private const string ThemeSettingKey = "App_RequestedTheme";
        public event EventHandler<ElementTheme> ThemeChanged;

        private ElementTheme _currentTheme = ElementTheme.Default;
        public ElementTheme CurrentTheme => _currentTheme;

        private ThemeService()
        {
            LoadSavedTheme();
        }

        private void LoadSavedTheme()
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            if (localSettings.Values.TryGetValue(ThemeSettingKey, out object val) && val is string str)
            {
                if (Enum.TryParse(str, out ElementTheme parsed))
                {
                    _currentTheme = parsed;
                }
            }
        }

        public void ApplyTheme(FrameworkElement element = null)
        {
            var target = element ?? Window.Current?.Content as FrameworkElement;
            if (target != null)
            {
                target.RequestedTheme = _currentTheme;
            }
        }

        public void SetTheme(ElementTheme theme, FrameworkElement element = null)
        {
            _currentTheme = theme;
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values[ThemeSettingKey] = theme.ToString();

            var target = element ?? Window.Current?.Content as FrameworkElement;
            if (target != null)
            {
                target.RequestedTheme = theme;
            }

            ThemeChanged?.Invoke(this, theme);
        }

        public ElementTheme ToggleTheme(FrameworkElement element = null)
        {
            var target = element ?? Window.Current?.Content as FrameworkElement;
            ElementTheme nextTheme;

            if (_currentTheme == ElementTheme.Light)
            {
                nextTheme = ElementTheme.Dark;
            }
            else if (_currentTheme == ElementTheme.Dark)
            {
                nextTheme = ElementTheme.Light;
            }
            else
            {
                nextTheme = (Application.Current.RequestedTheme == ApplicationTheme.Dark)
                    ? ElementTheme.Light
                    : ElementTheme.Dark;
            }

            SetTheme(nextTheme, target);
            return nextTheme;
        }
    }
}
