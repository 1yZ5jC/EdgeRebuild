using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
using Microsoft.Web.WebView2.Core;

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

        private double _zoomFactor = 1.0;
        private readonly Stack<string> _closedTabUrls = new Stack<string>();

        private readonly SolidColorBrush _selectedBrush = new SolidColorBrush(Colors.White);
        private readonly SolidColorBrush _unselectedBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
        private readonly SolidColorBrush _hoverBrush = new SolidColorBrush(Colors.Silver);
        private readonly SolidColorBrush _starYellowBrush = new SolidColorBrush(Colors.Gold);
        private readonly SolidColorBrush _starGrayBrush = new SolidColorBrush(Colors.Gray);
        private readonly SolidColorBrush _edgeBlueBrush = new SolidColorBrush(Colors.DodgerBlue);
        private readonly SolidColorBrush _webGreenBrush = new SolidColorBrush(Colors.MediumSeaGreen);

        private const int MinTabWidth = 100;
        private const int MaxTabWidth = 160;
        private const int AdditionalMargin = 30;
        private const int MinDragWidth = 20;
        private const int ButtonBaseOffset = 50;
        private double _rightReserved;

        private TabViewItem _dragItem;
        private Border _placeholder;
        private Point _dragOffset;
        private Point _dragStartScreenPoint;
        private Point _dragStartCanvasPoint;
        private bool _isDragging;
        private bool _hasMoved;
        private double _totalDx;

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
            EngineCombo.SelectionChanged += EngineCombo_SelectionChanged;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
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
                    await CreateNewTabAsync(EngineType.EdgeHtml, _pendingUrl);
                    _pendingUrl = null;
                }
                else
                {
                    await CreateNewTabAsync(EngineType.EdgeHtml, "about:blank");
                }
            }
        }

        private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            SetSafeZonePadding();
            if (!_isDragging) UpdateTabLayout();
        }

        private void ResetDragState()
        {
            if (_dragItem != null)
            {
                if (DragCanvas.Children.Contains(_dragItem.Container))
                    DragCanvas.Children.Remove(_dragItem.Container);
                _dragItem.Container.Opacity = 1.0;
            }

            if (_placeholder != null)
            {
                int idx = TabBarPanel.Children.IndexOf(_placeholder);
                if (idx >= 0) TabBarPanel.Children.RemoveAt(idx);
                _placeholder = null;
            }

            TabBarPanel.Children.Clear();
            foreach (var item in _tabViews)
            {
                if (item.Container.Parent == null)
                    TabBarPanel.Children.Add(item.Container);
            }

            _dragItem = null;
            _isDragging = false;
            _hasMoved = false;
            _totalDx = 0;

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
            if (!_isDragging) UpdateTabLayout();
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
            if (!_isLoaded || _tabViews.Count == 0 || _isDragging) return;

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

        private async Task CreateNewTabAsync(EngineType engine, string url = null)
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
                Width = MaxTabWidth
            };

            var tabPanel = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            tabPanel.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            tabPanel.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });

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

            var infoPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            infoPanel.Children.Add(engineMark);
            infoPanel.Children.Add(faviconPlaceholder);
            infoPanel.Children.Add(faviconImage);
            infoPanel.Children.Add(titleText);

            Grid.SetColumn(infoPanel, 0);
            tabPanel.Children.Add(infoPanel);

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
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 4, 0)
            };

            Grid.SetColumn(closeBtn, 1);
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

            tabBorder.PointerPressed += OnTabPointerPressed;
            tabBorder.PointerMoved += OnTabPointerMoved;
            tabBorder.PointerReleased += OnTabPointerReleased;
            tabBorder.PointerCanceled += OnTabPointerReleased;

            tabBorder.RightTapped += OnTabRightTapped;

            tabBorder.PointerEntered += (s, ev) =>
            {
                if (_currentTab != tab && !_isDragging) tabBorder.Background = _hoverBrush;
            };
            tabBorder.PointerExited += (s, ev) =>
            {
                if (_currentTab != tab && !_isDragging) tabBorder.Background = _unselectedBrush;
            };

            tab.TitleChanged += (title) =>
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    viewItem.TitleText.Text = string.IsNullOrEmpty(title) ? "新标签页" : title;
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
            tab.ContextMenuRequested += OnTabContextMenuRequested;

            await SwitchToTabAsync(viewItem);

            if (!string.IsNullOrEmpty(url))
                await tab.NavigateAsync(url);

            UpdateTabLayout();
        }

        // ==================== 拖拽实现 ====================
        private void OnTabPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var properties = e.GetCurrentPoint((UIElement)sender).Properties;
            if (properties.IsRightButtonPressed)
                return;

            ResetDragState();

            if (_tabViews.Count <= 1) return;

            var border = sender as Border;
            if (border == null) return;
            var viewItem = _tabViews.FirstOrDefault(v => v.Container == border);
            if (viewItem == null) return;

            _dragItem = viewItem;
            _dragOffset = e.GetCurrentPoint(viewItem.Container).Position;
            _hasMoved = false;
            _isDragging = false;
            _totalDx = 0;

            var pointInCanvas = e.GetCurrentPoint(DragCanvas).Position;
            _dragStartCanvasPoint = pointInCanvas;

            _dragStartScreenPoint = CoreWindow.GetForCurrentThread().PointerPosition;

            border.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void OnTabPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_dragItem == null) return;

            var posInCanvas = e.GetCurrentPoint(DragCanvas).Position;
            double dx = posInCanvas.X - _dragOffset.X;
            double dy = posInCanvas.Y - _dragOffset.Y;

            var screenPoint = CoreWindow.GetForCurrentThread().PointerPosition;
            double dyScreen = screenPoint.Y - _dragStartScreenPoint.Y;

            bool shouldPopOut = _isDragging && Math.Abs(dyScreen) > 15;

            if (!_isDragging && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3))
            {
                _hasMoved = true;

                _isDragging = true;
                int index = _tabViews.IndexOf(_dragItem);
                _placeholder = new Border
                {
                    Width = _dragItem.Container.ActualWidth,
                    Height = 32,
                    Background = new SolidColorBrush(Colors.Transparent)
                };
                TabBarPanel.Children.Insert(index, _placeholder);

                var parentPanel = _dragItem.Container.Parent as Panel;
                parentPanel?.Children.Remove(_dragItem.Container);

                DragCanvas.Children.Add(_dragItem.Container);
                Canvas.SetLeft(_dragItem.Container, posInCanvas.X - _dragOffset.X);
                Canvas.SetTop(_dragItem.Container, posInCanvas.Y - _dragOffset.Y);
                Canvas.SetZIndex(_dragItem.Container, 1000);
                _dragItem.Container.Opacity = 0.9;
            }

            if (_isDragging)
            {
                double newLeft = posInCanvas.X - _dragOffset.X;
                double maxLeft = DragCanvas.ActualWidth - _dragItem.Container.ActualWidth;
                if (newLeft > maxLeft) newLeft = maxLeft;
                Canvas.SetLeft(_dragItem.Container, newLeft);

                _totalDx = posInCanvas.X - _dragStartCanvasPoint.X;

                if (shouldPopOut)
                {
                    _dragItem.Container.ReleasePointerCaptures();
                    var item = _dragItem;
                    _dragItem = null;

                    if (DragCanvas.Children.Contains(item.Container))
                        DragCanvas.Children.Remove(item.Container);
                    item.Container.Opacity = 1.0;

                    if (_placeholder != null)
                    {
                        int idx = TabBarPanel.Children.IndexOf(_placeholder);
                        if (idx >= 0) TabBarPanel.Children.RemoveAt(idx);
                        _placeholder = null;
                    }

                    _isDragging = false;
                    _hasMoved = false;
                    _totalDx = 0;

                    TabBarPanel.Children.Clear();
                    foreach (var t in _tabViews)
                    {
                        if (t != item)
                            TabBarPanel.Children.Add(t.Container);
                    }

                    MoveTabToNewWindow(item);
                    return;
                }
            }
            e.Handled = true;
        }

        private void OnTabPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_dragItem == null) return;

            try
            {
                try { _dragItem.Container.ReleasePointerCaptures(); } catch { }

                if (!_hasMoved)
                {
                    SwitchToTab(_dragItem);
                    return;
                }

                if (_isDragging)
                {
                    var transform = _dragItem.Container.TransformToVisual(TabBarPanel);
                    var tabPos = transform.TransformPoint(new Point(0, 0));
                    double left = tabPos.X;
                    double width = _dragItem.Container.ActualWidth;
                    double centerX = left + width / 2;

                    int targetIndex = _tabViews.IndexOf(_dragItem);
                    bool placed = false;

                    for (int i = 0; i < _tabViews.Count; i++)
                    {
                        if (_tabViews[i] == _dragItem) continue;
                        var child = _tabViews[i].Container;
                        var childTransform = child.TransformToVisual(TabBarPanel);
                        double childLeft = childTransform.TransformPoint(new Point(0, 0)).X;
                        double childRight = childLeft + child.ActualWidth;

                        if (centerX >= childLeft && centerX <= childRight)
                        {
                            targetIndex = i;
                            placed = true;
                            break;
                        }
                    }

                    if (!placed)
                    {
                        if (_tabViews.Count > 0)
                        {
                            var firstChild = _tabViews[0].Container;
                            var firstTransform = firstChild.TransformToVisual(TabBarPanel);
                            double firstLeft = firstTransform.TransformPoint(new Point(0, 0)).X;

                            var lastChild = _tabViews[_tabViews.Count - 1].Container;
                            var lastTransform = lastChild.TransformToVisual(TabBarPanel);
                            double lastRight = lastTransform.TransformPoint(new Point(lastChild.ActualWidth, 0)).X;

                            if (centerX < firstLeft)
                                targetIndex = 0;
                            else if (centerX > lastRight)
                                targetIndex = _tabViews.Count;
                        }
                    }

                    int currentIndex = _tabViews.IndexOf(_dragItem);
                    if (targetIndex != currentIndex)
                        MoveTabToIndex(currentIndex, targetIndex);
                }
            }
            finally
            {
                ResetDragState();
                e.Handled = true;
            }
        }

        private void MoveTabToIndex(int oldIndex, int newIndex)
        {
            if (oldIndex == newIndex) return;
            var item = _tabViews[oldIndex];
            _tabViews.RemoveAt(oldIndex);
            if (newIndex >= _tabViews.Count)
                _tabViews.Add(item);
            else
                _tabViews.Insert(newIndex, item);
        }

        private async void MoveTabToNewWindow(TabViewItem item)
        {
            string url = item.Tab.CurrentUrl;
            CloseTab(item);
            if (_tabViews.Count == 0)
                await CreateNewTabAsync(EngineType.EdgeHtml, "about:blank");
            else
                UpdateTabLayout();

            try
            {
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create new window: {ex.Message}");
            }
        }

        // ==================== 标签右键菜单 ====================
        private void OnTabRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var border = sender as Border;
            var viewItem = _tabViews.FirstOrDefault(v => v.Container == border);
            if (viewItem == null) return;

            var menu = new MenuFlyout();

            var newTabItem = new MenuFlyoutItem { Text = "新建标签页" };
            newTabItem.Click += async (s, ev) =>
                await CreateNewTabAsync(EngineCombo.SelectedIndex == 1 ? EngineType.WebView2 : EngineType.EdgeHtml, "about:blank");
            menu.Items.Add(newTabItem);

            var reloadItem = new MenuFlyoutItem { Text = "重新加载" };
            reloadItem.Click += (s, ev) => viewItem.Tab?.RefreshAsync();
            menu.Items.Add(reloadItem);

            var closeItem = new MenuFlyoutItem { Text = "关闭标签页" };
            closeItem.Click += (s, ev) => CloseTab(viewItem);
            menu.Items.Add(closeItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var closeOthersItem = new MenuFlyoutItem { Text = "关闭其他标签页" };
            closeOthersItem.Click += (s, ev) => CloseOtherTabs(viewItem);
            menu.Items.Add(closeOthersItem);

            var closeRightItem = new MenuFlyoutItem { Text = "关闭右侧标签页" };
            closeRightItem.Click += (s, ev) => CloseTabsToRight(viewItem);
            menu.Items.Add(closeRightItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var moveItem = new MenuFlyoutItem { Text = "移动到新窗口" };
            moveItem.Click += (s, ev) =>
            {
                _ = SwitchToTabAsync(viewItem);
                MoveTabToNewWindow(viewItem);
            };
            menu.Items.Add(moveItem);

            menu.ShowAt(border, e.GetPosition(border));
        }

        private void CloseOtherTabs(TabViewItem keepItem)
        {
            var tabsToClose = _tabViews.Where(t => t != keepItem).ToList();
            foreach (var item in tabsToClose)
            {
                CloseTab(item);
            }
        }

        private void CloseTabsToRight(TabViewItem startItem)
        {
            int index = _tabViews.IndexOf(startItem);
            if (index < 0) return;
            var tabsToClose = _tabViews.Skip(index + 1).ToList();
            foreach (var item in tabsToClose)
            {
                CloseTab(item);
            }
        }

        // ==================== 网页右键菜单（仅 WebView2） ====================
        private void OnTabContextMenuRequested(TabContextMenuEventArgs args)
        {
            if (_currentTab is EdgeHtmlTab)
                return;

            var flyout = new MenuFlyout();

            var copyItem = new MenuFlyoutItem { Text = "复制" };
            copyItem.Click += async (s, e) =>
            {
                if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                {
                    string selectedText = await wv2.CoreWebView2.ExecuteScriptAsync("window.getSelection().toString();");
                    if (!string.IsNullOrEmpty(selectedText))
                    {
                        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                        dataPackage.SetText(selectedText);
                        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                    }
                }
            };
            copyItem.IsEnabled = args.HasSelection;
            flyout.Items.Add(copyItem);

            var pasteItem = new MenuFlyoutItem { Text = "粘贴" };
            pasteItem.Click += async (s, e) =>
            {
                var dialog = new ContentDialog
                {
                    Title = "粘贴",
                    Content = "请使用键盘快捷键 Ctrl+V 进行粘贴。",
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
            };
            pasteItem.IsEnabled = args.IsEditable;
            flyout.Items.Add(pasteItem);

            var selectAllItem = new MenuFlyoutItem { Text = "全选" };
            selectAllItem.Click += async (s, e) =>
            {
                if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                    await wv2.CoreWebView2.ExecuteScriptAsync("document.execCommand('selectAll');");
            };
            flyout.Items.Add(selectAllItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var backItem = new MenuFlyoutItem { Text = "后退", IsEnabled = args.CanGoBack };
            backItem.Click += (s, e) => _currentTab?.GoBackAsync();
            flyout.Items.Add(backItem);

            var forwardItem = new MenuFlyoutItem { Text = "前进", IsEnabled = args.CanGoForward };
            forwardItem.Click += (s, e) => _currentTab?.GoForwardAsync();
            flyout.Items.Add(forwardItem);

            var refreshItem = new MenuFlyoutItem { Text = "刷新" };
            refreshItem.Click += (s, e) => _currentTab?.RefreshAsync();
            flyout.Items.Add(refreshItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            if (args.MenuType == ContextMenuType.Link && !string.IsNullOrEmpty(args.LinkUrl))
            {
                var openInNewTabItem = new MenuFlyoutItem { Text = "在新标签页中打开" };
                openInNewTabItem.Click += async (s, e) => await CreateNewTabAsync(_currentTab?.Engine ?? EngineType.EdgeHtml, args.LinkUrl);
                flyout.Items.Add(openInNewTabItem);

                var copyLinkItem = new MenuFlyoutItem { Text = "复制链接" };
                copyLinkItem.Click += (s, e) =>
                {
                    var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dataPackage.SetText(args.LinkUrl);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                };
                flyout.Items.Add(copyLinkItem);
            }

            if (args.MenuType == ContextMenuType.Image && !string.IsNullOrEmpty(args.ImageUrl))
            {
                var copyImageUrlItem = new MenuFlyoutItem { Text = "复制图片地址" };
                copyImageUrlItem.Click += (s, e) =>
                {
                    var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dataPackage.SetText(args.ImageUrl);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                };
                flyout.Items.Add(copyImageUrlItem);
            }

            flyout.Items.Add(new MenuFlyoutSeparator());

            var printItem = new MenuFlyoutItem { Text = "打印" };
            printItem.Click += (s, e) =>
            {
                if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                    wv2.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
            };
            flyout.Items.Add(printItem);

            var sourceItem = new MenuFlyoutItem { Text = "查看页面源代码" };
            sourceItem.Click += async (s, ev) => await ViewSourceAsync();
            flyout.Items.Add(sourceItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            if (_currentTab is WebView2Tab wv2Tab && wv2Tab.CoreWebView2 != null)
            {
                var inspectItem = new MenuFlyoutItem { Text = "检查元素 (F12)" };
                inspectItem.Click += (s, e) => wv2Tab.CoreWebView2.OpenDevToolsWindow();
                flyout.Items.Add(inspectItem);
            }

            var targetElement = _currentTab?.ViewElement;
            if (targetElement != null)
            {
                double x = Math.Max(0, Math.Min(args.Location.X, targetElement.ActualWidth));
                double y = Math.Max(0, Math.Min(args.Location.Y, targetElement.ActualHeight));
                flyout.ShowAt(targetElement, new Point(x, y));
            }
        }

        // ==================== 功能菜单实现 ====================
        private void MenuBtn_Click(object sender, RoutedEventArgs e)
        {
            var menu = new MenuFlyout();

            var engineSub = new MenuFlyoutSubItem { Text = "渲染引擎" };
            var edgeHtmlItem = new MenuFlyoutItem { Text = "EdgeHTML" };
            edgeHtmlItem.Click += async (s, ev) => await SwitchCurrentTabEngine(EngineType.EdgeHtml);
            var webView2Item = new MenuFlyoutItem { Text = "WebView2" };
            webView2Item.Click += async (s, ev) => await SwitchCurrentTabEngine(EngineType.WebView2);
            if (_currentTab?.Engine == EngineType.EdgeHtml)
                edgeHtmlItem.IsEnabled = false;
            else if (_currentTab?.Engine == EngineType.WebView2)
                webView2Item.IsEnabled = false;
            engineSub.Items.Add(edgeHtmlItem);
            engineSub.Items.Add(webView2Item);
            menu.Items.Add(engineSub);

            menu.Items.Add(new MenuFlyoutSeparator());

            var zoomSub = new MenuFlyoutSubItem { Text = "缩放" };
            var zoomIn = new MenuFlyoutItem { Text = "放大" };
            zoomIn.Click += (s, ev) => AdjustZoom(0.1);
            var zoomOut = new MenuFlyoutItem { Text = "缩小" };
            zoomOut.Click += (s, ev) => AdjustZoom(-0.1);
            var zoomReset = new MenuFlyoutItem { Text = "重置" };
            zoomReset.Click += (s, ev) => ResetZoom();
            zoomSub.Items.Add(zoomIn);
            zoomSub.Items.Add(zoomOut);
            zoomSub.Items.Add(zoomReset);
            menu.Items.Add(zoomSub);

            var printItem = new MenuFlyoutItem { Text = "打印" };
            printItem.Click += (s, e) => PrintCurrentPage();
            var findItem = new MenuFlyoutItem { Text = "查找" };
            findItem.Click += (s, e) => FindOnPage();
            var sourceItem = new MenuFlyoutItem { Text = "查看页面源代码" };
            sourceItem.Click += async (s, e) => await ViewSourceAsync();
            menu.Items.Add(printItem);
            menu.Items.Add(findItem);
            menu.Items.Add(sourceItem);

            var skinSub = new MenuFlyoutSubItem { Text = "皮肤" };
            var classicSkin = new MenuFlyoutItem { Text = "经典 Edge" };
            classicSkin.Click += (s, ev) => SwitchSkin("Spartan");
            var modernIeSkin = new MenuFlyoutItem { Text = "Modern IE" };
            modernIeSkin.Click += (s, ev) => SwitchSkin("ModernIE");
            var mobileSkin = new MenuFlyoutItem { Text = "Windows 10 Mobile (即将推出)" };
            mobileSkin.IsEnabled = false;
            skinSub.Items.Add(classicSkin);
            skinSub.Items.Add(modernIeSkin);
            skinSub.Items.Add(mobileSkin);
            menu.Items.Add(skinSub);

            var toolsSub = new MenuFlyoutSubItem { Text = "更多工具" };
            var extensionsItem = new MenuFlyoutItem { Text = "扩展管理器" };
            extensionsItem.Click += (s, ev) => ShowNotImplementedDialog("扩展管理器");
            var taskManagerItem = new MenuFlyoutItem { Text = "任务管理器" };
            taskManagerItem.Click += (s, ev) => ShowNotImplementedDialog("任务管理器");
            var devToolsItem = new MenuFlyoutItem { Text = "开发者工具" };
            devToolsItem.Click += (s, ev) => OpenDevTools();
            toolsSub.Items.Add(extensionsItem);
            toolsSub.Items.Add(taskManagerItem);
            toolsSub.Items.Add(devToolsItem);
            menu.Items.Add(toolsSub);

            menu.Items.Add(new MenuFlyoutSeparator());

            var reopenTab = new MenuFlyoutItem { Text = "重新打开关闭的标签" };
            reopenTab.Click += async (s, ev) => await ReopenClosedTabAsync();
            var bookmarkAll = new MenuFlyoutItem { Text = "将所有标签加入收藏" };
            bookmarkAll.Click += (s, ev) => BookmarkAllTabs();
            menu.Items.Add(reopenTab);
            menu.Items.Add(bookmarkAll);

            var historyItem = new MenuFlyoutItem { Text = "历史记录" };
            historyItem.Click += (s, ev) =>
            {
                HubSplitView.IsPaneOpen = true;
                HubPivot.SelectedIndex = 1;
                RefreshHistoryPanel();
            };
            var clearDataItem = new MenuFlyoutItem { Text = "清除浏览数据" };
            clearDataItem.Click += async (s, ev) => await ClearBrowsingDataAsync();
            menu.Items.Add(historyItem);
            menu.Items.Add(clearDataItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var settingsItem = new MenuFlyoutItem { Text = "设置" };
            settingsItem.Click += (s, ev) => ShowNotImplementedDialog("设置");
            var aboutItem = new MenuFlyoutItem { Text = "关于 Edge Rebuild" };
            aboutItem.Click += (s, ev) => ShowAboutDialog();
            menu.Items.Add(settingsItem);
            menu.Items.Add(aboutItem);

            menu.ShowAt(MenuBtn, new Point(0, MenuBtn.ActualHeight));
        }

        // ---------- 菜单功能实现方法 ----------
        private async Task SwitchCurrentTabEngine(EngineType newEngine)
        {
            if (_currentTab == null || _currentTab.Engine == newEngine) return;

            string currentUrl = _currentTab.CurrentUrl;
            var viewItem = _tabViews.FirstOrDefault(v => v.Tab == _currentTab);
            if (viewItem == null) return;

            _currentTab.Dispose();
            int index = _tabViews.IndexOf(viewItem);

            IBrowserTab newTab = newEngine == EngineType.WebView2
                ? new WebView2Tab()
                : new EdgeHtmlTab();

            viewItem.Tab = newTab;

            newTab.TitleChanged += (title) =>
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    viewItem.TitleText.Text = string.IsNullOrEmpty(title) ? "新标签页" : title;
                });
            };
            newTab.UrlChanged += (url) =>
            {
                if (_currentTab == newTab)
                {
                    UrlBox.Text = url;
                    UpdateStarButton();
                }
                if (!string.IsNullOrEmpty(url) && url != "about:blank")
                    HistoryManager.Add(newTab.Title ?? url, url);
            };
            newTab.CanGoBackChanged += (can) => { if (_currentTab == newTab) BackBtn.IsEnabled = can; };
            newTab.CanGoForwardChanged += (can) => { if (_currentTab == newTab) ForwardBtn.IsEnabled = can; };
            newTab.FaviconChanged += (faviconUrl) => UpdateFavicon(viewItem, faviconUrl);
            newTab.ContextMenuRequested += OnTabContextMenuRequested;

            viewItem.EngineMark.Text = newTab.Engine == EngineType.EdgeHtml ? "E" : "W";
            viewItem.EngineMark.Foreground = newTab.Engine == EngineType.EdgeHtml ? _edgeBlueBrush : _webGreenBrush;

            _currentTab = null;
            await SwitchToTabAsync(viewItem);

            if (!string.IsNullOrEmpty(currentUrl) && currentUrl != "about:blank")
                await newTab.NavigateAsync(currentUrl);
            else
                await newTab.NavigateAsync("about:blank");
        }

        private async void AdjustZoom(double delta)
        {
            _zoomFactor = Math.Max(0.25, Math.Min(5.0, _zoomFactor + delta));
            if (_currentTab is EdgeHtmlTab edgeTab)
            {
                await edgeTab.ExecuteScriptAsync($"document.body.style.zoom = '{_zoomFactor}';");
            }
            else if (_currentTab is WebView2Tab wv2)
            {
                await wv2.ExecuteScriptAsync($"document.body.style.zoom = '{_zoomFactor}';");
            }
        }

        private async void ResetZoom()
        {
            _zoomFactor = 1.0;
            if (_currentTab is EdgeHtmlTab edgeTab)
            {
                await edgeTab.ExecuteScriptAsync("document.body.style.zoom = '1';");
            }
            else if (_currentTab is WebView2Tab wv2)
            {
                await wv2.ExecuteScriptAsync("document.body.style.zoom = '1';");
            }
        }

        private void PrintCurrentPage()
        {
            if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
            {
                wv2.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
            }
            else if (_currentTab is EdgeHtmlTab edgeTab)
            {
                _ = edgeTab.ExecuteScriptAsync("window.print();");
            }
        }

        private void FindOnPage()
        {
            if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
            {
                _ = wv2.CoreWebView2.ExecuteScriptAsync("window.find('');");
            }
            else
            {
                ShowNotImplementedDialog("查找（请切换到 WebView2）");
            }
        }

        private async Task ViewSourceAsync()
        {
            if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
            {
                try
                {
                    var html = await wv2.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML;");
                    html = html?.Trim('"').Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t");
                    var dialog = new ContentDialog
                    {
                        Title = "页面源代码",
                        Content = new ScrollViewer { Content = new TextBlock { Text = html, FontSize = 10, IsTextSelectionEnabled = true } },
                        PrimaryButtonText = "关闭"
                    };
                    await dialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    await new ContentDialog { Title = "错误", Content = $"获取源代码失败：{ex.Message}", CloseButtonText = "确定" }.ShowAsync();
                }
            }
            else
            {
                ShowNotImplementedDialog("查看源代码（请切换到 WebView2）");
            }
        }

        private void OpenDevTools()
        {
            if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                wv2.CoreWebView2.OpenDevToolsWindow();
            else
                ShowNotImplementedDialog("开发者工具（请切换到 WebView2）");
        }

        private async Task ReopenClosedTabAsync()
        {
            if (_closedTabUrls.Count > 0)
            {
                string url = _closedTabUrls.Pop();
                await CreateNewTabAsync(EngineType.WebView2, url);
            }
            else
            {
                await new ContentDialog { Title = "提示", Content = "没有可恢复的标签页。", CloseButtonText = "确定" }.ShowAsync();
            }
        }

        private void BookmarkAllTabs()
        {
            int count = 0;
            foreach (var item in _tabViews)
            {
                string url = item.Tab.CurrentUrl;
                if (!string.IsNullOrEmpty(url) && url != "about:blank" && !FavoritesManager.Instance.ContainsUrl(url))
                {
                    string title = !string.IsNullOrEmpty(item.Tab.Title) ? item.Tab.Title : url;
                    FavoritesManager.Instance.Add(title, url);
                    count++;
                }
            }
            UpdateStarButton();
            if (HubSplitView.IsPaneOpen) RefreshFavPanel();
            _ = new ContentDialog { Title = "完成", Content = $"已将 {count} 个标签页加入收藏。", CloseButtonText = "确定" }.ShowAsync();
        }

        private async Task ClearBrowsingDataAsync()
        {
            HistoryManager.Clear();
            RefreshHistoryPanel();
            DownloadManager.ClearCompleted();
            RefreshDownloadsPanel();

            if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
            {
                wv2.CoreWebView2.Profile.CookieManager.DeleteAllCookies();
            }

            await new ContentDialog { Title = "已清除", Content = "浏览数据已清除。", CloseButtonText = "确定" }.ShowAsync();
        }

        private void ShowAboutDialog()
        {
            var dialog = new ContentDialog
            {
                Title = "Edge Rebuild",
                Content = "版本 0.2 Alpha\n基于 UWP 的双内核浏览器外壳。",
                CloseButtonText = "确定"
            };
            _ = dialog.ShowAsync();
        }

        private async void ShowNotImplementedDialog(string feature)
        {
            var dialog = new ContentDialog
            {
                Title = "即将推出",
                Content = $"功能“{feature}”尚未实现。",
                CloseButtonText = "确定"
            };
            await dialog.ShowAsync();
        }

        private void CloseTab(TabViewItem viewItem)
        {
            if (!_isLoaded) return;
            int index = _tabViews.IndexOf(viewItem);
            if (index < 0) return;

            string url = viewItem.Tab.CurrentUrl;
            if (!string.IsNullOrEmpty(url) && url != "about:blank")
                _closedTabUrls.Push(url);

            var parent = viewItem.Container.Parent as Panel;
            parent?.Children.Remove(viewItem.Container);

            TabViewItem nextTab = null;
            if (_tabViews.Count > 1)
                nextTab = (index > 0) ? _tabViews[index - 1] : _tabViews[1];

            _tabViews.RemoveAt(index);

            if (_currentTab == viewItem.Tab)
            {
                if (nextTab != null)
                    _ = SwitchToTabAsync(nextTab);
                else
                    _currentTab = null;
            }
            viewItem.Tab.Dispose();

            if (_tabViews.Count == 0)
                _ = CreateNewTabAsync(EngineType.EdgeHtml, "about:blank");
            else
                UpdateTabLayout();
        }

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
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => RefreshDownloadsPanel());
        }

        private void SwitchToTab(TabViewItem viewItem)
        {
            _ = SwitchToTabAsync(viewItem);
        }

        private async Task SwitchToTabAsync(TabViewItem viewItem)
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
                EngineCombo.SelectedIndex = 0;
            }
            else
            {
                EngineLabel.Text = "W";
                EngineLabel.Foreground = _webGreenBrush;
                EngineCombo.SelectedIndex = 1;
            }

            foreach (var t in _tabViews)
                t.Container.Background = t == viewItem ? _selectedBrush : _unselectedBrush;

            UpdateStarButton();
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

        private void RefreshFavPanel()
        {
            HubFavStackPanel.Children.Clear();
            foreach (var fav in FavoritesManager.Instance.Favorites)
            {
                var stack = new StackPanel { Margin = new Thickness(4, 6, 4, 6), Padding = new Thickness(8), Background = new SolidColorBrush(Colors.Transparent) };
                stack.PointerEntered += (s, ev) => stack.Background = new SolidColorBrush(Colors.LightGray);
                stack.PointerExited += (s, ev) => stack.Background = new SolidColorBrush(Colors.Transparent);
                stack.Children.Add(new TextBlock { Text = fav.Title, FontWeight = Windows.UI.Text.FontWeights.SemiBold, FontSize = 14, Foreground = new SolidColorBrush(Colors.Black) });
                stack.Children.Add(new TextBlock { Text = fav.Url, FontSize = 12, Foreground = new SolidColorBrush(Colors.DimGray), TextTrimming = TextTrimming.CharacterEllipsis });

                stack.PointerPressed += (s, ev) =>
                {
                    if (ev.Pointer.PointerDeviceType != Windows.Devices.Input.PointerDeviceType.Mouse ||
                        ev.GetCurrentPoint(stack).Properties.IsLeftButtonPressed)
                    {
                        _currentTab?.NavigateAsync(fav.Url);
                        HubSplitView.IsPaneOpen = false;
                    }
                };
                stack.RightTapped += (s, ev) =>
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
                stack.PointerPressed += (s, ev) =>
                {
                    if (ev.Pointer.PointerDeviceType != Windows.Devices.Input.PointerDeviceType.Mouse ||
                        ev.GetCurrentPoint(stack).Properties.IsLeftButtonPressed)
                    {
                        _currentTab?.NavigateAsync(item.Url);
                        HubSplitView.IsPaneOpen = false;
                    }
                };
                stack.RightTapped += (s, ev) =>
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

        private void ScrollLeftBtn_Click(object sender, RoutedEventArgs e) =>
            TabScrollViewer.ChangeView(TabScrollViewer.HorizontalOffset - 100, null, null);
        private void ScrollRightBtn_Click(object sender, RoutedEventArgs e) =>
            TabScrollViewer.ChangeView(TabScrollViewer.HorizontalOffset + 100, null, null);
        private void TabScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) { }

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

        private async void NewTabBtn_Click(object sender, RoutedEventArgs e)
        {
            var engine = EngineCombo.SelectedIndex == 1 ? EngineType.WebView2 : EngineType.EdgeHtml;
            await CreateNewTabAsync(engine, "about:blank");
        }

        private async void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab?.CanGoBack == true)
                await _currentTab.GoBackAsync();
        }

        private async void ForwardBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab?.CanGoForward == true)
                await _currentTab.GoForwardAsync();
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab != null)
                await _currentTab.RefreshAsync();
        }

        private async void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
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
                    await _currentTab?.NavigateAsync(input);
                }
            }
        }

        private void UrlBox_GotFocus(object sender, RoutedEventArgs e) =>
            UrlBox.BorderBrush = new SolidColorBrush(Colors.DodgerBlue);
        private void UrlBox_LostFocus(object sender, RoutedEventArgs e) =>
            UrlBox.BorderBrush = new SolidColorBrush(Colors.LightGray);

        private async void EngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _currentTab == null) return;

            EngineType newEngine = EngineCombo.SelectedIndex == 1 ? EngineType.WebView2 : EngineType.EdgeHtml;
            if (_currentTab.Engine == newEngine) return;

            string currentUrl = _currentTab.CurrentUrl;
            var viewItem = _tabViews.FirstOrDefault(v => v.Tab == _currentTab);
            if (viewItem == null) return;

            _currentTab.Dispose();
            int index = _tabViews.IndexOf(viewItem);

            IBrowserTab newTab = newEngine == EngineType.WebView2
                ? new WebView2Tab()
                : new EdgeHtmlTab();

            viewItem.Tab = newTab;

            newTab.TitleChanged += (title) =>
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    viewItem.TitleText.Text = string.IsNullOrEmpty(title) ? "新标签页" : title;
                });
            };
            newTab.UrlChanged += (url) =>
            {
                if (_currentTab == newTab)
                {
                    UrlBox.Text = url;
                    UpdateStarButton();
                }
                if (!string.IsNullOrEmpty(url) && url != "about:blank")
                    HistoryManager.Add(newTab.Title ?? url, url);
            };
            newTab.CanGoBackChanged += (can) => { if (_currentTab == newTab) BackBtn.IsEnabled = can; };
            newTab.CanGoForwardChanged += (can) => { if (_currentTab == newTab) ForwardBtn.IsEnabled = can; };
            newTab.FaviconChanged += (faviconUrl) => UpdateFavicon(viewItem, faviconUrl);
            newTab.ContextMenuRequested += OnTabContextMenuRequested;

            viewItem.EngineMark.Text = newTab.Engine == EngineType.EdgeHtml ? "E" : "W";
            viewItem.EngineMark.Foreground = newTab.Engine == EngineType.EdgeHtml ? _edgeBlueBrush : _webGreenBrush;

            _currentTab = null;
            await SwitchToTabAsync(viewItem);

            if (!string.IsNullOrEmpty(currentUrl) && currentUrl != "about:blank")
                await newTab.NavigateAsync(currentUrl);
            else
                await newTab.NavigateAsync("about:blank");
        }

        // ==================== 皮肤切换 ====================
        private async void SwitchSkin(string name)
        {
            App.ApplySkin(name);
            await SettingsManager.SetAsync("Skin", name);
        }
    }
}