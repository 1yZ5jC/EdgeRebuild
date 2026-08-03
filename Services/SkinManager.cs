using Windows.UI;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Services
{
    public static class SkinManager
    {
        public const string SkinSpartan = "Spartan";
        public const string SkinModernIE = "ModernIE";

        public static SkinColors GetSkinColors(string skinName, bool isDark)
        {
            if (skinName == SkinModernIE) return CreateModernIEColors();
            return CreateSpartanColors(isDark);
        }

        private static SkinColors CreateSpartanColors(bool isDark)
        {
            Brush toolbarBackground = isDark
                ? new AcrylicBrush
                {
                    BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                    TintColor = Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B),
                    TintOpacity = 0.8,
                    FallbackColor = Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B)
                }
                : new AcrylicBrush
                {
                    BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                    TintColor = Color.FromArgb(0xFF, 0xF0, 0xF0, 0xF0),
                    TintOpacity = 0.8,
                    FallbackColor = Color.FromArgb(0xFF, 0xF0, 0xF0, 0xF0)
                };

            Color tabActive = isDark ? Color.FromArgb(0xFF, 0x5E, 0x5E, 0x62) : Colors.White;
            Color tabHover = isDark ? Color.FromArgb(0xFF, 0x5A, 0x5A, 0x5A) : Colors.Silver;
            Color addressBg = isDark ? Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E) : Colors.White;
            Color addressBorder = isDark ? Color.FromArgb(0xFF, 0x55, 0x55, 0x55) : Colors.LightGray;
            Color separator = isDark ? Color.FromArgb(0xFF, 0x55, 0x55, 0x55) : Colors.LightGray;
            Color foreground = isDark ? Colors.White : Colors.Black;
            Color muted = isDark ? Color.FromArgb(0xFF, 0xAA, 0xAA, 0xAA) : Color.FromArgb(0xFF, 0x66, 0x66, 0x66);

            // 未选中标签：直接复用工具栏亚克力（嵌套 HostBackdrop 亚克力会采样窗口
            // 背后而非下方元素，产生色调差异），选中标签用高对比实色凸出
            // （选中高亮由顶部蓝色强调条 + 明显更亮的背景共同标识）
            Brush tabInactive = toolbarBackground;

            return new SkinColors
            {
                ToolbarBackground = toolbarBackground,
                TabActiveBackground = new SolidColorBrush(tabActive),
                TabInactiveBackground = tabInactive,
                TabHoverBackground = new SolidColorBrush(tabHover),
                AccentBrush = new SolidColorBrush(Colors.DodgerBlue),
                AddressBarBackground = new SolidColorBrush(addressBg),
                AddressBarBorder = new SolidColorBrush(addressBorder),
                AddressBarFocusBorder = new SolidColorBrush(Colors.DodgerBlue),
                SeparatorBrush = new SolidColorBrush(separator),
                ForegroundBrush = new SolidColorBrush(foreground),
                MutedForegroundBrush = new SolidColorBrush(muted)
            };
        }

        private static SkinColors CreateModernIEColors()
        {
            // Windows 8/10 Metro 风格：扁平、纯色、无圆角、深灰蓝标题栏，
            // 选中标签更深 + Metro 蓝（#0078D7）强调条/焦点色
            Color metroBlue = Color.FromArgb(0xFF, 0x00, 0x78, 0xD7);
            return new SkinColors
            {
                ToolbarBackground = new SolidColorBrush(Color.FromArgb(0xFF, 0x2D, 0x2D, 0x30)),
                TabActiveBackground = new SolidColorBrush(Color.FromArgb(0xFF, 0x19, 0x19, 0x1C)),
                TabInactiveBackground = new SolidColorBrush(Color.FromArgb(0xFF, 0x2D, 0x2D, 0x30)),
                TabHoverBackground = new SolidColorBrush(Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3E)),
                AccentBrush = new SolidColorBrush(metroBlue),
                AddressBarBackground = new SolidColorBrush(Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E)),
                AddressBarBorder = new SolidColorBrush(Color.FromArgb(0xFF, 0x55, 0x55, 0x55)),
                AddressBarFocusBorder = new SolidColorBrush(metroBlue),
                SeparatorBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x55, 0x55, 0x55)),
                ForegroundBrush = new SolidColorBrush(Colors.White),
                MutedForegroundBrush = new SolidColorBrush(Colors.Gray)
            };
        }

        public class SkinColors
        {
            public Brush ToolbarBackground;
            public SolidColorBrush TabActiveBackground;
            public Brush TabInactiveBackground;
            public SolidColorBrush TabHoverBackground;
            public SolidColorBrush AccentBrush;
            public SolidColorBrush AddressBarBackground;
            public SolidColorBrush AddressBarBorder;
            public SolidColorBrush AddressBarFocusBorder;
            public SolidColorBrush SeparatorBrush;
            public SolidColorBrush ForegroundBrush;
            public SolidColorBrush MutedForegroundBrush;
        }
    }
}