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
        private const int MinRightPadding = 40;

        private Point _dragStartPoint;
        private TabViewItem _dragItem;
        private int _dragStartIndex;
        private bool _isDragging;
        private bool _isSorting;
        private TranslateTransform _dragTransform;

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

            SetDragAreaMargin();
            UpdateScrollButtonsVisibility();

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

        private void SetDragAreaMargin()
        {
            double rightInset = 120;
            try
            {
                var bounds = ApplicationView.GetForCurrentView().VisibleBounds;
                var windowBounds = Window.Current.Bounds;
                rightInset = windowBounds.Width - bounds.Width;
                if (rightInset <= 0) rightInset = 120;
            }
            catch { }
            TitleBarDragArea.Margin = new Thickness(0, 0, rightInset + MinRightPadding, 0);
        }

        private void TabBarBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SetDragAreaMargin();
            AdjustTabWidths();
        }

        private void UpdateScrollButtonsVisibility()
        {
            if (TabScrollViewer.ScrollableWidth > 0)
            {
                ScrollLeftBtn.Visibility = Visibility.Visible;
                ScrollRightBtn.Visibility = Visibility.Visible;
            }
            else
            {
                ScrollLeftBtn.Visibility = Visibility.Collapsed;
                ScrollRightBtn.Visibility = Visibility.Collapsed;
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string url)
            {
                _pendingUrl = url;
            }
        }

        private void AdjustTabWidths()
        {
            if (_tabViews.Count == 0) return;

            double availableWidth = TabScrollViewer.ViewportWidth;
            if (availableWidth <= 0) return;

            double totalIdeal = _tabViews.Count * MaxTabWidth;
            double tabWidth = MaxTabWidth;
            if (totalIdeal > availableWidth)
            {
                tabWidth = Math.Max(MinTabWidth, availableWidth / _tabViews.Count);
            }

            foreach (var item in _tabViews)
            {
                item.Container.Width = tabWidth;
                double reserved = 60;
                item.TitleText.MaxWidth = Math.Max(0, tabWidth - reserved);
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
                Width = MaxTabWidth
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

            tabBorder.PointerPressed += OnTabPointerPressed;
            tabBorder.PointerMoved += OnTabPointerMoved;
            tabBorder.PointerReleased += OnTabPointerReleased;

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
            };
            tab.CanGoBackChanged += (can) => { if (_currentTab == tab) BackBtn.IsEnabled = can; };
            tab.CanGoForwardChanged += (can) => { if (_currentTab == tab) ForwardBtn.IsEnabled = can; };
            tab.FaviconChanged += (faviconUrl) => UpdateFavicon(viewItem, faviconUrl);

            SwitchToTab(viewItem);

            if (!string.IsNullOrEmpty(url))
                tab.Navigate(url);

            AdjustTabWidths();
            UpdateScrollButtonsVisibility();
        }

        private void ScrollLeftBtn_Click(object sender, RoutedEventArgs e) =>
            TabScrollViewer.ChangeView(TabScrollViewer.HorizontalOffset - 100, null, null);

        private void ScrollRightBtn_Click(object sender, RoutedEventArgs e) =>
            TabScrollViewer.ChangeView(TabScrollViewer.HorizontalOffset + 100, null, null);

        private void TabScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) =>
            UpdateScrollButtonsVisibility();

        private void OnTabPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;

            var viewItem = _tabViews.FirstOrDefault(v => v.Container == border);
            if (viewItem == null) return;

            _dragItem = viewItem;
            _dragStartIndex = _tabViews.IndexOf(viewItem);
            _dragStartPoint = e.GetCurrentPoint(border).Position;
            _isDragging = false;
            _isSorting = false;
            _dragTransform = new TranslateTransform();
            viewItem.Container.RenderTransform = _dragTransform;
            border.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void OnTabPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_dragItem == null || sender != _dragItem.Container) return;

            var currentPoint = e.GetCurrentPoint(_dragItem.Container);
            double dx = currentPoint.Position.X - _dragStartPoint.X;
            double dy = currentPoint.Position.Y - _dragStartPoint.Y;

            if (!_isDragging && !_isSorting && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3))
            {
                if (dy < -30 || dy > 30)
                {
                    _isDragging = true;
                    _dragItem.Container.ReleasePointerCapture(e.Pointer);
                    _dragItem.Container.RenderTransform = null;
                    MoveTabToNewWindow(_dragItem);
                    _dragItem = null;
                    return;
                }
                else
                {
                    _isSorting = true;
                }
            }

            if (_isSorting)
            {
                _dragTransform.X = dx;

                int currentIndex = _tabViews.IndexOf(_dragItem);
                if (currentIndex < 0) return;

                double itemWidth = _dragItem.Container.ActualWidth;
                int offset = (int)(dx / itemWidth);
                int newIndex = _dragStartIndex + offset;
                newIndex = Math.Max(0, Math.Min(newIndex, _tabViews.Count - 1));

                if (newIndex != currentIndex)
                {
                    SwapTabs(currentIndex, newIndex);
                }
            }
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

        private void OnTabPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_dragItem != null && sender == _dragItem.Container)
            {
                _dragItem.Container.ReleasePointerCapture(e.Pointer);
                if (_isSorting)
                {
                    _dragItem.Container.RenderTransform = null;
                }
                _dragItem = null;
            }
            _isDragging = false;
            _isSorting = false;
            e.Handled = true;
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
                nextTab = (index > 0) ? _tabViews[index - 1] : _tabViews[1];
            }

            TabBarPanel.Children.Remove(viewItem.Container);
            _tabViews.RemoveAt(index);

            if (_currentTab == viewItem.Tab)
            {
                if (nextTab != null)
                    SwitchToTab(nextTab);
                else
                    _currentTab = null;
            }

            viewItem.Tab.Dispose();

            if (_tabViews.Count == 0)
                CreateNewTab(EngineType.EdgeHtml, "about:blank");
            else
                AdjustTabWidths();
            UpdateScrollButtonsVisibility();
        }

        private void UpdateStarButton()
        {
            if (_currentTab == null) return;
            bool exists = FavoritesManager.Instance.ContainsUrl(_currentTab.CurrentUrl);
            if (exists)
            {
                AddFavBtn.Content = "\xE735";
                AddFavBtn.Foreground = _starYellowBrush;
            }
            else
            {
                AddFavBtn.Content = "\xE734";
                AddFavBtn.Foreground = _starGrayBrush;
            }
        }

        private void AddFavBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab == null || string.IsNullOrEmpty(_currentTab.CurrentUrl)) return;

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

        private void CloseHubBtn_Click(object sender, RoutedEventArgs e) => HubSplitView.IsPaneOpen = false;

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

                stack.PointerPressed += (s, e) =>
                {
                    if (e.Pointer.PointerDeviceType != Windows.Devices.Input.PointerDeviceType.Mouse ||
                        e.GetCurrentPoint(stack).Properties.IsLeftButtonPressed)
                    {
                        _currentTab?.Navigate(fav.Url);
                        HubSplitView.IsPaneOpen = false;
                    }
                };

                HubFavStackPanel.Children.Add(stack);
            }
        }

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
                    {
                        input = "https://" + input;
                    }
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