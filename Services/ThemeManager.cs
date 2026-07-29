using System;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Services
{
    public static class ThemeManager
    {
        private static string _currentSkin = "Spartan";
        private static bool _isDarkTheme = false;
        private static SkinManager.SkinColors _currentColors;
        private static UISettings _uiSettings;

        public static event Action<SkinManager.SkinColors, bool> ThemeChanged;

        public static SkinManager.SkinColors Colors => _currentColors;
        public static bool IsDarkTheme => _isDarkTheme;
        public static string CurrentSkin => _currentSkin;

        public static void Initialize()
        {
            _isDarkTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark;
            _currentSkin = Task.Run(async () => await SettingsManager.GetAsync("Skin") ?? "Spartan").Result;
            ApplyTheme();

            SettingsManager.SettingChanged += OnSettingChanged;

            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        }

        private static void OnColorValuesChanged(UISettings sender, object args)
        {
            Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                () =>
                {
                    bool newDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
                    if (_isDarkTheme != newDark)
                    {
                        _isDarkTheme = newDark;
                        ApplyTheme();
                    }
                });
        }

        private static async void OnSettingChanged(string key, string value)
        {
            if (key == "Skin")
            {
                _currentSkin = value;
                ApplyTheme();
            }
        }

        public static void RefreshTheme()
        {
            _isDarkTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark;
            ApplyTheme();
        }

        private static void ApplyTheme()
        {
            _currentColors = SkinManager.GetSkinColors(_currentSkin, _isDarkTheme);
            UpdateTitleBarColors();
            ThemeChanged?.Invoke(_currentColors, _isDarkTheme);
        }

        public static void UpdateTitleBarColors()
        {
            var titleBar = ApplicationView.GetForCurrentView().TitleBar;

            // 前景色：使用 Color.FromArgb 避免任何命名冲突
            titleBar.ButtonForegroundColor = _isDarkTheme
                ? Color.FromArgb(255, 255, 255, 255)
                : Color.FromArgb(255, 0, 0, 0);

            // 透明颜色：使用 Color.FromArgb(0, 0, 0, 0) 表示完全透明
            var transparent = Color.FromArgb(0, 0, 0, 0);

            // 背景色透明，让亚克力透出
            titleBar.BackgroundColor = transparent;
            titleBar.InactiveBackgroundColor = transparent;

            // 按钮背景透明
            titleBar.ButtonBackgroundColor = transparent;
            titleBar.ButtonInactiveBackgroundColor = transparent;

            // 悬停和按下效果：半透明，根据主题调整
            if (_isDarkTheme)
            {
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
            }
            else
            {
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x30, 0x00, 0x00, 0x00);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x40, 0x00, 0x00, 0x00);
            }
        }

        // ---- 便利访问方法 ----
        public static Brush GetToolbarBackground() => _currentColors?.ToolbarBackground;
        public static SolidColorBrush GetForegroundBrush() => _currentColors?.ForegroundBrush;
        public static SolidColorBrush GetMutedForegroundBrush() => _currentColors?.MutedForegroundBrush;
        public static SolidColorBrush GetTabActiveBackground() => _currentColors?.TabActiveBackground;
        public static SolidColorBrush GetTabInactiveBackground() => _currentColors?.TabInactiveBackground;
        public static SolidColorBrush GetTabHoverBackground() => _currentColors?.TabHoverBackground;
        public static SolidColorBrush GetSeparatorBrush() => _currentColors?.SeparatorBrush;
        public static SolidColorBrush GetAddressBarBackground() => _currentColors?.AddressBarBackground;
        public static SolidColorBrush GetAddressBarBorder() => _currentColors?.AddressBarBorder;
        public static SolidColorBrush GetAddressBarFocusBorder() => _currentColors?.AddressBarFocusBorder;
    }
}