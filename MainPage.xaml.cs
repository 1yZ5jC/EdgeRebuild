using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using EdgeRebuild.Core;
using EdgeRebuild.Services;

namespace EdgeRebuild
{
    public sealed partial class MainPage : Page
    {
        private class TabViewItem
        {
            public IBrowserTab Tab { get; set; }
            public TextBlock TitleText { get; set; }
            public Button CloseButton { get; set; }
            public Border Container { get; set; }
            public Image FaviconImage { get; set; }
            public FontIcon FaviconPlaceholder { get; set; }
            public TextBlock EngineMark { get; set; }
        }

        private readonly List<TabViewItem> _tabViews = new List<TabViewItem>();
        private IBrowserTab _currentTab;
        private bool _isLoaded;
        private string _pendingUrl;

        private readonly SolidColorBrush _selectedBrush = new SolidColorBrush(Colors.White);
        private readonly SolidColorBrush _unselectedBrush = new SolidColorBrush(Color.FromArgb(0xB3, 0xE0, 0xE0, 0xE0));
        private readonly SolidColorBrush _hoverBrush = new SolidColorBrush(Colors.Silver);
        private readonly SolidColorBrush _starYellowBrush = new SolidColorBrush(Colors.Gold);
        private readonly SolidColorBrush _starGrayBrush = new SolidColorBrush(Colors.Gray);
        private readonly SolidColorBrush _edgeBlueBrush = new SolidColorBrush(Colors.DodgerBlue);
        private readonly SolidColorBrush _webGreenBrush = new SolidColorBrush(Colors.MediumSeaGreen);

        private const int MinTabWidth = 44;
        private const int MaxTabWidth = 120;
        private const int AdditionalMargin = 30;
        private const int MinDragWidth = 20;
        private const int ButtonBaseOffset = 50;
        private double _rightReserved;

        // Manipulation 拖拽状态
        private TabViewItem _dragItem;
        private int _dragStartIndex;

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;

            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
            Window.Current.SetTitleBar(TitleBarDragArea);
            var titleBar = ApplicationView.GetForCurrentView().TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.Black;

            SetSafeZonePadding();
            UpdateTabLayout();

            Window.Current.SizeChanged += OnWindowSizeChanged;

            if (_tabViews.Count == 0)
            {
                if (!string.IsNullOrEmpty(_pendingUrl))
                {
                    CreateNewTab(EngineType.EdgeHtml, _pendingUrl);
                    _pendingUrl = null;
                }
                else
                {
                    CreateNewTab(EngineType.EdgeHtml, "about:blank");
                }
            }
        }

        private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            SetSafeZonePadding();
            UpdateTabLayout();
        }

        private void SetSafeZonePadding()
        {
            double systemOverlay = 100;
            try
            {
                var bounds = ApplicationView.GetForCurrentView().VisibleBounds;
                var windowBounds = Window.Current.Bounds;
                systemOverlay = windowBounds.Width - bounds.Width;
            }
            catch { }
            if (systemOverlay <= 0) systemOverlay = 100;

            _rightReserved = systemOverlay + AdditionalMargin;
            TabBarBorder.Padding = new Thickness(0, 0, _rightReserved, 0);
        }

        private void TabBarBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SetSafeZonePadding();
            UpdateTabLayout();
        }

        private void UpdateRightPanelMargin()
        {
            RightSidePanel.UpdateLayout();
            double rightFixed = RightSidePanel.ActualWidth + RightSidePanel.Margin.Left + RightSidePanel.Margin.Right;
            double leftFixed = ScrollLeftBtn.Visibility == Visibility.Visible
                ? ScrollLeftBtn.ActualWidth + ScrollLeftBtn.Margin.Left + ScrollLeftBtn.Margin.Right
                : 0;
            double contentWidth = TabBarBorder.ActualWidth - _rightReserved;

            double requiredForDrag = rightFixed + MinDragWidth;
            double extraOffset = Math.Max(0, requiredForDrag - (contentWidth - leftFixed));

            RightSidePanel.Margin = new Thickness(0, 0, ButtonBaseOffset + extraOffset, 0);
            RightSidePanel.UpdateLayout();
        }

        private void UpdateTabLayout()
        {
            if (!_isLoaded || _tabViews.Count == 0) return;

            TabBarBorder.UpdateLayout();

            RightSidePanel.Margin = new Thickness(0, 0, ButtonBaseOffset, 0);
            RightSidePanel.UpdateLayout();

            UpdateRightPanelMargin();

            double leftFixed = ScrollLeftBtn.Visibility == Visibility.Visible
                ? ScrollLeftBtn.ActualWidth + ScrollLeftBtn.Margin.Left + ScrollLeftBtn.Margin.Right
                : 0;
            double rightFixed = RightSidePanel.ActualWidth + RightSidePanel.Margin.Left + RightSidePanel.Margin.Right;
            double contentWidth = TabBarBorder.ActualWidth - _rightReserved;
            double availableWidth = Math.Max(0, contentWidth - leftFixed - rightFixed - MinDragWidth);

            double idealTotal = _tabViews.Count * MaxTabWidth;
            double minTotal = _tabViews.Count * MinTabWidth;

            bool needScroll = false;
            double targetTabWidth = MaxTabWidth;

            if (idealTotal <= availableWidth)
            {
                targetTabWidth = MaxTabWidth;
            }
            else if (minTotal <= availableWidth)
            {
                targetTabWidth = availableWidth / _tabViews.Count;
            }
            else
            {
                targetTabWidth = MinTabWidth;
                needScroll = true;
            }

            foreach (var item in _tabViews)
            {
                item.Container.Width = targetTabWidth;
                double reserved = 60;
                item.TitleText.MaxWidth = Math.Max(0, targetTabWidth - reserved);
            }

            TabScrollViewer.MaxWidth = availableWidth;
            if (!needScroll)
            {
                TabScrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
                ScrollLeftBtn.Visibility = Visibility.Collapsed;
                ScrollRightBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                TabScrollViewer.HorizontalScrollMode = ScrollMode.Enabled;
                ScrollLeftBtn.Visibility = Visibility.Visible;
                ScrollRightBtn.Visibility = Visibility.Visible;
            }

            UpdateRightPanelMargin();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string url)
            {
                _pendingUrl = url;
            }
        }

        private void CreateNewTab(EngineType engine, string url = null)
        {
            if (!_isLoaded) return;

            IBrowserTab tab = engine == EngineType.WebView2
                ? new WebView2Tab()
                : new EdgeHtmlTab();

            var tabBorder = new Border
            {
                Height = 32,
                Background = _unselectedBrush,
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                Width = MaxTabWidth,
                ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY
            };

            var tabPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var engineMark = new TextBlock
            {
                Text = tab.Engine == EngineType.EdgeHtml ? "E" : "W",
                Foreground = tab.Engine == EngineType.EdgeHtml ? _edgeBlueBrush : _webGreenBrush,
                FontSize = 11,
                FontWeight = Windows.UI.Text.FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };

            var faviconPlaceholder = new FontIcon
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Glyph = "\xE774",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var faviconImage = new Image
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            var titleText = new TextBlock
            {
                Text = "新标签页",
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.Black),
                VerticalAlignment = VerticalAlignment.Center
            };

            var closeBtn = new Button
            {
                Content = "\xE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.DimGray),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Width = 20,
                Height = 20,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0)
            };

            tabPanel.Children.Add(engineMark);
            tabPanel.Children.Add(faviconPlaceholder);
            tabPanel.Children.Add(faviconImage);
            tabPanel.Children.Add(titleText);
            tabPanel.Children.Add(closeBtn);
            tabBorder.Child = tabPanel;
            TabBarPanel.Children.Add(tabBorder);

            var viewItem = new TabViewItem
            {
                Tab = tab,
                TitleText = titleText,
                CloseButton = closeBtn,
                Container = tabBorder,
                FaviconImage = faviconImage,
                FaviconPlaceholder = faviconPlaceholder,
                EngineMark = engineMark
            };
            _tabViews.Add(viewItem);

            closeBtn.Click += (s, ev) => CloseTab(viewItem);
            titleText.Tapped += (s, ev) => SwitchToTab(viewItem);

            // Manipulation 事件
            tabBorder.ManipulationStarted += OnTabManipulationStarted;
            tabBorder.ManipulationDelta += OnTabManipulationDelta;
            tabBorder.ManipulationCompleted += OnTabManipulationCompleted;

            tabBorder.PointerEntered += (s, ev) =>
            {
                if (_currentTab != tab) tabBorder.Background = _hoverBrush;
            };
            tabBorder.PointerExited += (s, ev) =>
            {
                if (_currentTab != tab) tabBorder.Background = _unselectedBrush;
            };

            tab.TitleChanged += (title) =>
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    titleText.Text = string.IsNullOrEmpty(title) ? "新标签页" : title;
                });
            };

            tab.UrlChanged += (url) =>
            {
                if (_currentTab == tab)
                {
                    UrlBox.Text = url;
                    UpdateStarButton();
                }
                if (!string.IsNullOrEmpty(url) && url != "about:blank")
                    HistoryManager.Add(tab.Title ?? url, url);
            };
            tab.CanGoBackChanged += (can) => { if (_currentTab == tab) BackBtn.IsEnabled = can; };
            tab.CanGoForwardChanged += (can) => { if (_currentTab == tab) ForwardBtn.IsEnabled = can; };
            tab.FaviconChanged += (faviconUrl) => UpdateFavicon(viewItem, faviconUrl);

            SwitchToTab(viewItem);

            if (!string.IsNullOrEmpty(url))
                tab.Navigate(url);

            UpdateTabLayout();
        }

        // ==================== Manipulation 拖拽实现 ====================
        private void OnTabManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            var viewItem = _tabViews.FirstOrDefault(v => v.Container == border);
            if (viewItem == null) return;

            _dragItem = viewItem;
            _dragStartIndex = _tabViews.IndexOf(viewItem);
            viewItem.Container.Opacity = 0.9;
            e.Handled = true;
        }

        private void OnTabManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (_dragItem == null) return;

            double totalX = e.Cumulative.Translation.X;
            double totalY = e.Cumulative.Translation.Y;

            // 垂直拖出窗口（阈值 30px）
            if (Math.Abs(totalY) > 30 && Math.Abs(totalY) > Math.Abs(totalX))
            {
                var item = _dragItem;
                _dragItem = null;
                item.Container.RenderTransform = null;
                item.Container.Opacity = 1.0;
                MoveTabToNewWindow(item);
                e.Complete();
                return;
            }

            // 水平拖拽排序
            _dragItem.Container.RenderTransform = new TranslateTransform { X = totalX };

            int currentIndex = _tabViews.IndexOf(_dragItem);
            if (currentIndex < 0) return;

            double itemWidth = _dragItem.Container.ActualWidth;
            if (itemWidth <= 0) itemWidth = MaxTabWidth;

            int newIndex = currentIndex;
            if (totalX > itemWidth * 0.5 && currentIndex < _tabViews.Count - 1)
                newIndex = currentIndex + 1;
            else if (totalX < -itemWidth * 0.5 && currentIndex > 0)
                newIndex = currentIndex - 1;

            if (newIndex != currentIndex)
            {
                SwapTabs(currentIndex, newIndex);
                _dragItem.Container.RenderTransform = null;
                _dragStartIndex = newIndex;
                e.Complete();
            }
            e.Handled = true;
        }

        private void OnTabManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (_dragItem != null)
            {
                _dragItem.Container.RenderTransform = null;
                _dragItem.Container.Opacity = 1.0;
                _dragItem = null;
            }
            UpdateTabLayout();
            e.Handled = true;
        }

        private void SwapTabs(int oldIndex, int newIndex)
        {
            if (oldIndex == newIndex) return;
            var item = _tabViews[oldIndex];
            _tabViews.RemoveAt(oldIndex);
            _tabViews.Insert(newIndex, item);

            TabBarPanel.Children.RemoveAt(oldIndex);
            TabBarPanel.Children.Insert(newIndex, item.Container);
        }

        private async void MoveTabToNewWindow(TabViewItem item)
        {
            string url = item.Tab.CurrentUrl;
            CloseTab(item);
            if (_tabViews.Count == 0)
                CreateNewTab(EngineType.EdgeHtml, "about:blank");

            var newView = CoreApplication.CreateNewView();
            int newViewId = 0;
            await newView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                var frame = new Frame();
                frame.Navigate(typeof(MainPage), url);
                Window.Current.Content = frame;
                Window.Current.Activate();
                newViewId = ApplicationView.GetForCurrentView().Id;
            });
            await ApplicationViewSwitcher.TryShowAsStandaloneAsync(newViewId);
        }

        // ==================== 下载事件 ====================
        private void AttachDownloadEvents(WebView2Tab wv2Tab)
        {
            if (wv2Tab.CoreWebView2 == null) return;
            wv2Tab.CoreWebView2.DownloadStarting += (sender, args) =>
            {
                var op = args.DownloadOperation;
                var item = DownloadManager.Add(op.ResultFilePath, op.ResultFilePath, System.IO.Path.GetFileName(op.ResultFilePath));
                op.StateChanged += (s, stateArgs) =>
                {
                    if (op.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Completed)
                    {
                        item.Status = "已完成";
                        item.Progress = 100;
                    }
                    else if (op.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Interrupted)
                    {
                        item.Status = "已中断";
                    }
                    RefreshDownloadsIfOpen();
                };
                RefreshDownloadsIfOpen();
            };
        }

        private void RefreshDownloadsIfOpen()
        {
            if (HubSplitView.IsPaneOpen)
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => RefreshDownloadsPanel());
            }
        }

        // ==================== 标签切换/关闭 ====================
        private async void SwitchToTab(TabViewItem viewItem)
        {
            if (!_isLoaded || _currentTab == viewItem.Tab) return;
            ContentContainer.Child = null;
            _currentTab = viewItem.Tab;
            ContentContainer.Child = _currentTab.ViewElement;

            if (_currentTab is WebView2Tab wv2Tab)
            {
                await wv2Tab.EnsureInitializedAsync();
                AttachDownloadEvents(wv2Tab);
            }

            UrlBox.Text = _currentTab.CurrentUrl;
            BackBtn.IsEnabled = _currentTab.CanGoBack;
            ForwardBtn.IsEnabled = _currentTab.CanGoForward;

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

            foreach (var t in _tabViews)
                t.Container.Background = t == viewItem ? _selectedBrush : _unselectedBrush;

            UpdateStarButton();
        }

        private void CloseTab(TabViewItem viewItem)
        {
            if (!_isLoaded) return;
            int index = _tabViews.IndexOf(viewItem);
            if (index < 0) return;

            TabViewItem nextTab = null;
            if (_tabViews.Count > 1)
                nextTab = (index > 0) ? _tabViews[index - 1] : _tabViews[1];

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
            else
                UpdateTabLayout();
        }

        private void UpdateStarButton()
        {
            if (_currentTab == null) return;
            bool exists = FavoritesManager.Instance.ContainsUrl(_currentTab.CurrentUrl);
            AddFavBtn.Content = exists ? "\xE735" : "\xE734";
            AddFavBtn.Foreground = exists ? _starYellowBrush : _starGrayBrush;
        }

        private void AddFavBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab == null || string.IsNullOrEmpty(_currentTab.CurrentUrl)) return;
            string url = _currentTab.CurrentUrl;
            if (FavoritesManager.Instance.ContainsUrl(url))
            {
                var item = FavoritesManager.Instance.Favorites.FirstOrDefault(f => f.Url == url);
                if (item != null) FavoritesManager.Instance.Remove(item);
            }
            else
            {
                string title = !string.IsNullOrEmpty(_currentTab.Title) ? _currentTab.Title : _currentTab.CurrentUrl;
                FavoritesManager.Instance.Add(title, url);
            }
            UpdateStarButton();
            if (HubSplitView.IsPaneOpen) RefreshFavPanel();
        }

        // ==================== Hub 面板刷新 ====================
        private void RefreshFavPanel()
        {
            HubFavStackPanel.Children.Clear();
            foreach (var fav in FavoritesManager.Instance.Favorites)
            {
                var stack = new StackPanel { Margin = new Thickness(4, 6, 4, 6), Padding = new Thickness(8), Background = new SolidColorBrush(Colors.Transparent) };
                stack.PointerEntered += (s, e) => stack.Background = new SolidColorBrush(Colors.LightGray);
                stack.PointerExited += (s, e) => stack.Background = new SolidColorBrush(Colors.Transparent);
                stack.Children.Add(new TextBlock { Text = fav.Title, FontWeight = Windows.UI.Text.FontWeights.SemiBold, FontSize = 14, Foreground = new SolidColorBrush(Colors.Black) });
                stack.Children.Add(new TextBlock { Text = fav.Url, FontSize = 12, Foreground = new SolidColorBrush(Colors.DimGray), TextTrimming = TextTrimming.CharacterEllipsis });

                stack.PointerPressed += (s, e) =>
                {
                    if (e.Pointer.PointerDeviceType != Windows.Devices.Input.PointerDeviceType.Mouse ||
                        e.GetCurrentPoint(stack).Properties.IsLeftButtonPressed)
                    {
                        _currentTab?.Navigate(fav.Url);
                        HubSplitView.IsPaneOpen = false;
                    }
                };
                stack.RightTapped += (s, e) =>
                {
                    var flyout = new MenuFlyout();
                    var editItem = new MenuFlyoutItem { Text = "编辑" };
                    var deleteItem = new MenuFlyoutItem { Text = "删除" };
                    editItem.Click += async (_, _) =>
                    {
                        var titleBox = new TextBox { Text = fav.Title, PlaceholderText = "标题" };
                        var urlBox = new TextBox { Text = fav.Url, PlaceholderText = "网址" };
                        var panel = new StackPanel(); panel.Children.Add(titleBox); panel.Children.Add(urlBox);
                        var dialog = new ContentDialog { Title = "编辑收藏", Content = panel, PrimaryButtonText = "保存", SecondaryButtonText = "取消" };
                        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                        {
                            fav.Title = titleBox.Text;
                            fav.Url = urlBox.Text;
                            RefreshFavPanel();
                            UpdateStarButton();
                        }
                    };
                    deleteItem.Click += (_, _) => { FavoritesManager.Instance.Remove(fav); RefreshFavPanel(); UpdateStarButton(); };
                    flyout.Items.Add(editItem); flyout.Items.Add(deleteItem);
                    flyout.ShowAt(stack);
                };
                HubFavStackPanel.Children.Add(stack);
            }
        }

        private void RefreshHistoryPanel()
        {
            HubHistoryStackPanel.Children.Clear();
            foreach (var item in HistoryManager.History)
            {
                var stack = new StackPanel { Margin = new Thickness(4, 6, 4, 6) };
                stack.Children.Add(new TextBlock { Text = item.Title, FontWeight = Windows.UI.Text.FontWeights.SemiBold, FontSize = 14, Foreground = new SolidColorBrush(Colors.Black) });
                stack.Children.Add(new TextBlock { Text = $"{item.Url} - {item.VisitTime:g}", FontSize = 11, Foreground = new SolidColorBrush(Colors.DimGray) });
                stack.PointerPressed += (s, e) =>
                {
                    if (e.Pointer.PointerDeviceType != Windows.Devices.Input.PointerDeviceType.Mouse ||
                        e.GetCurrentPoint(stack).Properties.IsLeftButtonPressed)
                    {
                        _currentTab?.Navigate(item.Url);
                        HubSplitView.IsPaneOpen = false;
                    }
                };
                stack.RightTapped += (s, e) =>
                {
                    var flyout = new MenuFlyout();
                    var deleteItem = new MenuFlyoutItem { Text = "删除" };
                    deleteItem.Click += (_, _) => { HistoryManager.History.Remove(item); RefreshHistoryPanel(); };
                    flyout.Items.Add(deleteItem);
                    flyout.ShowAt(stack);
                };
                HubHistoryStackPanel.Children.Add(stack);
            }
        }

        private void RefreshDownloadsPanel()
        {
            HubDownloadsStackPanel.Children.Clear();
            foreach (var item in DownloadManager.Downloads)
            {
                var stack = new StackPanel { Margin = new Thickness(4, 6, 4, 6) };
                stack.Children.Add(new TextBlock { Text = item.FileName, FontWeight = Windows.UI.Text.FontWeights.SemiBold, FontSize = 14, Foreground = new SolidColorBrush(Colors.Black), TextTrimming = TextTrimming.CharacterEllipsis });
                stack.Children.Add(new TextBlock { Text = $"{item.Status} - {item.Progress}%", FontSize = 11, Foreground = new SolidColorBrush(Colors.DimGray) });
                HubDownloadsStackPanel.Children.Add(stack);
            }
        }

        private void HubBtn_Click(object sender, RoutedEventArgs e)
        {
            HubSplitView.IsPaneOpen = !HubSplitView.IsPaneOpen;
            if (HubSplitView.IsPaneOpen)
            {
                RefreshFavPanel();
                RefreshHistoryPanel();
                RefreshDownloadsPanel();
            }
        }

        private void CloseHubBtn_Click(object sender, RoutedEventArgs e) => HubSplitView.IsPaneOpen = false;

        private void ClearHistoryBtn_Click(object sender, RoutedEventArgs e)
        {
            HistoryManager.Clear();
            RefreshHistoryPanel();
        }

        private void ClearDownloadsBtn_Click(object sender, RoutedEventArgs e)
        {
            DownloadManager.ClearCompleted();
            RefreshDownloadsPanel();
        }

        // ==================== 滚动箭头 ====================
        private void ScrollLeftBtn_Click(object sender, RoutedEventArgs e) =>
            TabScrollViewer.ChangeView(TabScrollViewer.HorizontalOffset - 100, null, null);

        private void ScrollRightBtn_Click(object sender, RoutedEventArgs e) =>
            TabScrollViewer.ChangeView(TabScrollViewer.HorizontalOffset + 100, null, null);

        private void TabScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) { }

        // ==================== Favicon 更新 ====================
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

        // ==================== 其他按钮事件 ====================
        private void NewTabBtn_Click(object sender, RoutedEventArgs e)
        {
            var engine = EngineCombo.SelectedIndex == 1 ? EngineType.WebView2 : EngineType.EdgeHtml;
            CreateNewTab(engine, "about:blank");
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e) { if (_currentTab?.CanGoBack == true) _currentTab.GoBack(); }
        private void ForwardBtn_Click(object sender, RoutedEventArgs e) { if (_currentTab?.CanGoForward == true) _currentTab.GoForward(); }
        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => _currentTab?.Refresh();

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
                        input = "https://" + input;
                }
                _currentTab?.Navigate(input);
            }
        }

        private void UrlBox_GotFocus(object sender, RoutedEventArgs e) =>
            UrlBox.BorderBrush = new SolidColorBrush(Colors.DodgerBlue);
        private void UrlBox_LostFocus(object sender, RoutedEventArgs e) =>
            UrlBox.BorderBrush = new SolidColorBrush(Colors.LightGray);
    }
}