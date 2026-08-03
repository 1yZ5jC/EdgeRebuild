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

        // 画笔是 XAML DependencyObject，具有线程亲和性：
        // 不能跨窗口共享（辅助窗口布局时会抛 RPC_E_WRONG_THREAD），
        // 因此 Colors 每次都在调用线程上新建一套。
        public static SkinManager.SkinColors Colors => SkinManager.GetSkinColors(_currentSkin, _isDarkTheme);
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
        // 每次调用都在当前线程新建画笔，保证多窗口下线程安全
        public static Brush GetToolbarBackground() => Colors.ToolbarBackground;
        public static SolidColorBrush GetForegroundBrush() => Colors.ForegroundBrush;
        public static SolidColorBrush GetMutedForegroundBrush() => Colors.MutedForegroundBrush;
        public static Brush GetTabActiveBackground() => Colors.TabActiveBackground;
        public static Brush GetTabInactiveBackground() => Colors.TabInactiveBackground;
        public static SolidColorBrush GetTabHoverBackground() => Colors.TabHoverBackground;
        public static SolidColorBrush GetSeparatorBrush() => Colors.SeparatorBrush;
        public static SolidColorBrush GetAddressBarBackground() => Colors.AddressBarBackground;
        public static SolidColorBrush GetAddressBarBorder() => Colors.AddressBarBorder;
        public static SolidColorBrush GetAddressBarFocusBorder() => Colors.AddressBarFocusBorder;
    }
}