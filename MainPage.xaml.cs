using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.Core;
using Windows.Devices.Input;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
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
            public Border Container { get; set; }
            public Image FaviconImage { get; set; }
            public FontIcon FaviconPlaceholder { get; set; }
            public TextBlock EngineMark { get; set; }
        }

        private readonly List<TabViewItem> _tabViews = new List<TabViewItem>();
        private IBrowserTab _currentTab;
        private bool _isLoaded;

        private readonly SolidColorBrush _selectedBrush = new SolidColorBrush(Colors.White);
        private readonly SolidColorBrush _unselectedBrush = new SolidColorBrush(Colors.LightGray);
        private readonly SolidColorBrush _hoverBrush = new SolidColorBrush(Colors.Silver);
        private readonly SolidColorBrush _starYellowBrush = new SolidColorBrush(Colors.Gold);
        private readonly SolidColorBrush _starGrayBrush = new SolidColorBrush(Colors.Gray);
        private readonly SolidColorBrush _edgeBlueBrush = new SolidColorBrush(Colors.DodgerBlue);
        private readonly SolidColorBrush _webGreenBrush = new SolidColorBrush(Colors.MediumSeaGreen);

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
            titleBar.ButtonForegroundColor = Colors.Black;

            CreateNewTab(EngineType.EdgeHtml, "about:blank");
        }

        private void CreateNewTab(EngineType engine, string url = null)
        {
            if (!_isLoaded) return;

            IBrowserTab tab = engine == EngineType.WebView2
                ? new WebView2Tab()
                : new EdgeHtmlTab();

            // 标签容器
            var tabBorder = new Border
            {
                Background = _unselectedBrush,
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var tabPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 引擎标记 (字母)
            var engineMark = new TextBlock
            {
                Text = tab.Engine == EngineType.EdgeHtml ? "E" : "W",
                Foreground = tab.Engine == EngineType.EdgeHtml ? _edgeBlueBrush : _webGreenBrush,
                FontSize = 11,
                FontWeight = Windows.UI.Text.FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            // Favicon 占位符
            var faviconPlaceholder = new FontIcon
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Glyph = "\xE774",
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            // 真实 Favicon 图片
            var faviconImage = new Image
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            // 标题按钮
            var titleBtn = new Button
            {
                Content = "新标签页",
                Background = new SolidColorBrush(Colors.Transparent),
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.Black),
                Padding = new Thickness(6, 0, 6, 0),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // 关闭按钮
            var closeBtn = new Button
            {
                Content = "\xE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.DimGray),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Width = 24,
                Height = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };

            // 组装
            tabPanel.Children.Add(engineMark);
            tabPanel.Children.Add(faviconPlaceholder);
            tabPanel.Children.Add(faviconImage);
            tabPanel.Children.Add(titleBtn);
            tabPanel.Children.Add(closeBtn);
            tabBorder.Child = tabPanel;
            TabBarPanel.Children.Add(tabBorder);

            var viewItem = new TabViewItem
            {
                Tab = tab,
                TabButton = titleBtn,
                CloseButton = closeBtn,
                Container = tabBorder,
                FaviconImage = faviconImage,
                FaviconPlaceholder = faviconPlaceholder,
                EngineMark = engineMark
            };
            _tabViews.Add(viewItem);

            // 事件绑定
            titleBtn.Click += (s, ev) => SwitchToTab(viewItem);
            closeBtn.Click += (s, ev) => CloseTab(viewItem);

            // 悬停效果
            tabBorder.PointerEntered += (s, ev) =>
            {
                if (_currentTab != tab)
                    tabBorder.Background = _hoverBrush;
            };
            tabBorder.PointerExited += (s, ev) =>
            {
                if (_currentTab != tab)
                    tabBorder.Background = _unselectedBrush;
            };

            tab.TitleChanged += (title) =>
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (titleBtn != null) titleBtn.Content = title;
                });
            };

            tab.UrlChanged += (url) =>
            {
                if (_currentTab == tab)
                {
                    UrlBox.Text = url;
                    UpdateStarButton();
                }
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

            // 引擎指示
            if (_currentTab.Engine == EngineType.EdgeHtml)
            {
                EngineLabel.Text = "E";
                EngineLabel.Foreground = _edgeBlueBrush;
            }
            else
            {
                EngineLabel.Text = "W";
                EngineLabel.Foreground = _webGreenBrush;
            }

            // 更新标签背景
            foreach (var t in _tabViews)
            {
                t.Container.Background = t == viewItem ? _selectedBrush : _unselectedBrush;
            }

            UpdateStarButton();
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

        // ========== 收藏与面板 ==========

        private void UpdateStarButton()
        {
            if (_currentTab == null) return;
            bool exists = FavoritesManager.Instance.ContainsUrl(_currentTab.CurrentUrl);
            if (exists)
            {
                AddFavBtn.Content = "\xE735"; // 实心星
                AddFavBtn.Foreground = _starYellowBrush;
            }
            else
            {
                AddFavBtn.Content = "\xE734"; // 空心星
                AddFavBtn.Foreground = _starGrayBrush;
            }
        }

        private void AddFavBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab == null || string.IsNullOrEmpty(_currentTab.CurrentUrl))
                return;

            string url = _currentTab.CurrentUrl;
            if (FavoritesManager.Instance.ContainsUrl(url))
            {
                var item = FavoritesManager.Instance.Favorites.FirstOrDefault(f => f.Url == url);
                if (item != null)
                    FavoritesManager.Instance.Remove(item);
            }
            else
            {
                string title = !string.IsNullOrEmpty(_currentTab.Title) ? _currentTab.Title : _currentTab.CurrentUrl;
                FavoritesManager.Instance.Add(title, url);
            }
            UpdateStarButton();
            if (HubSplitView.IsPaneOpen)
                RefreshHubPanel();
        }

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

        private void RefreshHubPanel()
        {
            HubFavStackPanel.Children.Clear();
            var favorites = FavoritesManager.Instance.Favorites;
            foreach (var fav in favorites)
            {
                var stack = new StackPanel
                {
                    Margin = new Thickness(4, 6, 4, 6),
                    Padding = new Thickness(8),
                    Background = new SolidColorBrush(Colors.Transparent),
                    Tag = fav
                };

                stack.PointerEntered += (s, e) => stack.Background = new SolidColorBrush(Colors.LightGray);
                stack.PointerExited += (s, e) => stack.Background = new SolidColorBrush(Colors.Transparent);

                stack.Children.Add(new TextBlock
                {
                    Text = fav.Title,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Colors.Black)
                });
                stack.Children.Add(new TextBlock
                {
                    Text = fav.Url,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.DimGray),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                // 右键菜单
                stack.RightTapped += (sender, e) =>
                {
                    var flyout = new MenuFlyout();
                    var editItem = new MenuFlyoutItem { Text = "编辑" };
                    var deleteItem = new MenuFlyoutItem { Text = "删除" };

                    editItem.Click += async (s, args) =>
                    {
                        var titleBox = new TextBox { Text = fav.Title, PlaceholderText = "标题" };
                        var urlBox = new TextBox { Text = fav.Url, PlaceholderText = "网址" };
                        var panel = new StackPanel();
                        panel.Children.Add(titleBox);
                        panel.Children.Add(urlBox);

                        var dialog = new ContentDialog
                        {
                            Title = "编辑收藏",
                            Content = panel,
                            PrimaryButtonText = "保存",
                            SecondaryButtonText = "取消"
                        };

                        var result = await dialog.ShowAsync();
                        if (result == ContentDialogResult.Primary)
                        {
                            fav.Title = titleBox.Text;
                            fav.Url = urlBox.Text;
                            RefreshHubPanel();
                            UpdateStarButton();
                        }
                    };

                    deleteItem.Click += (s, args) =>
                    {
                        FavoritesManager.Instance.Remove(fav);
                        RefreshHubPanel();
                        UpdateStarButton();
                    };

                    flyout.Items.Add(editItem);
                    flyout.Items.Add(deleteItem);
                    flyout.ShowAt(stack);
                };

                // 左键导航
                stack.PointerPressed += (s, e) =>
                {
                    if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse ||
                        e.GetCurrentPoint(stack).Properties.IsLeftButtonPressed)
                    {
                        _currentTab?.Navigate(fav.Url);
                        HubSplitView.IsPaneOpen = false;
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

        private void UrlBox_GotFocus(object sender, RoutedEventArgs e)
        {
            UrlBox.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD7));
        }

        private void UrlBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UrlBox.BorderBrush = new SolidColorBrush(Colors.LightGray);
        }
    }
}