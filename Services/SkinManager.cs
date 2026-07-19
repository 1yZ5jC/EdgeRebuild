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

            Color tabActive = isDark ? Color.FromArgb(0xFF, 0x3D, 0x3D, 0x3D) : Colors.White;
            Color tabInactive = isDark ? Color.FromArgb(0x60, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF);
            Color tabHover = isDark ? Color.FromArgb(0xFF, 0x5A, 0x5A, 0x5A) : Colors.Silver;
            Color addressBg = isDark ? Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E) : Colors.White;
            Color addressBorder = isDark ? Color.FromArgb(0xFF, 0x55, 0x55, 0x55) : Colors.LightGray;
            Color separator = isDark ? Color.FromArgb(0xFF, 0x55, 0x55, 0x55) : Colors.LightGray;
            Color foreground = isDark ? Colors.White : Colors.Black;
            Color muted = isDark ? Color.FromArgb(0xFF, 0xAA, 0xAA, 0xAA) : Color.FromArgb(0xFF, 0x66, 0x66, 0x66);

            return new SkinColors
            {
                ToolbarBackground = toolbarBackground,
                TabActiveBackground = new SolidColorBrush(tabActive),
                TabInactiveBackground = new SolidColorBrush(tabInactive),
                TabHoverBackground = new SolidColorBrush(tabHover),
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