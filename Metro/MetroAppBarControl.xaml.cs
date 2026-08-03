using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Metro
{
    /// <summary>
    /// 底部命令栏（Windows 8.1 AppBar 形态）：图标+文字平铺全宽，由 MetroPage 唤出/收起。
    /// </summary>
    public sealed partial class MetroAppBarControl : UserControl
    {
        public event Action NewTabClicked;
        public event Action TabsClicked;
        public event Action RefreshClicked;
        public event Action FullscreenClicked;
        public event Action SettingsClicked;
        public event Action MoreClicked;

        public MetroAppBarControl()
        {
            InitializeComponent();
        }

        public void UpdateTabCount(int count) => TabCountText.Text = count.ToString();

        public void SetFullscreenText(bool fullscreen) => FullscreenText.Text = fullscreen ? "退出全屏" : "全屏";

        private void NewTabBtn_Click(object sender, RoutedEventArgs e) => NewTabClicked?.Invoke();
        private void TabsBtn_Click(object sender, RoutedEventArgs e) => TabsClicked?.Invoke();
        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshClicked?.Invoke();
        private void FullscreenBtn_Click(object sender, RoutedEventArgs e) => FullscreenClicked?.Invoke();
        private void SettingsBtn_Click(object sender, RoutedEventArgs e) => SettingsClicked?.Invoke();
        private void MoreBtn_Click(object sender, RoutedEventArgs e) => MoreClicked?.Invoke();
    }
}
