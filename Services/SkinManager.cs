using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Services
{
    public static class SkinManager
    {
        public const string SkinSpartan = "Spartan";
        public const string SkinModernIE = "ModernIE";

        public static SkinColors GetSkinColors(string skinName)
        {
            if (skinName == SkinModernIE) return CreateModernIEColors();
            return CreateSpartanColors();
        }

        private static SkinColors CreateSpartanColors()
        {
            bool isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;

            // 根据系统主题选择颜色
            var toolbarBackground = isDark
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

            Color tabActiveColor = isDark ? Color.FromArgb(0xFF, 0x3D, 0x3D, 0x3D) : Colors.White;
            Color tabInactiveColor = isDark ? Color.FromArgb(0x60, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF);
            Color tabHoverColor = isDark ? Color.FromArgb(0xFF, 0x5A, 0x5A, 0x5A) : Colors.Silver;
            Color addressBarBgColor = isDark ? Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E) : Colors.White;
            Color addressBarBorderColor = isDark ? Color.FromArgb(0xFF, 0x55, 0x55, 0x55) : Colors.LightGray;
            Color focusBorderColor = Colors.DodgerBlue; // 保持不变
            Color separatorColor = isDark ? Color.FromArgb(0xFF, 0x55, 0x55, 0x55) : Colors.LightGray;
            Color foregroundColor = isDark ? Colors.White : Colors.Black;
            Color mutedForegroundColor = isDark ? Color.FromArgb(0xFF, 0xAA, 0xAA, 0xAA) : Color.FromArgb(0xFF, 0x66, 0x66, 0x66);

            return new SkinColors
            {
                ToolbarBackground = toolbarBackground,
                TabActiveBackground = new SolidColorBrush(tabActiveColor),
                TabInactiveBackground = new SolidColorBrush(tabInactiveColor),
                TabHoverBackground = new SolidColorBrush(tabHoverColor),
                AddressBarBackground = new SolidColorBrush(addressBarBgColor),
                AddressBarBorder = new SolidColorBrush(addressBarBorderColor),
                AddressBarFocusBorder = new SolidColorBrush(focusBorderColor),
                SeparatorBrush = new SolidColorBrush(separatorColor),
                ForegroundBrush = new SolidColorBrush(foregroundColor),
                MutedForegroundBrush = new SolidColorBrush(mutedForegroundColor)
            };
        }

        private static SkinColors CreateModernIEColors()
        {
            // ModernIE 始终保持深色，不随系统变化
            return new SkinColors
            {
                ToolbarBackground = new SolidColorBrush(Color.FromArgb(0xFF, 0x2D, 0x2D, 0x30)),
                TabActiveBackground = new SolidColorBrush(Color.FromArgb(0xFF, 0x3E, 0x3E, 0x40)),
                TabInactiveBackground = new SolidColorBrush(Color.FromArgb(0xFF, 0x2D, 0x2D, 0x30)),
                TabHoverBackground = new SolidColorBrush(Color.FromArgb(0xFF, 0x4E, 0x4E, 0x50)),
                AddressBarBackground = new SolidColorBrush(Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E)),
                AddressBarBorder = new SolidColorBrush(Color.FromArgb(0xFF, 0x55, 0x55, 0x55)),
                AddressBarFocusBorder = new SolidColorBrush(Colors.DodgerBlue),
                SeparatorBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x55, 0x55, 0x55)),
                ForegroundBrush = new SolidColorBrush(Colors.White),
                MutedForegroundBrush = new SolidColorBrush(Colors.Gray)
            };
        }

        public class SkinColors
        {
            public Brush ToolbarBackground;
            public SolidColorBrush TabActiveBackground;
            public SolidColorBrush TabInactiveBackground;
            public SolidColorBrush TabHoverBackground;
            public SolidColorBrush AddressBarBackground;
            public SolidColorBrush AddressBarBorder;
            public SolidColorBrush AddressBarFocusBorder;
            public SolidColorBrush SeparatorBrush;
            public SolidColorBrush ForegroundBrush;
            public SolidColorBrush MutedForegroundBrush;
        }
    }
}