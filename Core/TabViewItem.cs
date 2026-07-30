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
    }
}