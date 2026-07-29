using EdgeRebuild.Controls;
using EdgeRebuild.Core;
using EdgeRebuild.Services;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using TabViewItem = EdgeRebuild.Core.TabViewItem;

namespace EdgeRebuild
{
    public sealed partial class MainPage : Page
    {
        public static MainPage Instance { get; private set; }

        // ---- 字段 ----
        private bool _isLoaded;
        private string _pendingUrl;
        private EngineType _defaultEngine = EngineType.EdgeHtml;
        private double _zoomFactor = 1.0;

        // ---- 管理器 ----
        private TabManager _tabManager;
        private LayoutManager _layoutManager;
        private TabDragManager _dragManager;
        private TabContextMenuManager _contextMenuManager;
        private DownloadCoordinator _downloadCoordinator;
        private AppMenuManager _menuManager;

        // ---- 拖拽状态 ----
        private bool _isDragging;

        public MainPage()
        {
            Instance = this;
            this.InitializeComponent();
            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            CleanupStaticEvents();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CleanupStaticEvents();
            _downloadCoordinator?.CleanupAll();
            Window.Current.SizeChanged -= OnWindowSizeChanged;
            ShortcutManager.Disable();
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _layoutManager?.Dispose();
            _tabManager?.DisposeAll();
            _dragManager?.Dispose();
        }

        private void CleanupStaticEvents()
        {
            DownloadManager.DownloadProgressChanged -= OnDownloadProgress;
            DownloadManager.DownloadStatusChanged -= OnDownloadStatusChanged;
            DownloadManager.RetryDownloadRequested -= OnRetryDownloadRequested;
            EdgeHtmlTab.DownloadRequested -= OnEdgeHtmlDownloadRequested;
            SettingsManager.SettingChanged -= OnSettingChanged;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;

            try
            {
                // ---- 布局管理器 ----
                _layoutManager = new LayoutManager(
                    TabBarBorder,
                    TabScrollViewer,
                    TabBarPanel,
                    RightSidePanel,
                    ScrollLeftBtn,
                    ScrollRightBtn
                );
                _layoutManager.LayoutUpdated += OnLayoutUpdated;

                // ---- 标签管理器 ----
                _tabManager = new TabManager();
                _tabManager.Initialize(TabBarPanel, ContentContainer);
                _tabManager.TabOrderChanged += OnTabOrderChanged;
                _tabManager.TabCreated += OnTabManagerTabCreated;
                _tabManager.TabChanged += OnTabManagerTabChanged;
                _tabManager.TabCountChanged += OnTabManagerTabCountChanged;
                _tabManager.TabUrlChanged += OnTabManagerTabUrlChanged;
                _tabManager.TabCanGoBackChanged += OnTabManagerCanGoBackChanged;
                _tabManager.TabCanGoForwardChanged += OnTabManagerCanGoForwardChanged;
                _tabManager.TabSuspendedChanged += OnTabManagerTabSuspendedChanged;
                _tabManager.NewWindowRequested += OnTabManagerNewWindowRequested;
                _tabManager.ContextMenuRequested += OnTabManagerContextMenuRequested;

                // ---- 拖拽管理器 ----
                _dragManager = new TabDragManager(_tabManager, TabBarPanel, DragCanvas, _layoutManager);
                _dragManager.SortRequested += OnDragSortRequested;
                _dragManager.DetachRequested += OnDragDetachRequested;
                _dragManager.DragStarted += () => _isDragging = true;
                _dragManager.DragCompleted += () => { _isDragging = false; UpdateTabLayout(); };
                _dragManager.TabRightTapped += (border, viewItem) =>
                {
                    _contextMenuManager?.ShowTabRightClickMenu(border, viewItem);
                };

                // ---- 上下文菜单管理器 ----
                _contextMenuManager = new TabContextMenuManager(
                    _tabManager,
                    _defaultEngine,
                    () => _tabManager.CurrentTab?.Engine ?? _defaultEngine,
                    PrintCurrentPageAsync,
                    ViewSourceAsync
                );

                // ---- 下载协调器 ----
                _downloadCoordinator = new DownloadCoordinator(RootGrid);
                await _downloadCoordinator.InitializeAsync();

                // ---- 应用菜单管理器 ----
                _menuManager = new AppMenuManager(
                    _tabManager,
                    _defaultEngine,
                    () => _tabManager.CurrentTab?.Engine ?? _defaultEngine,
                    PrintCurrentPageAsync,
                    ViewSourceAsync,
                    () => { HubSplitView.IsPaneOpen = true; hubPane.ShowHistory(); },
                    () => SettingsSplitView.IsPaneOpen = !SettingsSplitView.IsPaneOpen,
                    ClearBrowsingDataAsync,
                    BookmarkAllTabs,
                    ShowAboutDialog,
                    () => { _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank"); },
                    AdjustZoom,
                    ResetZoom,
                    FindOnPage,
                    OpenDevTools,
                    ToolbarControl_EngineSwitched,
                    async () => { if (_tabManager.TryReopenClosedTab(out EngineType engine, out string url, out _)) await _tabManager.CreateTabAsync(engine, url); },
                    async () => await _tabManager.CreateTabAsync(_defaultEngine, "about:blank"),
                    () => _tabManager.ClearClosedTabs(),
                    () => _tabManager.TryReopenClosedTab(out _, out _, out _)
                );

                // ---- 为所有标签附加拖拽事件 ----
                foreach (var tab in _tabManager.Tabs)
                {
                    _dragManager.AttachEvents(tab);
                }
                _tabManager.TabCreated += viewItem => _dragManager.AttachEvents(viewItem);

                // ---- 窗口设置 ----
                CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
                Window.Current.SetTitleBar(TitleBarDragArea);
                ThemeManager.UpdateTitleBarColors();

                Window.Current.SizeChanged += OnWindowSizeChanged;

                ThemeManager.ThemeChanged += OnThemeChanged;
                ApplyTheme();

                // ---- 加载设置 ----
                string askSetting = await SettingsManager.GetAsync("AskBeforeDownload");
                _downloadCoordinator.UpdateAskSetting(askSetting == "True" || askSetting == "true");

                string engineSetting = await SettingsManager.GetAsync("DefaultEngine");
                _defaultEngine = (engineSetting == "WebView2") ? EngineType.WebView2 : EngineType.EdgeHtml;

                string downloadPath = await SettingsManager.GetAsync("DownloadFolder");
                if (!string.IsNullOrEmpty(downloadPath))
                {
                    DownloadManager.SetCustomDownloadPath(downloadPath);
                }

                await DownloadManager.UpdateSystemDownloadFolderAccessAsync();
                await DownloadManager.LoadDownloadsAsync();

                DownloadManager.DownloadProgressChanged += OnDownloadProgress;
                DownloadManager.DownloadStatusChanged += OnDownloadStatusChanged;
                DownloadManager.RetryDownloadRequested += OnRetryDownloadRequested;
                EdgeHtmlTab.DownloadRequested += OnEdgeHtmlDownloadRequested;
                SettingsManager.SettingChanged += OnSettingChanged;

                // ---- 创建首页标签 ----
                string homeUrl = await SettingsManager.GetAsync("HomePage") ?? "about:blank";
                if (_tabManager.TabCount == 0)
                {
                    if (!string.IsNullOrEmpty(_pendingUrl))
                        await _tabManager.CreateTabAsync(_defaultEngine, _pendingUrl);
                    else
                        await _tabManager.CreateTabAsync(_defaultEngine, homeUrl);
                }

                // ---- 快捷键 ----
                ShortcutManager.Enable();
                ShortcutManager.OnNewTab += async () =>
                {
                    await _tabManager.CreateTabAsync(_defaultEngine, "about:blank");
                    toolbarControl.FocusAddressBarAndClear();
                };
                ShortcutManager.OnCloseTab += () =>
                {
                    if (_tabManager.CurrentTab != null)
                    {
                        var tabItem = _tabManager.Tabs.FirstOrDefault(v => v.Tab == _tabManager.CurrentTab);
                        if (tabItem != null)
                            _ = _tabManager.CloseTabAsync(tabItem);
                    }
                };
                ShortcutManager.OnReopenTab += async () =>
                {
                    if (_tabManager.TryReopenClosedTab(out EngineType engine, out string url, out _))
                        await _tabManager.CreateTabAsync(engine, url);
                };
                ShortcutManager.OnFocusAddressBar += () => toolbarControl.FocusAddressBarAndClear();
                ShortcutManager.OnRefresh += () => _tabManager.CurrentTab?.RefreshAsync();
                ShortcutManager.OnAddFavorite += () => ToolbarControl_AddFavoriteClicked();
                ShortcutManager.OnShowHistory += () =>
                {
                    HubSplitView.IsPaneOpen = true;
                    hubPane.ShowHistory();
                };
                ShortcutManager.OnShowDownloads += () =>
                {
                    HubSplitView.IsPaneOpen = true;
                    hubPane.ShowDownloads();
                };
                ShortcutManager.OnFind += () => FindOnPage();
                ShortcutManager.OnPrint += () => _ = PrintCurrentPageAsync();
                ShortcutManager.OnNextTab += () =>
                {
                    SwitchToNextTab();
                    SetFocusToCurrentTabContent();
                };
                ShortcutManager.OnPreviousTab += () =>
                {
                    SwitchToPreviousTab();
                    SetFocusToCurrentTabContent();
                };
                ShortcutManager.OnSwitchToTab += (index) =>
                {
                    SwitchToTabByIndex(index);
                    SetFocusToCurrentTabContent();
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OnLoaded] 异常: {ex}");
                await new ContentDialog
                {
                    Title = "界面初始化失败",
                    Content = ex.ToString(),
                    CloseButtonText = "确定"
                }.ShowAsync();
            }
        }

        // ---- TabManager 事件处理 ----
        private void OnTabOrderChanged() => SyncTabPanelOrder();

        private void OnTabManagerTabCreated(TabViewItem viewItem)
        {
            UpdateTabLayout();
            toolbarControl.FocusAddressBarAndClear();
        }

        private void OnTabManagerTabChanged(TabViewItem viewItem)
        {
            var tab = viewItem.Tab;
            toolbarControl.SetEngine(tab.Engine);
            toolbarControl.UpdateNavState(tab.CanGoBack, tab.CanGoForward, tab.CurrentUrl);
            UpdateStarButton();

            bool isSecure = tab.CurrentUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            string host = "";
            try { host = new Uri(tab.CurrentUrl).Host; } catch { }
            toolbarControl.UpdateSiteInfo(isSecure, host);
        }

        private void OnTabManagerTabCountChanged(int count)
        {
            UpdateTabLayout();
            if (count == 0)
                _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank");
        }

        private void OnTabManagerTabUrlChanged(IBrowserTab tab, string url)
        {
            if (_tabManager.CurrentTab == tab)
            {
                toolbarControl.UpdateNavState(tab.CanGoBack, tab.CanGoForward, url);
                UpdateStarButton();

                bool isSecure = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                string host = "";
                try { host = new Uri(url).Host; } catch { }
                toolbarControl.UpdateSiteInfo(isSecure, host);
            }
        }

        private void OnTabManagerCanGoBackChanged(IBrowserTab tab, bool canGoBack)
        {
            if (_tabManager.CurrentTab == tab)
                toolbarControl.UpdateNavState(canGoBack, tab.CanGoForward, tab.CurrentUrl);
        }

        private void OnTabManagerCanGoForwardChanged(IBrowserTab tab, bool canGoForward)
        {
            if (_tabManager.CurrentTab == tab)
                toolbarControl.UpdateNavState(tab.CanGoBack, canGoForward, tab.CurrentUrl);
        }

        private void OnTabManagerTabSuspendedChanged(IBrowserTab tab, bool suspended)
        {
            _tabManager.UpdateSuspendedState(tab, suspended);
        }

        private void OnTabManagerNewWindowRequested(string url)
        {
            _ = _tabManager.CreateTabAsync(_defaultEngine, url);
        }

        private void OnTabManagerContextMenuRequested(TabContextMenuEventArgs args)
        {
            _contextMenuManager.ShowWebContextMenu(args);
        }

        // ---- 拖拽管理器事件 ----
        private void OnDragSortRequested(int fromIndex, int toIndex)
        {
            if (fromIndex >= 0 && toIndex >= 0)
            {
                _tabManager.ReorderTab(fromIndex, toIndex);
            }
            else
            {
                SyncTabPanelOrder();
            }
        }

        private async void OnDragDetachRequested(TabViewItem item)
        {
            string url = item.Tab.CurrentUrl;
            EngineType engine = item.Tab.Engine;
            if (string.IsNullOrWhiteSpace(url) || url == "about:blank")
                url = "https://www.bing.com";
            await _tabManager.CreateTabAsync(engine, url);
            await _tabManager.CloseTabAsync(item);
        }

        // ---- 导航辅助 ----
        private void SwitchToNextTab()
        {
            var tabs = _tabManager.Tabs;
            if (tabs.Count <= 1) return;
            int index = tabs.ToList().IndexOf(tabs.First(v => v.Tab == _tabManager.CurrentTab));
            int nextIndex = (index + 1) % tabs.Count;
            _ = _tabManager.SwitchToTabAsync(tabs[nextIndex]);
        }

        private void SwitchToPreviousTab()
        {
            var tabs = _tabManager.Tabs;
            if (tabs.Count <= 1) return;
            int index = tabs.ToList().IndexOf(tabs.First(v => v.Tab == _tabManager.CurrentTab));
            int prevIndex = (index - 1 + tabs.Count) % tabs.Count;
            _ = _tabManager.SwitchToTabAsync(tabs[prevIndex]);
        }

        private void SwitchToTabByIndex(int index)
        {
            var tabs = _tabManager.Tabs;
            if (index >= 0 && index < tabs.Count)
                _ = _tabManager.SwitchToTabAsync(tabs[index]);
        }

        private void SetFocusToCurrentTabContent()
        {
            if (_tabManager.CurrentTab != null && _tabManager.CurrentTab.ViewElement is Control control)
                control.Focus(FocusState.Programmatic);
        }

        // ---- 布局更新 ----
        private void OnLayoutUpdated()
        {
            double tabWidth = _layoutManager.TabWidth;
            foreach (var item in _tabManager.Tabs)
            {
                if (item.Container != null)
                    item.Container.Width = tabWidth;
            }
        }

        private void UpdateTabLayout()
        {
            if (!_isLoaded || _tabManager.TabCount == 0 || _isDragging) return;
            _layoutManager.Recalculate(TabBarBorder.ActualWidth, _tabManager.TabCount);
        }

        private void SyncTabPanelOrder()
        {
            TabBarPanel.Children.Clear();
            foreach (var item in _tabManager.Tabs)
            {
                if (item.Container != null)
                    TabBarPanel.Children.Add(item.Container);
            }
        }

        private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            if (!_isDragging)
                UpdateTabLayout();
        }

        private void TabBarBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isDragging)
                UpdateTabLayout();
        }

        private void ScrollLeftBtn_Click(object sender, RoutedEventArgs e) => _layoutManager?.ScrollToLeft();
        private void ScrollRightBtn_Click(object sender, RoutedEventArgs e) => _layoutManager?.ScrollToRight();
        private void TabScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) { }

        // ---- 主题 ----
        private void OnThemeChanged(SkinManager.SkinColors colors, bool isDark) => ApplyTheme();

        private void ApplyTheme()
        {
            var colors = ThemeManager.Colors;
            if (colors == null) return;

            bool isDark = ThemeManager.IsDarkTheme;

            TabBarBorder.Background = colors.ToolbarBackground;
            toolbarControl.ApplySkin(colors, isDark);
            HubSplitView.PaneBackground = colors.ToolbarBackground;
            SettingsSplitView.PaneBackground = colors.ToolbarBackground;

            hubPane.ApplySkinColors(colors.ForegroundBrush, colors.MutedForegroundBrush, isDark);
            SettingsPaneControl.ApplySkinColors(colors.ToolbarBackground);

            ContentContainer.Background = isDark
                ? new SolidColorBrush(Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B))
                : new SolidColorBrush(Colors.White);

            _tabManager?.UpdateTheme(colors);

            ThemeManager.UpdateTitleBarColors();
            UpdateTabLayout();
        }

        // ---- 工具栏事件 ----
        private async void ToolbarControl_UrlSubmitted(string url)
        {
            if (_tabManager.CurrentTab != null)
            {
                await _tabManager.CurrentTab.NavigateAsync(url);
                if (!string.IsNullOrEmpty(url) && url != "about:blank")
                    await HistoryManager.AddAsync(_tabManager.CurrentTab.Title ?? url, url);
            }
        }

        private async void ToolbarControl_BackRequested() { if (_tabManager.CurrentTab?.CanGoBack == true) await _tabManager.CurrentTab.GoBackAsync(); }
        private async void ToolbarControl_ForwardRequested() { if (_tabManager.CurrentTab?.CanGoForward == true) await _tabManager.CurrentTab.GoForwardAsync(); }
        private async void ToolbarControl_RefreshRequested() { if (_tabManager.CurrentTab != null) await _tabManager.CurrentTab.RefreshAsync(); }

        private async void ToolbarControl_AddFavoriteClicked()
        {
            if (_tabManager.CurrentTab == null || string.IsNullOrEmpty(_tabManager.CurrentTab.CurrentUrl)) return;
            string url = _tabManager.CurrentTab.CurrentUrl;
            if (FavoritesManager.Instance.ContainsUrl(url))
            {
                var item = FavoritesManager.Instance.Favorites.FirstOrDefault(f => f.Url == url);
                if (item != null) await FavoritesManager.Instance.RemoveAsync(item);
            }
            else
                await FavoritesManager.Instance.AddAsync(!string.IsNullOrEmpty(_tabManager.CurrentTab.Title) ? _tabManager.CurrentTab.Title : url, url);
            UpdateStarButton();
            if (HubSplitView.IsPaneOpen) hubPane.RefreshFavorites();
        }

        private void ToolbarControl_HubClicked()
        {
            HubSplitView.IsPaneOpen = !HubSplitView.IsPaneOpen;
            if (HubSplitView.IsPaneOpen) hubPane.RefreshAll();
        }

        private void ToolbarControl_MenuClicked()
        {
            var menu = _menuManager.BuildMenu();
            toolbarControl.ShowMenu(menu);
        }

        private void UpdateStarButton()
        {
            if (_tabManager.CurrentTab != null)
                toolbarControl.UpdateFavoriteButton(FavoritesManager.Instance.ContainsUrl(_tabManager.CurrentTab.CurrentUrl));
        }

        // ---- 引擎切换 ----
        private async void ToolbarControl_EngineSwitched(EngineType newEngine)
        {
            try
            {
                if (_tabManager.CurrentTab == null || _tabManager.CurrentTab.Engine == newEngine) return;
                var viewItem = _tabManager.Tabs.FirstOrDefault(v => v.Tab == _tabManager.CurrentTab);
                if (viewItem == null) return;
                string currentUrl = _tabManager.CurrentTab.CurrentUrl;
                _tabManager.CurrentTab.Dispose();

                await _tabManager.CloseTabAsync(viewItem, false);
                await _tabManager.CreateTabAsync(newEngine, currentUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"引擎切换失败: {ex}");
                await new ContentDialog
                {
                    Title = "引擎切换失败",
                    Content = ex.Message,
                    CloseButtonText = "确定"
                }.ShowAsync();
            }
        }

        // ---- 设置变化 ----
        private async void OnSettingChanged(string key, string newValue)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                switch (key)
                {
                    case "AskBeforeDownload":
                        _downloadCoordinator?.UpdateAskSetting(newValue == "True" || newValue == "true");
                        break;
                    case "DefaultEngine":
                        _defaultEngine = (newValue == "WebView2") ? EngineType.WebView2 : EngineType.EdgeHtml;
                        break;
                    case "DownloadFolder":
                        DownloadManager.SetCustomDownloadPath(newValue);
                        break;
                    case "DoNotTrack":
                        foreach (var item in _tabManager.Tabs)
                        {
                            if (item.Tab is WebView2Tab wv2)
                            {
                                await wv2.EnsureInitializedAsync();
                                if (newValue == "True" || newValue == "true")
                                    wv2.CoreWebView2.WebResourceRequested += OnWebResourceRequestedForDNT;
                                else
                                    wv2.CoreWebView2.WebResourceRequested -= OnWebResourceRequestedForDNT;
                            }
                        }
                        break;
                }
            });
        }

        private void OnWebResourceRequestedForDNT(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            e.Request.Headers.SetHeader("DNT", "1");
        }

        // ---- 下载事件 ----
        private void OnDownloadProgress(DownloadItem item) =>
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => hubPane.UpdateDownloadItem(item));

        private void OnDownloadStatusChanged(DownloadItem item) =>
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => hubPane.UpdateDownloadItem(item));

        private async void OnRetryDownloadRequested(DownloadItem item)
        {
            await _downloadCoordinator.RetryDownload(item);
        }

        private async void OnEdgeHtmlDownloadRequested(string url)
        {
            await _downloadCoordinator.HandleEdgeHtmlDownloadRequested(url);
        }

        // ---- 其他功能方法 ----
        private async Task PrintCurrentPageAsync()
        {
            if (_tabManager.CurrentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                wv2.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
            else if (_tabManager.CurrentTab is EdgeHtmlTab edgeTab)
                await edgeTab.ExecuteScriptAsync("window.print();");
        }

        private void FindOnPage()
        {
            if (_tabManager.CurrentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                _ = wv2.CoreWebView2.ExecuteScriptAsync("window.find('');");
            else
                ShowNotImplementedDialog("查找（请切换到 WebView2）");
        }

        private async Task ViewSourceAsync()
        {
            if (_tabManager.CurrentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
            {
                try
                {
                    var html = await wv2.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML;");
                    html = html?.Trim('"').Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t");
                    await new ContentDialog
                    {
                        Title = "页面源代码",
                        Content = new ScrollViewer
                        {
                            Content = new TextBlock
                            {
                                Text = html,
                                FontSize = 10,
                                IsTextSelectionEnabled = true
                            }
                        },
                        PrimaryButtonText = "关闭"
                    }.ShowAsync();
                }
                catch (Exception ex)
                {
                    await new ContentDialog
                    {
                        Title = "错误",
                        Content = $"获取源代码失败：{ex.Message}",
                        CloseButtonText = "确定"
                    }.ShowAsync();
                }
            }
            else
            {
                ShowNotImplementedDialog("查看源代码（请切换到 WebView2）");
            }
        }

        private void OpenDevTools()
        {
            if (_tabManager.CurrentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                wv2.CoreWebView2.OpenDevToolsWindow();
            else
                ShowNotImplementedDialog("开发者工具（请切换到 WebView2）");
        }

        private void AdjustZoom(double delta)
        {
            _zoomFactor = Math.Max(0.25, Math.Min(5.0, _zoomFactor + delta));
            if (_tabManager.CurrentTab is EdgeHtmlTab edgeTab)
                _ = edgeTab.ExecuteScriptAsync($"document.body.style.zoom = '{_zoomFactor}';");
            else if (_tabManager.CurrentTab is WebView2Tab wv2)
                _ = wv2.ExecuteScriptAsync($"document.body.style.zoom = '{_zoomFactor}';");
        }

        private void ResetZoom()
        {
            _zoomFactor = 1.0;
            if (_tabManager.CurrentTab is EdgeHtmlTab edgeTab)
                _ = edgeTab.ExecuteScriptAsync("document.body.style.zoom = '1';");
            else if (_tabManager.CurrentTab is WebView2Tab wv2)
                _ = wv2.ExecuteScriptAsync("document.body.style.zoom = '1';");
        }

        private async void BookmarkAllTabs()
        {
            int count = 0;
            foreach (var item in _tabManager.Tabs)
            {
                string url = item.Tab.CurrentUrl;
                if (!string.IsNullOrEmpty(url) && url != "about:blank" && !FavoritesManager.Instance.ContainsUrl(url))
                {
                    await FavoritesManager.Instance.AddAsync(string.IsNullOrEmpty(item.Tab.Title) ? url : item.Tab.Title, url);
                    count++;
                }
            }
            UpdateStarButton();
            if (HubSplitView.IsPaneOpen)
                hubPane.RefreshFavorites();
            await new ContentDialog
            {
                Title = "完成",
                Content = $"已将 {count} 个标签页加入收藏。",
                CloseButtonText = "确定"
            }.ShowAsync();
        }

        private async Task ClearBrowsingDataAsync()
        {
            await HistoryManager.ClearAsync();
            DownloadManager.ClearCompleted();
            if (HubSplitView.IsPaneOpen)
                hubPane.RefreshAll();
            await ClearAllCookiesInternalAsync();
            await new ContentDialog
            {
                Title = "已清除",
                Content = "浏览数据已清除。",
                CloseButtonText = "确定"
            }.ShowAsync();
        }

        public async Task ClearAllCookiesInternalAsync()
        {
            foreach (var item in _tabManager.Tabs)
            {
                if (item.Tab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                {
                    wv2.CoreWebView2.Profile.CookieManager.DeleteAllCookies();
                }
            }
        }

        private void ShowAboutDialog()
        {
            _ = new ContentDialog
            {
                Title = "Edge Rebuild",
                Content = "版本 0.2 Alpha\n基于 UWP 的双内核浏览器外壳。",
                CloseButtonText = "确定"
            }.ShowAsync();
        }

        private async void ShowNotImplementedDialog(string feature)
        {
            await new ContentDialog
            {
                Title = "即将推出",
                Content = $"功能“{feature}”尚未实现。",
                CloseButtonText = "确定"
            }.ShowAsync();
        }

        private void HubPane_NavigateRequested(string url)
        {
            if (_tabManager.CurrentTab != null)
                _ = _tabManager.CurrentTab.NavigateAsync(url);
            HubSplitView.IsPaneOpen = false;
        }

        private void NewTabBtn_Click(object sender, RoutedEventArgs e) => _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank");

        private void SettingsPane_CloseRequested(object sender, EventArgs e) => SettingsSplitView.IsPaneOpen = false;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string url)
                _pendingUrl = url;
        }
    }
}