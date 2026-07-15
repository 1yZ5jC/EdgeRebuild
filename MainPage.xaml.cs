using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
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

            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
            Window.Current.SetTitleBar(TabBarBorder);
            var titleBar = ApplicationView.GetForCurrentView().TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            CreateNewTab(EngineType.EdgeHtml, "about:blank");
        }

        private void CreateNewTab(EngineType engine, string url = null)
        {
            if (!_isLoaded) return;

            IBrowserTab tab = engine == EngineType.WebView2
                ? new WebView2Tab()
                : new EdgeHtmlTab();

            var tabPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(1, 0, 1, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = _unselectedBrush
            };

            var engineMark = new TextBlock
            {
                Text = tab.EngineIcon,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };

            var faviconPlaceholder = new FontIcon
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Glyph = "\xE774",
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var faviconImage = new Image
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            var titleBtn = new Button
            {
                Content = "新标签页",
                Background = _unselectedBrush,
                FontSize = 10,
                Padding = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                BorderThickness = new Thickness(0)
            };

            var closeBtn = new Button
            {
                Content = "✕",
                FontSize = 10,
                Padding = new Thickness(2, 0, 2, 0),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                BorderThickness = new Thickness(0)
            };

            tabPanel.Children.Add(engineMark);
            tabPanel.Children.Add(faviconPlaceholder);
            tabPanel.Children.Add(faviconImage);
            tabPanel.Children.Add(titleBtn);
            tabPanel.Children.Add(closeBtn);
            TabBarPanel.Children.Add(tabPanel);

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

            titleBtn.Click += (s, ev) => SwitchToTab(viewItem);
            closeBtn.Click += (s, ev) => CloseTab(viewItem);

            tab.TitleChanged += (title) =>
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (titleBtn != null) titleBtn.Content = title;
                });
            };

            tab.UrlChanged += (url) =>
            {
                if (_currentTab == tab) UrlBox.Text = url;
            };
            tab.CanGoBackChanged += (can) =>
            {
                if (_currentTab == tab) BackBtn.IsEnabled = can;
            };
            tab.CanGoForwardChanged += (can) =>
            {
                if (_currentTab == tab) ForwardBtn.IsEnabled = can;
            };
            tab.FaviconChanged += (faviconUrl) => UpdateFavicon(viewItem, faviconUrl);

            SwitchToTab(viewItem);

            if (!string.IsNullOrEmpty(url))
                tab.Navigate(url);
            else if (string.IsNullOrEmpty(tab.CurrentUrl))
                tab.Navigate("about:blank");
        }

        private async void UpdateFavicon(TabViewItem viewItem, string faviconUrl)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (string.IsNullOrEmpty(faviconUrl) ||
                    !Uri.TryCreate(faviconUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != "http" && uri.Scheme != "https"))
                {
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
                    viewItem.FaviconImage.Visibility = Visibility.Collapsed;
                    viewItem.FaviconPlaceholder.Visibility = Visibility.Visible;
                }
            });
        }

        private async void SwitchToTab(TabViewItem viewItem)
        {
            if (!_isLoaded || _currentTab == viewItem.Tab) return;

            ContentContainer.Child = null;
            _currentTab = viewItem.Tab;
            ContentContainer.Child = _currentTab.ViewElement;

            if (_currentTab is WebView2Tab wv2Tab)
                await wv2Tab.EnsureInitializedAsync();

            UrlBox.Text = _currentTab.CurrentUrl;
            BackBtn.IsEnabled = _currentTab.CanGoBack;
            ForwardBtn.IsEnabled = _currentTab.CanGoForward;
            EngineLabel.Text = _currentTab.EngineIcon;

            foreach (var t in _tabViews)
            {
                t.TabButton.Background = t == viewItem ? _selectedBrush : _unselectedBrush;
                t.Container.Background = t == viewItem ? _selectedBrush : _unselectedBrush;
            }
        }

        private void CloseTab(TabViewItem viewItem)
        {
            if (!_isLoaded) return;

            int index = _tabViews.IndexOf(viewItem);
            if (index < 0) return;

            TabViewItem nextTab = null;
            if (_tabViews.Count > 1)
            {
                if (index > 0) nextTab = _tabViews[index - 1];
                else nextTab = _tabViews[1];
            }

            TabBarPanel.Children.Remove(viewItem.Container);
            _tabViews.RemoveAt(index);

            if (_currentTab == viewItem.Tab)
            {
                if (nextTab != null) SwitchToTab(nextTab);
                else _currentTab = null;
            }

            viewItem.Tab.Dispose();

            if (_tabViews.Count == 0)
                CreateNewTab(EngineType.EdgeHtml, "about:blank");
        }

        // ========== Hub 面板 ==========
        private void HubBtn_Click(object sender, RoutedEventArgs e)
        {
            HubSplitView.IsPaneOpen = !HubSplitView.IsPaneOpen;
            if (HubSplitView.IsPaneOpen)
                RefreshHubPanel();
        }

        private void CloseHubBtn_Click(object sender, RoutedEventArgs e)
        {
            HubSplitView.IsPaneOpen = false;
        }

        private void HubSplitView_PaneClosed(SplitView sender, object args)
        {
            // 可选，面板关闭时清理
        }

        private void RefreshHubPanel()
        {
            HubFavStackPanel.Children.Clear();
            var favorites = FavoritesManager.Instance.Favorites;
            foreach (var fav in favorites)
            {
                var stack = new StackPanel
                {
                    Margin = new Thickness(4, 8, 4, 8),
                    Tag = fav.Url
                };
                stack.Children.Add(new TextBlock
                {
                    Text = fav.Title,
                    FontWeight = Windows.UI.Text.FontWeights.Bold,
                    FontSize = 14
                });
                stack.Children.Add(new TextBlock
                {
                    Text = fav.Url,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                stack.PointerPressed += (s, e) =>
                {
                    if (s is StackPanel sp && sp.Tag is string url)
                    {
                        _currentTab?.Navigate(url);
                        HubSplitView.IsPaneOpen = false; // 自动关闭
                    }
                };
                HubFavStackPanel.Children.Add(stack);
            }
        }

        // ========== 其他事件 ==========
        private void NewTabBtn_Click(object sender, RoutedEventArgs e)
        {
            var engine = EngineCombo.SelectedIndex == 1 ? EngineType.WebView2 : EngineType.EdgeHtml;
            CreateNewTab(engine, "about:blank");
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab?.CanGoBack == true) _currentTab.GoBack();
        }

        private void ForwardBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab?.CanGoForward == true) _currentTab.GoForward();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            _currentTab?.Refresh();
        }

        private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                string input = UrlBox.Text?.Trim();
                if (!string.IsNullOrEmpty(input))
                {
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

        private void AddFavBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab != null && !string.IsNullOrEmpty(_currentTab.CurrentUrl))
            {
                string title = !string.IsNullOrEmpty(_currentTab.Title) ? _currentTab.Title : _currentTab.CurrentUrl;
                FavoritesManager.Instance.Add(title, _currentTab.CurrentUrl);
            }
        }
    }
}