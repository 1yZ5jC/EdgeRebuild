using EdgeRebuild.Core;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
namespace EdgeRebuild
{
    public sealed partial class MainPage : Page
    {
        private IBrowserTab _previousTab;
        public TabManager TabManager { get; } = new TabManager();

        public MainPage()
        {
            this.InitializeComponent();
            TabManager.CurrentTabChanged += OnCurrentTabChanged;

            // 创建初始标签（默认 EdgeHTML）
            var firstTab = CreateTab(EngineType.EdgeHtml);
            TabManager.AddTab(firstTab);
            firstTab.Navigate("about:blank"); // 第一个标签直接导航（此时已设为当前标签）
        }

        private async void OnCurrentTabChanged(IBrowserTab tab)
        {
            // 取消旧标签的事件订阅
            if (_previousTab != null)
            {
                _previousTab.CanGoBackChanged -= OnCanGoBackChanged;
                _previousTab.CanGoForwardChanged -= OnCanGoForwardChanged;
            }
            _previousTab = tab;

            if (tab != null)
            {
                tab.CanGoBackChanged += OnCanGoBackChanged;
                tab.CanGoForwardChanged += OnCanGoForwardChanged;

                ContentContainer.Child = tab.ViewElement;

                // 如果切换到 WebView2 标签，确保其内核已初始化
                if (tab is WebView2Tab wv2Tab)
                    await wv2Tab.EnsureInitializedAsync();

                UrlTextBox.Text = tab.CurrentUrl;
                BackButton.IsEnabled = tab.CanGoBack;
                ForwardButton.IsEnabled = tab.CanGoForward;
            }
            else
            {
                ContentContainer.Child = null;
                UrlTextBox.Text = "";
                BackButton.IsEnabled = false;
                ForwardButton.IsEnabled = false;
            }
        }

        private async void OnCanGoBackChanged(bool canGoBack)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => BackButton.IsEnabled = canGoBack);
        }

        private async void OnCanGoForwardChanged(bool canGoForward)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ForwardButton.IsEnabled = canGoForward);
        }

        private IBrowserTab CreateTab(EngineType engine)
        {
            IBrowserTab tab = engine == EngineType.WebView2 ? new WebView2Tab() : new EdgeHtmlTab();
            tab.UrlChanged += (url) =>
            {
                if (TabManager.CurrentTab == tab)
                    UrlTextBox.Text = url;
            };
            return tab;
        }

        private void NewTabButton_Click(object sender, RoutedEventArgs e)
        {
            EngineType engine = EngineComboBox.SelectedIndex == 1 ? EngineType.WebView2 : EngineType.EdgeHtml;
            var newTab = CreateTab(engine);
            TabManager.AddTab(newTab);     // 这会触发 CurrentTabChanged，在 OnCurrentTabChanged 中完成了初始化

            // 安全导航：如果已经是 WebView2 且 CoreWebView2 可能未就绪，则使用 async void 无碍
            newTab.Navigate("about:blank");
        }

        private void CloseTabButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is IBrowserTab tab)
                TabManager.CloseTab(tab);
        }

        private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
                TabManager.CurrentTab?.Navigate(UrlTextBox.Text);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (TabManager.CurrentTab?.CanGoBack == true)
                TabManager.CurrentTab.GoBack();
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (TabManager.CurrentTab?.CanGoForward == true)
                TabManager.CurrentTab.GoForward();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => TabManager.CurrentTab?.Refresh();
    }

    public enum EngineType
    {
        EdgeHtml,
        WebView2
    }
}