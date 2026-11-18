using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Core
{
    public class TabViewItem
    {
        public IBrowserTab Tab { get; set; } = null!;
        public TextBlock TitleText { get; set; } = null!;
        public Button CloseButton { get; set; } = null!;
        public Border Container { get; set; } = null!;
        public Border ContentContainer { get; set; } = null!;
        public Image FaviconImage { get; set; } = null!;
        public FontIcon FaviconPlaceholder { get; set; } = null!;
        public TextBlock EngineMark { get; set; } = null!;
        public TextBlock SuspendedIndicator { get; set; } = null!;
        public bool IsSuspended { get; set; }
        public ProgressRing LoadingRing { get; set; } = null!;
        public int Id { get; set; }

        // 固定标签支持
        public bool IsPinned { get; set; }
        public string? OriginalTitle { get; set; }

        // ---- 会话懒恢复 ----
        // LazyUrl：待首次激活时再导航的地址（激活后清空）。
        // FallbackUrl/FallbackTitle：懒标签尚未产生真实 URL/标题时的显示与会话保存兜底。
        public string? LazyUrl { get; set; }
        public string? FallbackUrl { get; set; }
        public string? FallbackTitle { get; set; }

        // 选中标签顶部强调条（与背景/边框一起随选中状态切换）
        public Border TabAccentBar { get; set; } = null!;

        // 后台音频播放指示（喇叭图标，WebView2 桥上报；主窗口标签条专用）
        public FontIcon AudioIndicator { get; set; } = null!;
    }
}