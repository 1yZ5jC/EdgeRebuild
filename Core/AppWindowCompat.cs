using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.WindowManagement;

namespace EdgeRebuild.Core
{
    /// <summary>
    /// AppWindow（18362+）唯一出口。17763 上类型不存在，调用方必须先检查
    /// PlatformCompat.HasAppWindow；该类的所有成员因此统一标注 18362。
    ///
    /// 页面字段/导航参数中不再直接出现 AppWindow 类型，避免 17763 在加载
    /// MainPage 时因字段类型解析失败。
    /// </summary>
    [SupportedOSPlatform("windows10.0.18362.0")]
    internal static class AppWindowCompat
    {
        public static void SetTitle(object appWindow, string title)
        {
            try
            {
                if (appWindow is AppWindow aw)
                    aw.Title = title ?? "";
            }
            catch { }
        }

        public static void ApplyChrome(object appWindow, bool isDark, bool extendContentIntoTitleBar = true)
        {
            try
            {
                if (!(appWindow is AppWindow aw)) return;

                var transparent = Color.FromArgb(0, 0, 0, 0);
                var fg = isDark ? Colors.White : Colors.Black;

                ApplyTitleBarColors(
                    appWindow,
                    extendContentIntoTitleBar: extendContentIntoTitleBar,
                    background: transparent,
                    inactiveBackground: transparent,
                    foreground: fg,
                    buttonBackground: transparent,
                    buttonHoverBackground: isDark
                        ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)
                        : Color.FromArgb(0x30, 0x00, 0x00, 0x00),
                    buttonPressedBackground: isDark
                        ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)
                        : Color.FromArgb(0x40, 0x00, 0x00, 0x00));
            }
            catch { }
        }

        /// <summary>通用标题栏配色（Metro 固定色板等场景）。</summary>
        public static void ApplyTitleBarColors(
            object appWindow,
            bool extendContentIntoTitleBar,
            Color background,
            Color inactiveBackground,
            Color foreground,
            Color buttonBackground,
            Color buttonHoverBackground,
            Color buttonPressedBackground)
        {
            try
            {
                if (!(appWindow is AppWindow aw)) return;

                var tb = aw.TitleBar;
                if (extendContentIntoTitleBar)
                    tb.ExtendsContentIntoTitleBar = true;

                tb.ForegroundColor = foreground;
                tb.BackgroundColor = background;
                tb.InactiveBackgroundColor = inactiveBackground;
                tb.ButtonForegroundColor = foreground;
                tb.ButtonBackgroundColor = buttonBackground;
                tb.ButtonInactiveBackgroundColor = inactiveBackground;
                tb.ButtonHoverBackgroundColor = buttonHoverBackground;
                tb.ButtonPressedBackgroundColor = buttonPressedBackground;
            }
            catch { }
        }

        public static async Task<bool> TryShowAsync(object appWindow)
        {
            try
            {
                return appWindow is AppWindow aw && await aw.TryShowAsync();
            }
            catch
            {
                return false;
            }
        }

        public static bool TrySetFullScreen(object appWindow, bool fullScreen)
        {
            try
            {
                if (!(appWindow is AppWindow aw)) return false;

                return aw.Presenter.RequestPresentation(fullScreen
                    ? AppWindowPresentationKind.FullScreen
                    : AppWindowPresentationKind.Default);
            }
            catch
            {
                return false;
            }
        }
    }
}
