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
        public Image FaviconImage { get; set; }
        public FontIcon FaviconPlaceholder { get; set; }
        public TextBlock EngineMark { get; set; }
    }
}
