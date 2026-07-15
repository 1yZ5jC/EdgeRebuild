using System;
using System.Collections.Generic;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using EdgeRebuild.Core;

namespace EdgeRebuild
{
    public sealed partial class MainPage : Page
    {
        // 标签视图项，关联控件
        private class TabViewItem
        {
            public IBrowserTab Tab { get; set; }
            public Button TabButton { get; set; }
            public Button CloseButton { get; set; }
            public Panel Container { get; set; }
            public Image FaviconImage { get; set; }
            public FontIcon FaviconPlaceholder { get; set; }
            public TextBlock EngineMark { get; set; }
        }

        private readonly List<TabViewItem> _tabViews = new List<TabViewItem>();
        private IBrowserTab _currentTab;
        private bool _isLoaded;

        private readonly SolidColorBrush _selectedBrush = new SolidColorBrush(Colors.White);
        private readonly SolidColorBrush _unselectedBrush = new SolidColorBrush(Colors.LightGray);

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            // 创建第一个标签（EdgeHTML，打开空白页）
            CreateNewTab(EngineType.EdgeHtml, "about:blank");
        }

        /// <summary>
        /// 创建新标签并添加到界面
        /// </summary>
        private void CreateNewTab(EngineType engine, string url = null)
        {
            if (!_isLoaded) return;

            // 实例化对应的标签引擎
            IBrowserTab tab = engine == EngineType.WebView2
                ? new WebView2Tab()
                : new EdgeHtmlTab();

            // 构建标签面板（一行）
            var tabPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            // 引擎标记（Emoji）
            var engineMark = new TextBlock
            {
                Text = tab.EngineIcon, // "🌐" 或 "🧬"
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };

            // Favicon 占位符（地球图标，默认可见）
            var faviconPlaceholder = new FontIcon
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Glyph = "\xE774",
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            // 真实 Favicon 图片（默认隐藏，获取到 favicon 后显示）
            var faviconImage = new Image
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            // 标题按钮（点击可切换到此标签）
            var titleBtn = new Button
            {
                Content = tab.Id.Substring(0, 8), // 初始显示ID前8位
                Background = _unselectedBrush,
                FontSize = 10,
                Padding = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // 关闭按钮
            var closeBtn = new Button
            {
                Content = "✕",
                FontSize = 10,
                Padding = new Thickness(2, 0, 2, 0),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // 将控件按顺序添加到面板
            tabPanel.Children.Add(engineMark);
            tabPanel.Children.Add(faviconPlaceholder);
            tabPanel.Children.Add(faviconImage);
            tabPanel.Children.Add(titleBtn);
            tabPanel.Children.Add(closeBtn);

            // 把整个面板添加到标签栏
            TabBarPanel.Children.Add(tabPanel);

            // 创建视图项记录
            var viewItem = new TabViewItem
            {
                Tab = tab,
                TabButton = titleBtn,
                CloseButton = closeBtn,
                Container = tabPanel,
                FaviconImage = faviconImage,
                FaviconPlaceholder = faviconPlaceholder,
                EngineMark = engineMark
            };
            _tabViews.Add(viewItem);

            // 事件绑定
            titleBtn.Click += (s, ev) => SwitchToTab(viewItem);
            closeBtn.Click += (s, ev) => CloseTab(viewItem);

            // 标题更新（确保在 UI 线程）
            tab.TitleChanged += (title) =>
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (titleBtn != null)
                        titleBtn.Content = string.IsNullOrEmpty(title) ? tab.Id : title;
                });
            };

            // URL 变化时更新地址栏
            tab.UrlChanged += (url) =>
            {
                if (_currentTab == tab)
                    UrlBox.Text = url;
            };

            // 导航状态更新按钮
            tab.CanGoBackChanged += (can) =>
            {
                if (_currentTab == tab) BackBtn.IsEnabled = can;
            };
            tab.CanGoForwardChanged += (can) =>
            {
                if (_currentTab == tab) ForwardBtn.IsEnabled = can;
            };

            // Favicon 变化时更新图标
            tab.FaviconChanged += (faviconUrl) => UpdateFavicon(viewItem, faviconUrl);

            // 切换到新标签
            SwitchToTab(viewItem);

            // 导航到指定 URL
            if (!string.IsNullOrEmpty(url))
                tab.Navigate(url);
            else if (string.IsNullOrEmpty(tab.CurrentUrl))
                tab.Navigate("about:blank");
        }

        /// <summary>
        /// 安全更新标签的 Favicon 图标
        /// </summary>
        private async void UpdateFavicon(TabViewItem viewItem, string faviconUrl)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (string.IsNullOrEmpty(faviconUrl) ||
                    !Uri.TryCreate(faviconUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != "http" && uri.Scheme != "https"))
                {
                    // 无效链接：显示占位符，隐藏真实图标
                    viewItem.FaviconPlaceholder.Visibility = Visibility.Visible;
                    viewItem.FaviconImage.Visibility = Visibility.Collapsed;
                    viewItem.FaviconImage.Source = null;
                    return;
                }

                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.UriSource = uri;
                    viewItem.FaviconImage.Source = bitmap;
                    viewItem.FaviconImage.Visibility = Visibility.Visible;
                    viewItem.FaviconPlaceholder.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    // 加载失败，保持占位符
                    viewItem.FaviconImage.Visibility = Visibility.Collapsed;
                    viewItem.FaviconPlaceholder.Visibility = Visibility.Visible;
                }
            });
        }

        /// <summary>
        /// 切换到指定标签
        /// </summary>
        private async void SwitchToTab(TabViewItem viewItem)
        {
            if (!_isLoaded || _currentTab == viewItem.Tab) return;

            // 移除旧内容
            ContentContainer.Child = null;

            _currentTab = viewItem.Tab;
            ContentContainer.Child = _currentTab.ViewElement;

            // 确保 WebView2 初始化完成
            if (_currentTab is WebView2Tab wv2Tab)
                await wv2Tab.EnsureInitializedAsync();

            // 更新地址栏和按钮状态
            UrlBox.Text = _currentTab.CurrentUrl;
            BackBtn.IsEnabled = _currentTab.CanGoBack;
            ForwardBtn.IsEnabled = _currentTab.CanGoForward;
            EngineLabel.Text = _currentTab.EngineIcon; // 引擎 Emoji

            // 高亮当前标签按钮
            foreach (var t in _tabViews)
            {
                t.TabButton.Background = t == viewItem ? _selectedBrush : _unselectedBrush;
            }
        }

        /// <summary>
        /// 关闭标签
        /// </summary>
        private void CloseTab(TabViewItem viewItem)
        {
            if (!_isLoaded) return;

            int index = _tabViews.IndexOf(viewItem);
            if (index < 0) return;

            // 确定下一个要激活的标签
            TabViewItem nextTab = null;
            if (_tabViews.Count > 1)
            {
                if (index > 0) nextTab = _tabViews[index - 1];
                else nextTab = _tabViews[1];
            }

            // 从 UI 移除
            TabBarPanel.Children.Remove(viewItem.Container);
            _tabViews.RemoveAt(index);

            // 如果关闭的是当前标签，切换到下一个
            if (_currentTab == viewItem.Tab)
            {
                if (nextTab != null)
                    SwitchToTab(nextTab);
                else
                    _currentTab = null;
            }

            // 销毁标签资源
            viewItem.Tab.Dispose();

            // 如果没有任何标签了，创建一个新的
            if (_tabViews.Count == 0)
                CreateNewTab(EngineType.EdgeHtml, "about:blank");
        }

        // 新建标签按钮
        private void NewTabBtn_Click(object sender, RoutedEventArgs e)
        {
            var engine = EngineCombo.SelectedIndex == 1 ? EngineType.WebView2 : EngineType.EdgeHtml;
            CreateNewTab(engine, "about:blank");
        }

        // 后退
        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab?.CanGoBack == true) _currentTab.GoBack();
        }

        // 前进
        private void ForwardBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab?.CanGoForward == true) _currentTab.GoForward();
        }

        // 刷新
        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            _currentTab?.Refresh();
        }

        // 地址栏回车导航
        private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                string input = UrlBox.Text?.Trim();
                if (!string.IsNullOrEmpty(input))
                {
                    // 自动补全 https://
                    if (!input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                        !input.StartsWith("about:", StringComparison.OrdinalIgnoreCase) &&
                        !input.StartsWith("edge:", StringComparison.OrdinalIgnoreCase) &&
                        !input.Contains("://"))
                    {
                        input = "https://" + input;
                    }
                }
                _currentTab?.Navigate(input);
            }
        }
    }
}