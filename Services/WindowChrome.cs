using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;

namespace EdgeRebuild.Services
{
    /// <summary>
    /// 17763 可用的窗口标题栏方案。
    ///
    /// 主窗口与 CoreApplication.CreateNewView 原生副窗口各自拥有独立的
    /// CoreApplicationView / ApplicationView / Window.Current，因此这套 API
    /// 在每个窗口的 UI 线程上调用即可，无需 AppWindow。
    ///
    /// 标题栏背景由 XAML 侧的 AcrylicBrush/实色 Border 提供；这里只负责
    /// 内容扩展进标题栏、拖拽区与系统按钮配色（全部为 10240+ API）。
    /// </summary>
    public static class WindowChrome
    {
        public static void ApplyLegacy(FrameworkElement dragArea, bool isDark)
        {
            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
            Window.Current.SetTitleBar(dragArea);
            ApplyApplicationViewTitleBarColors(isDark);
        }

        public static void ApplyApplicationViewTitleBarColors(bool isDark)
        {
            var tb = ApplicationView.GetForCurrentView().TitleBar;
            var transparent = Color.FromArgb(0, 0, 0, 0);
            var fg = isDark ? Colors.White : Colors.Black;

            tb.ForegroundColor = fg;
            tb.BackgroundColor = transparent;
            tb.InactiveBackgroundColor = transparent;
            tb.ButtonForegroundColor = fg;
            tb.ButtonBackgroundColor = transparent;
            tb.ButtonInactiveBackgroundColor = transparent;
            tb.ButtonHoverBackgroundColor = isDark
                ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x30, 0x00, 0x00, 0x00);
            tb.ButtonPressedBackgroundColor = isDark
                ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x40, 0x00, 0x00, 0x00);
        }
    }
}
