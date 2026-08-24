using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Core
{
    public class TabViewItem
    {
        public IBrowserTab Tab { get; set; }
        public TextBlock TitleText { get; set; }
        public Button CloseButton { get; set; }
        public Border Container { get; set; }
        public Border ContentContainer { get; set; }
        public Image FaviconImage { get; set; }
        public FontIcon FaviconPlaceholder { get; set; }
        public TextBlock EngineMark { get; set; }
        public TextBlock SuspendedIndicator { get; set; }
        public bool IsSuspended { get; set; }
        public ProgressRing LoadingRing { get; set; }
        public int Id { get; set; }

        // 固定标签支持
        public bool IsPinned { get; set; }
        public string OriginalTitle { get; set; }

        // 选中标签顶部强调条（与背景/边框一起随选中状态切换）
        public Border TabAccentBar { get; set; }

        // 后台音频播放指示（喇叭图标，WebView2 桥上报；主窗口标签条专用）
        public FontIcon AudioIndicator { get; set; }
    }
}