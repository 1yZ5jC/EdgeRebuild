using EdgeRebuild.Services;
using Windows.UI;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Metro
{
    /// <summary>
    /// Metro IE（Windows 8/8.1 全屏浏览器）固定色板。
    /// 与主窗口皮肤（ThemeManager）完全隔离：Metro 窗口始终使用本色板。
    /// </summary>
    public static class MetroColorPalette
    {
        // IE11 chrome：近黑底 + 深灰活动标签 + 强调色描边（不是 Win10 的蓝底填充）
        public static readonly Color ChromeColor = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
        public static readonly Color ToolbarColor = ChromeColor;
        public static readonly Color TabActiveColor = Color.FromArgb(0xFF, 0x33, 0x33, 0x33);
        public static readonly Color TabActiveBorderColor = Color.FromArgb(0xFF, 0x00, 0x78, 0xD7);
        public static readonly Color PanelColor = Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E);
        public static readonly Color MetroBlue = Color.FromArgb(0xFF, 0x00, 0x78, 0xD7);
        public static readonly Color HoverColor = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
        public static readonly Color PressedColor = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
        public static readonly Color BorderColor = Color.FromArgb(0xFF, 0x55, 0x55, 0x55);
        public static readonly Color ForegroundColor = Colors.White;
        public static readonly Color MutedColor = Color.FromArgb(0xFF, 0x8E, 0x8E, 0x8E);

        public static Brush ChromeBrush => new SolidColorBrush(ChromeColor);
        public static Brush ToolbarBrush => new SolidColorBrush(ToolbarColor);
        public static Brush TabActiveBrush => new SolidColorBrush(TabActiveColor);
        public static Brush TabActiveBorderBrush => new SolidColorBrush(TabActiveBorderColor);
        public static Brush PanelBrush => new SolidColorBrush(PanelColor);
        public static Brush AccentBrush => new SolidColorBrush(MetroBlue);
        public static Brush HoverBrush => new SolidColorBrush(HoverColor);
        public static Brush PressedBrush => new SolidColorBrush(PressedColor);
        public static Brush BorderBrush => new SolidColorBrush(BorderColor);
        public static Brush ForegroundBrush => new SolidColorBrush(ForegroundColor);
        public static Brush MutedBrush => new SolidColorBrush(MutedColor);

        /// <summary>
        /// 供 TabManager 使用的皮肤色板（无标签条模式只用到内容容器背景）。
        /// </summary>
        public static SkinManager.SkinColors GetSkinColors()
        {
            return new SkinManager.SkinColors
            {
                ToolbarBackground = ChromeBrush,
                TabActiveBackground = new SolidColorBrush(TabActiveColor),
                TabInactiveBackground = ChromeBrush,
                TabHoverBackground = new SolidColorBrush(Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A)),
                AccentBrush = new SolidColorBrush(MetroBlue),
                AddressBarBackground = new SolidColorBrush(Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F)),
                AddressBarBorder = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                AddressBarFocusBorder = new SolidColorBrush(MetroBlue),
                SeparatorBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x33, 0x33)),
                ForegroundBrush = new SolidColorBrush(ForegroundColor),
                MutedForegroundBrush = new SolidColorBrush(MutedColor)
            };
        }
    }
}
