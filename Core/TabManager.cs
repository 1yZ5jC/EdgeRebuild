using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using EdgeRebuild.Services;

namespace EdgeRebuild.Core
{
    public class TabManager
    {
        private readonly List<TabViewItem> _tabs = new List<TabViewItem>();
        private IBrowserTab _currentTab;
        private readonly Stack<(string url, string title, EngineType engine)> _closedTabs = new Stack<(string, string, EngineType)>();
        private const int MaxClosedTabs = 20;

        private readonly Dictionary<IBrowserTab, DispatcherTimer> _suspendTimers = new Dictionary<IBrowserTab, DispatcherTimer>();
        private const int SuspendDelaySeconds = 120;
        private bool _enableTabSuspend = true;

        private StackPanel _tabBarPanel;
        private Panel _contentContainer;
        private readonly Func<SkinManager.SkinColors> _colorProvider;

        // 无标签条模式（Metro 窗口）：标签 UI 不构建，靠 TabCreated/TabChanged 事件驱动
        private bool IsTabBarlessMode => _tabBarPanel == null;

        public event Action<TabViewItem> TabCreated;
        public event Action<TabViewItem> TabChanged;
        public event Action<int> TabCountChanged;
        public event Action<TabViewItem> TabClosed;
        public event Action<IBrowserTab, bool> TabSuspendedChanged;
        public event Action<IBrowserTab, string> TabUrlChanged;
        public event Action<IBrowserTab, bool> TabCanGoBackChanged;
        public event Action<IBrowserTab, bool> TabCanGoForwardChanged;
        public event Action<string> NewWindowRequested;
        public event Action<TabContextMenuEventArgs> ContextMenuRequested;
        public event Action TabOrderChanged;
        public event Action<PageShortcutKey> PageKeyPressed;
        public event Action<string> PageMarkActionRequested;
        public event Action<string> TtsTextRequested;
        public event Action<string> NtpSearchRequested;

        public IReadOnlyList<TabViewItem> Tabs => _tabs;
        public IBrowserTab CurrentTab => _currentTab;
        public int TabCount => _tabs.Count;
        public bool IsTabSuspendEnabled => _enableTabSuspend;

        public const int PinnedTabWidth = 36;

        public TabManager(Func<SkinManager.SkinColors> colorProvider = null)
        {
            _colorProvider = colorProvider;
            _ = LoadSettingsAsync();
        }

        public void Initialize(StackPanel tabBarPanel, Panel contentContainer)
        {
            _tabBarPanel = tabBarPanel;
            _contentContainer = contentContainer;
        }

        private SkinManager.SkinColors GetColors() => _colorProvider?.Invoke() ?? ThemeManager.Colors;

        private async Task LoadSettingsAsync()
        {
            var setting = await SettingsManager.GetAsync("EnableTabSuspend") ?? "True";
            _enableTabSuspend = (setting == "True" || setting == "true");
        }

        public async Task<TabViewItem> CreateTabAsync(EngineType engine, string url = null, bool isPinned = false, bool isMetroHome = false)
        {
            IBrowserTab tab = engine == EngineType.WebView2 ? new WebView2Tab() : new EdgeHtmlTab();
            if (isMetroHome) tab.SetMetroHome();

            if (tab is WebView2Tab wv2Tab)
            {
                string doNotTrack = await SettingsManager.GetAsync("DoNotTrack") ?? "False";
                if (doNotTrack == "True" || doNotTrack == "true")
                {
                    await wv2Tab.EnsureInitializedAsync();
                    wv2Tab.CoreWebView2.WebResourceRequested += OnWebResourceRequestedForDNT;
                }
            }

            var viewItem = new TabViewItem
            {
                Tab = tab,
                IsSuspended = false,
                IsPinned = false,
                OriginalTitle = null
            };

            BuildTabUI(viewItem);
            BindTabEvents(viewItem);

            _tabs.Add(viewItem);
            TabCreated?.Invoke(viewItem);

            if (!string.IsNullOrEmpty(url))
                await tab.NavigateAsync(url);
            else
                await tab.NavigateAsync("about:blank");

            TabCountChanged?.Invoke(_tabs.Count);

            if (_currentTab == null)
                await SwitchToTabAsync(viewItem);

            if (isPinned && !viewItem.IsPinned)
                await TogglePinTabAsync(viewItem);

            SessionManager.ScheduleSave(_tabs);

            return viewItem;
        }

        private void BuildTabUI(TabViewItem viewItem)
        {
            if (_contentContainer == null)
                throw new InvalidOperationException("TabManager not initialized. Call Initialize() first.");

            var tab = viewItem.Tab;
            var colors = GetColors();

            // 无标签条模式（Metro 窗口）：不构建顶部标签 UI，仅内容容器
            if (_tabBarPanel != null && colors != null)
            {
            var tabBorder = new Border
            {
                Height = 32,
                Background = colors.TabInactiveBackground,
                BorderBrush = ThemeManager.GetSeparatorBrush(),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                Width = LayoutManager.MaxTabWidth
            };

            var tabPanel = new Grid { VerticalAlignment = VerticalAlignment.Center };
            tabPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tabPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var engineMark = new TextBlock
            {
                Text = tab.Engine == EngineType.EdgeHtml ? "E" : "W",
                Foreground = tab.Engine == EngineType.EdgeHtml
                    ? new SolidColorBrush(Colors.DodgerBlue)
                    : new SolidColorBrush(Colors.MediumSeaGreen),
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
                Foreground = ThemeManager.GetMutedForegroundBrush(),
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
                Foreground = colors.ForegroundBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(titleText, "新标签页");

            var suspendedIndicator = new TextBlock
            {
                Text = "💤",
                FontSize = 10,
                Foreground = ThemeManager.GetMutedForegroundBrush(),
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            var loadingRing = new ProgressRing
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                IsActive = true,
                RenderTransform = new ScaleTransform { ScaleX = 0.5, ScaleY = 0.5 },
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            var infoPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            infoPanel.Children.Add(engineMark);
            infoPanel.Children.Add(faviconPlaceholder);
            infoPanel.Children.Add(faviconImage);
            infoPanel.Children.Add(loadingRing);
            infoPanel.Children.Add(suspendedIndicator);
            infoPanel.Children.Add(titleText);
            Grid.SetColumn(infoPanel, 0);
            tabPanel.Children.Add(infoPanel);

            var closeBtn = new Button
            {
                Content = "\xE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = colors.MutedForegroundBrush,
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

            // 选中标签顶部强调条（Edge/Fluent 风格，高对比标识当前标签）
            var accentBar = new Border
            {
                Height = 2,
                VerticalAlignment = VerticalAlignment.Top,
                Background = colors.AccentBrush ?? new SolidColorBrush(Colors.DodgerBlue),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };

            var tabRoot = new Grid();
            tabRoot.Children.Add(tabPanel);
            tabRoot.Children.Add(accentBar);
            tabBorder.Child = tabRoot;

            _tabBarPanel.Children.Add(tabBorder);

            viewItem.Container = tabBorder;
            viewItem.TitleText = titleText;
            viewItem.CloseButton = closeBtn;
            viewItem.FaviconImage = faviconImage;
            viewItem.FaviconPlaceholder = faviconPlaceholder;
            viewItem.EngineMark = engineMark;
            viewItem.SuspendedIndicator = suspendedIndicator;
            viewItem.LoadingRing = loadingRing;
            viewItem.TabAccentBar = accentBar;
            closeBtn.Click += (s, e) => _ = CloseTabAsync(viewItem);
            }

            var contentBorder = new Border
            {
                Child = tab.ViewElement,
                Visibility = Visibility.Collapsed,
                Background = _contentContainer.Background
            };
            _contentContainer.Children.Add(contentBorder);
            viewItem.ContentContainer = contentBorder;
        }

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = CoreWindow.GetForCurrentThread()?.Dispatcher;
            if (dispatcher == null) return;
            _ = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => action());
        }

        private void BindTabEvents(TabViewItem viewItem)
        {
            var tab = viewItem.Tab;

            tab.TitleChanged += (title) =>
            {
                RunOnUiThread(() =>
                {
                    if (viewItem.TitleText == null) return;   // 无标签条模式（Metro）没有标题 UI
                    string displayTitle = string.IsNullOrEmpty(title) ? "新标签页" : title;
                    if (tab.IsSuspended) displayTitle = "[已挂起] " + displayTitle;
                    viewItem.TitleText.Text = displayTitle;
                    ToolTipService.SetToolTip(viewItem.TitleText, displayTitle);
                });
            };

            tab.UrlChanged += (url) =>
            {
                TabUrlChanged?.Invoke(tab, url);
                SessionManager.ScheduleSave(_tabs);
                if (!string.IsNullOrEmpty(url) && url != "about:blank")
                    _ = HistoryManager.AddAsync(tab.Title ?? url, url);
            };

            tab.CanGoBackChanged += (can) => TabCanGoBackChanged?.Invoke(tab, can);
            tab.CanGoForwardChanged += (can) => TabCanGoForwardChanged?.Invoke(tab, can);

            tab.FaviconChanged += (faviconUrl) => UpdateFavicon(viewItem, faviconUrl);

            tab.PageShortcutKeyPressed += (key) => PageKeyPressed?.Invoke(key);

            tab.PageMarkActionRequested += (payload) => PageMarkActionRequested?.Invoke(payload);

            tab.TtsTextRequested += (text) => TtsTextRequested?.Invoke(text);

            tab.NtpSearchRequested += (query) => NtpSearchRequested?.Invoke(query);

            tab.ContextMenuRequested += (args) => ContextMenuRequested?.Invoke(args);

            tab.NewWindowRequested += (url) => NewWindowRequested?.Invoke(url);

            tab.LoadingChanged += (isLoading) =>
            {
                RunOnUiThread(() =>
                {
                    if (viewItem.LoadingRing != null)
                        viewItem.LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;

                    if (viewItem.FaviconImage != null && viewItem.FaviconPlaceholder != null)
                    {
                        if (isLoading)
                        {
                            viewItem.FaviconImage.Visibility = Visibility.Collapsed;
                            viewItem.FaviconPlaceholder.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            if (viewItem.FaviconImage.Source != null)
                                viewItem.FaviconImage.Visibility = Visibility.Visible;
                            else
                                viewItem.FaviconPlaceholder.Visibility = Visibility.Visible;
                        }
                    }
                });
            };
        }

        private void UpdateFavicon(TabViewItem viewItem, string faviconUrl)
        {
            RunOnUiThread(() =>
            {
                    if (viewItem.FaviconPlaceholder == null || viewItem.FaviconImage == null) return;   // 无标签条模式（Metro）没有 favicon UI（网格卡片自行加载）
                    if (string.IsNullOrEmpty(faviconUrl) || !Uri.TryCreate(faviconUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                    {
                        viewItem.FaviconPlaceholder.Visibility = Visibility.Visible;
                        viewItem.FaviconImage.Visibility = Visibility.Collapsed;
                        viewItem.FaviconImage.Source = null;
                        return;
                    }
                    try
                    {
                        var bitmap = new BitmapImage { UriSource = uri };
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

        public void UpdateSuspendedState(IBrowserTab tab, bool suspended)
        {
            var viewItem = _tabs.FirstOrDefault(v => v.Tab == tab);
            if (viewItem == null) return;

            viewItem.IsSuspended = suspended;
            if (viewItem.SuspendedIndicator != null)
            {
                if (!viewItem.IsPinned)
                    viewItem.SuspendedIndicator.Visibility = suspended ? Visibility.Visible : Visibility.Collapsed;
            }
            string originalTitle = tab.Title ?? "新标签页";
            if (viewItem.TitleText != null)   // 无标签条模式（Metro）没有标题 UI
            {
                viewItem.TitleText.Text = suspended ? "[已挂起] " + originalTitle : originalTitle;
                ToolTipService.SetToolTip(viewItem.TitleText, viewItem.TitleText.Text);
            }
        }

        public async Task SwitchToTabAsync(TabViewItem viewItem)
        {
            if (viewItem == null || viewItem.Tab == _currentTab) return;

            var previousTab = _currentTab;
            _currentTab = viewItem.Tab;

            if (_currentTab.IsSuspended)
                await _currentTab.ResumeAsync();

            var colors = GetColors();
            if (colors != null)
            {
                foreach (var item in _tabs)
                {
                    if (item.Container != null)
                        item.Container.Background = (item == viewItem) ? colors.TabActiveBackground : colors.TabInactiveBackground;
                    if (item.TabAccentBar != null)
                        item.TabAccentBar.Visibility = (item == viewItem) ? Visibility.Visible : Visibility.Collapsed;
                    if (item.ContentContainer != null)
                        item.ContentContainer.Visibility = (item == viewItem) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            else
            {
                foreach (var item in _tabs)
                {
                    if (item.ContentContainer != null)
                        item.ContentContainer.Visibility = (item == viewItem) ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            if (_enableTabSuspend && previousTab != null && previousTab != _currentTab)
                await SuspendTabAsync(previousTab);

            TabChanged?.Invoke(viewItem);
        }

        public async Task CloseTabAsync(TabViewItem viewItem, bool addToClosed = true)
        {
            if (viewItem == null || !_tabs.Contains(viewItem)) return;

            // 修复：只在需要保存到关闭列表时获取 URL
            if (addToClosed)
            {
                string url = viewItem.Tab.CurrentUrl;
                if (!string.IsNullOrEmpty(url) && url != "about:blank")
                {
                    _closedTabs.Push((url, viewItem.Tab.Title ?? url, viewItem.Tab.Engine));
                    while (_closedTabs.Count > MaxClosedTabs)
                        _closedTabs.Pop();
                }
            }

            _tabs.Remove(viewItem);

            if (viewItem.Container != null && _tabBarPanel != null)
                _tabBarPanel.Children.Remove(viewItem.Container);
            if (viewItem.ContentContainer != null && _contentContainer != null)
                _contentContainer.Children.Remove(viewItem.ContentContainer);

            if (_currentTab == viewItem.Tab)
            {
                _currentTab = null;
                if (_tabs.Count > 0)
                    await SwitchToTabAsync(_tabs[0]);
            }

            viewItem.Tab.Dispose();
            CleanupSuspendTimer(viewItem.Tab);

            TabClosed?.Invoke(viewItem);
            TabCountChanged?.Invoke(_tabs.Count);
            SessionManager.ScheduleSave(_tabs);
        }

        public bool TryReopenClosedTab(out EngineType engine, out string url, out string title)
        {
            if (_closedTabs.Count > 0)
            {
                (url, title, engine) = _closedTabs.Pop();
                return true;
            }
            engine = EngineType.EdgeHtml;
            url = null;
            title = null;
            return false;
        }

        public void ClearClosedTabs() => _closedTabs.Clear();

        private async Task SuspendTabAsync(IBrowserTab tab)
        {
            if (tab is WebView2Tab wv2 && !wv2.IsSuspended)
            {
                if (_suspendTimers.ContainsKey(tab))
                    _suspendTimers[tab].Stop();

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SuspendDelaySeconds) };
                timer.Tick += async (s, e) =>
                {
                    timer.Stop();
                    var target = tab;
                    if (target == _currentTab || target.IsSuspended)
                    {
                        _suspendTimers.Remove(target);
                        return;
                    }

                    bool hasActiveDownload = DownloadManager.Downloads.Any(d =>
                        d.Status == DownloadStatus.Downloading ||
                        d.Status == DownloadStatus.Paused);
                    if (hasActiveDownload)
                    {
                        _suspendTimers.Remove(target);
                        return;
                    }

                    if (target is WebView2Tab wv2Target)
                    {
                        await wv2Target.SuspendAsync();
                        var vi = _tabs.FirstOrDefault(v => v.Tab == wv2Target);
                        if (vi != null)
                        {
                            vi.IsSuspended = true;
                            TabSuspendedChanged?.Invoke(target, true);
                        }
                    }
                    _suspendTimers.Remove(target);
                };
                _suspendTimers[tab] = timer;
                timer.Start();
            }
        }

        private void CleanupSuspendTimer(IBrowserTab tab)
        {
            if (_suspendTimers.ContainsKey(tab))
            {
                _suspendTimers[tab].Stop();
                _suspendTimers.Remove(tab);
            }
        }

        private void OnWebResourceRequestedForDNT(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestedEventArgs e)
        {
            e.Request.Headers.SetHeader("DNT", "1");
        }

        public void UpdateTheme(SkinManager.SkinColors colors)
        {
            foreach (var item in _tabs)
            {
                if (item.Container != null)
                {
                    item.Container.Background = (item.Tab == _currentTab)
                        ? colors.TabActiveBackground
                        : colors.TabInactiveBackground;
                }
                if (item.TabAccentBar != null)
                {
                    item.TabAccentBar.Visibility = (item.Tab == _currentTab) ? Visibility.Visible : Visibility.Collapsed;
                    if (colors.AccentBrush != null)
                        item.TabAccentBar.Background = colors.AccentBrush;
                }
                if (item.TitleText != null)
                    item.TitleText.Foreground = colors.ForegroundBrush;
                if (item.CloseButton != null)
                    item.CloseButton.Foreground = colors.MutedForegroundBrush;
                if (item.ContentContainer != null)
                    item.ContentContainer.Background = _contentContainer?.Background ?? new SolidColorBrush(Colors.White);
                if (item.FaviconPlaceholder != null)
                    item.FaviconPlaceholder.Foreground = colors.MutedForegroundBrush;
            }
        }

        public void ReorderTab(int fromIndex, int toIndex)
        {
            if (fromIndex == toIndex || fromIndex < 0 || toIndex < 0 ||
                fromIndex >= _tabs.Count || toIndex > _tabs.Count) return;

            var item = _tabs[fromIndex];
            _tabs.RemoveAt(fromIndex);
            int adjustedIndex = (toIndex > fromIndex) ? toIndex - 1 : toIndex;
            _tabs.Insert(adjustedIndex, item);

            TabOrderChanged?.Invoke();
            SessionManager.ScheduleSave(_tabs);
        }

        public int GetTabIndex(TabViewItem item) => _tabs.IndexOf(item);

        // ---- 固定标签支持 ----
        public async Task TogglePinTabAsync(TabViewItem viewItem)
        {
            if (viewItem == null) return;
            viewItem.IsPinned = !viewItem.IsPinned;

            if (viewItem.IsPinned)
            {
                viewItem.TitleText.Visibility = Visibility.Collapsed;
                viewItem.EngineMark.Visibility = Visibility.Collapsed;
                viewItem.SuspendedIndicator.Visibility = Visibility.Collapsed;
                viewItem.CloseButton.Visibility = Visibility.Collapsed;
                viewItem.Container.Width = PinnedTabWidth;
                viewItem.Container.MinWidth = PinnedTabWidth;
                viewItem.Container.MaxWidth = PinnedTabWidth;

                if (viewItem.FaviconImage.Source != null)
                {
                    viewItem.FaviconImage.Visibility = Visibility.Visible;
                    viewItem.FaviconPlaceholder.Visibility = Visibility.Collapsed;
                }
                else
                {
                    viewItem.FaviconImage.Visibility = Visibility.Collapsed;
                    viewItem.FaviconPlaceholder.Visibility = Visibility.Visible;
                    viewItem.FaviconPlaceholder.Glyph = "\uE75E";
                }

                ReorderTabToPinnedSection(viewItem);
            }
            else
            {
                viewItem.TitleText.Visibility = Visibility.Visible;
                viewItem.EngineMark.Visibility = Visibility.Visible;
                viewItem.SuspendedIndicator.Visibility = viewItem.Tab.IsSuspended ? Visibility.Visible : Visibility.Collapsed;
                viewItem.CloseButton.Visibility = Visibility.Visible;
                viewItem.Container.Width = LayoutManager.MaxTabWidth;
                viewItem.Container.MinWidth = LayoutManager.MinTabWidth;
                viewItem.Container.MaxWidth = LayoutManager.MaxTabWidth;
                viewItem.FaviconImage.Visibility = Visibility.Visible;
                viewItem.FaviconPlaceholder.Visibility = Visibility.Collapsed;

                string displayTitle = viewItem.Tab.Title ?? "新标签页";
                if (viewItem.Tab.IsSuspended)
                    displayTitle = "[已挂起] " + displayTitle;
                viewItem.TitleText.Text = displayTitle;
                ToolTipService.SetToolTip(viewItem.TitleText, displayTitle);
            }

            TabOrderChanged?.Invoke();
            TabCountChanged?.Invoke(_tabs.Count);
            SessionManager.ScheduleSave(_tabs);
        }

        private void ReorderTabToPinnedSection(TabViewItem viewItem)
        {
            if (viewItem == null || !viewItem.IsPinned) return;

            var pinnedTabs = _tabs.Where(t => t.IsPinned).ToList();
            if (pinnedTabs.Count <= 1) return;

            int pinnedEndIndex = _tabs.IndexOf(pinnedTabs.Last());
            int currentIndex = _tabs.IndexOf(viewItem);

            if (currentIndex < pinnedEndIndex)
            {
                _tabs.RemoveAt(currentIndex);
                _tabs.Insert(pinnedEndIndex, viewItem);
            }
            else if (currentIndex > pinnedEndIndex)
            {
                _tabs.RemoveAt(currentIndex);
                _tabs.Insert(pinnedEndIndex, viewItem);
            }
        }

        public int GetPinnedTabCount() => _tabs.Count(t => t.IsPinned);

        public IReadOnlyList<TabViewItem> GetPinnedTabs() => _tabs.Where(t => t.IsPinned).ToList();

        public bool IsAnyPinnedTab() => _tabs.Any(t => t.IsPinned);

        public void DisposeAll()
        {
            foreach (var item in _tabs)
                item.Tab.Dispose();
            _tabs.Clear();
            _currentTab = null;
            foreach (var timer in _suspendTimers.Values)
                timer.Stop();
            _suspendTimers.Clear();
        }
    }
}