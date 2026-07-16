using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Services
{
    public static class SkinManager
    {
        public const string SkinSpartan = "Spartan";
        public const string SkinModernIE = "ModernIE";

        /// <summary>
        /// 获取指定皮肤的所有颜色定义。
        /// </summary>
        public static SkinColors GetSkinColors(string skinName)
        {
            if (skinName == SkinModernIE)
                return CreateModernIEColors();
            else // 默认 Spartan
                return CreateSpartanColors();
        }

        private static SkinColors CreateSpartanColors()
        {
            return new SkinColors
            {
                ToolbarBackground = new AcrylicBrush
                {
                    BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                    TintColor = Color.FromArgb(0xFF, 0xF0, 0xF0, 0xF0),
                    TintOpacity = 0.8,
                    FallbackColor = Color.FromArgb(0xFF, 0xF0, 0xF0, 0xF0)
                },
                TabActiveBackground = new SolidColorBrush(Colors.White),
                TabInactiveBackground = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
                TabHoverBackground = new SolidColorBrush(Colors.Silver),
                AddressBarBackground = new SolidColorBrush(Colors.White),
                AddressBarBorder = new SolidColorBrush(Colors.LightGray),
                AddressBarFocusBorder = new SolidColorBrush(Colors.DodgerBlue),
                SeparatorBrush = new SolidColorBrush(Colors.LightGray),
                ForegroundBrush = new SolidColorBrush(Colors.Black),
                MutedForegroundBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x66, 0x66, 0x66))
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
            public Brush ToolbarBackground { get; set; }
            public SolidColorBrush TabActiveBackground { get; set; }
            public SolidColorBrush TabInactiveBackground { get; set; }
            public SolidColorBrush TabHoverBackground { get; set; }
            public SolidColorBrush AddressBarBackground { get; set; }
            public SolidColorBrush AddressBarBorder { get; set; }
            public SolidColorBrush AddressBarFocusBorder { get; set; }
            public SolidColorBrush SeparatorBrush { get; set; }
            public SolidColorBrush ForegroundBrush { get; set; }
            public SolidColorBrush MutedForegroundBrush { get; set; }
        }
    }
}