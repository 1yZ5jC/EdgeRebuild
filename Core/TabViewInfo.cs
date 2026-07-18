using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Core
{
    public class TabItemInfo
    {
        public IBrowserTab Tab { get; set; }
        public TextBlock TitleText { get; set; }
        public Button CloseButton { get; set; }
        public Border Container { get; set; }
        public Image FaviconImage { get; set; }
        public FontIcon FaviconPlaceholder { get; set; }
        public TextBlock EngineMark { get; set; }
        public TextBlock SuspendedIndicator { get; set; }
        public bool IsSuspended { get; set; }

        // 新增：引用对应的 WinUI TabViewItem，以便动态更新标题等
        public Microsoft.UI.Xaml.Controls.TabViewItem TabViewItem { get; set; }
    }
}