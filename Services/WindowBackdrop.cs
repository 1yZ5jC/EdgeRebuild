using Windows.Foundation.Metadata;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Services
{
    /// <summary>
    /// 窗口级 Backdrop 工厂（17763 可用）。
    ///
    /// 17763 自带 AcrylicBrush（16299+），不需要 WinUI 3 的 SystemBackdrop/Mica。
    /// 高对比度、关闭“透明效果”、旧系统下退化为实色，避免半透明但无模糊的脏底色。
    /// 每次调用都在当前 UI 线程新建 Brush（DependencyObject 有线程亲和性）。
    /// </summary>
    public static class WindowBackdrop
    {
        public static Brush Create(bool isDark)
        {
            Color tint = isDark
                ? Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B)
                : Color.FromArgb(0xFF, 0xF0, 0xF0, 0xF0);

            bool useSolid = !ApiInformation.IsTypePresent("Windows.UI.Xaml.Media.AcrylicBrush");
            if (!useSolid)
            {
                try
                {
                    var ui = new UISettings();
                    var accessibility = new AccessibilitySettings();
                    useSolid = accessibility.HighContrast || !ui.AdvancedEffectsEnabled;
                }
                catch
                {
                    // 取不到系统无障碍/透明效果状态时，保守使用实色
                    useSolid = true;
                }
            }

            if (useSolid)
                return new SolidColorBrush(tint);

            return new AcrylicBrush
            {
                BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                TintColor = tint,
                TintOpacity = 0.8,
                FallbackColor = tint
            };
        }
    }
}
