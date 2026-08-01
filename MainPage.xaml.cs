using EdgeRebuild.Controls;
using EdgeRebuild.Core;
using EdgeRebuild.Services;
using Microsoft.UI.Xaml.Controls;
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
using Windows.UI.WindowManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.System;

using TabViewItem = EdgeRebuild.Core.TabViewItem;

namespace EdgeRebuild
{
    public sealed partial class MainPage : Page
    {
        // ---- 字段 ----
        private bool _isLoaded;
        private string _pendingUrl;
        private EngineType? _pendingEngine;
        private EngineType _defaultEngine = EngineType.EdgeHtml;
        private double _zoomFactor = 1.0;
        private bool _isEngineSwitching = false;
        // ---- 管理器 ----
        private TabManager _tabManager;
        private LayoutManager _layoutManager;
        private TabDragManager _dragManager;
        private TabContextMenuManager _contextMenuManager;
        private DownloadCoordinator _downloadCoordinator;
        private AppMenuManager _menuManager;

        // ---- 拖拽状态 ----
        private bool _isDragging;
        private bool _isNewWindow;

        // ---- 阅读模式：tab -> 原始 URL ----
        private readonly Dictionary<IBrowserTab, string> _readerModeUrls = new Dictionary<IBrowserTab, string>();

        // ---- 辅助窗口引用（AppWindow；主窗口为 null）----
        private AppWindow _appWindow;

        // ---- 快捷键处理器引用（用于跨窗口守卫和反注册） ----
        private Action _shortcutNewTab, _shortcutCloseTab, _shortcutReopenTab, _shortcutFocusAddressBar,
            _shortcutRefresh, _shortcutAddFavorite, _shortcutShowHistory, _shortcutShowDownloads,
            _shortcutFind, _shortcutPrint, _shortcutNextTab, _shortcutPreviousTab;
        private Action<int> _shortcutSwitchToTab;

        public MainPage()
        {
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
            if (_isNewWindow) Services.Diagnostics.Log("[窗口] OnUnloaded 触发（窗口被关闭）");
            CleanupStaticEvents();
            _ = SessionManager.FlushAsync();
            _downloadCoordinator?.CleanupAll();
            if (this.XamlRoot != null) this.XamlRoot.Changed -= OnXamlRootChanged;
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

            ShortcutManager.OnNewTab -= _shortcutNewTab;
            ShortcutManager.OnCloseTab -= _shortcutCloseTab;
            ShortcutManager.OnReopenTab -= _shortcutReopenTab;
            ShortcutManager.OnFocusAddressBar -= _shortcutFocusAddressBar;
            ShortcutManager.OnRefresh -= _shortcutRefresh;
            ShortcutManager.OnAddFavorite -= _shortcutAddFavorite;
            ShortcutManager.OnShowHistory -= _shortcutShowHistory;
            ShortcutManager.OnShowDownloads -= _shortcutShowDownloads;
            ShortcutManager.OnFind -= _shortcutFind;
            ShortcutManager.OnPrint -= _shortcutPrint;
            ShortcutManager.OnNextTab -= _shortcutNextTab;
            ShortcutManager.OnPreviousTab -= _shortcutPreviousTab;
            ShortcutManager.OnSwitchToTab -= _shortcutSwitchToTab;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            if (_isNewWindow) Services.Diagnostics.Log("[窗口] OnLoaded 开始");

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
                _tabManager.TabClosed += OnTabManagerTabClosedCleanup;
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
                _dragManager.DragStarted += () => { _isDragging = true; Services.Diagnostics.Log("[拖拽] DragStarted 事件"); };
                _dragManager.DragCompleted += () => { _isDragging = false; UpdateTabLayout(); Services.Diagnostics.Log("[拖拽] DragCompleted 事件"); };
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
                    ViewSourceAsync,
                    (viewItem) => _ = _tabManager.TogglePinTabAsync(viewItem)
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
                    async () => { if (!await WindowManager.OpenNewWindowAsync(_defaultEngine, "about:blank")) NotificationService.ShowToast("无法打开新窗口", "创建窗口失败"); },
                    AdjustZoom,
                    ResetZoom,
                    FindOnPage,
                    OpenDevTools,
                    ToolbarControl_EngineSwitched,
                    async () => { if (_tabManager.TryReopenClosedTab(out EngineType engine, out string url, out _)) await _tabManager.CreateTabAsync(engine, url); },
                    async () => await _tabManager.CreateTabAsync(_defaultEngine, "about:blank"),
                    () => _tabManager.ClearClosedTabs(),
                    () => _tabManager.TryReopenClosedTab(out _, out _, out _),
                    CopyCurrentUrl,
                    () => _tabManager.CurrentTab != null && _readerModeUrls.ContainsKey(_tabManager.CurrentTab),
                    ToggleReaderMode
                );

                // ---- 为所有标签附加拖拽事件 ----
                foreach (var tab in _tabManager.Tabs)
                {
                    _dragManager.AttachEvents(tab);
                }
                _tabManager.TabCreated += viewItem => _dragManager.AttachEvents(viewItem);

                // ---- 窗口设置 ----
                if (_appWindow != null)
                {
                    // 子窗口（AppWindow）：恢复自定义标题栏外观（扩展标题栏）。
                    // 注意：UWP AppWindow 无 SetTitleBar/SetDragRectangles，
                    // 系统把顶部条带设为默认拖拽区并吞掉该区域所有指针输入，
                    // 因此子窗口标签栏内的按钮点击无效——先隐藏新建标签按钮；
                    // 标签切换/关闭请用快捷键（Ctrl+Tab / Ctrl+W）。
                    try { _appWindow.Title = "EdgeRebuild"; } catch { }
                    _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                    ApplyAppWindowTitleBarColors();
                    NewTabButton.Visibility = Visibility.Collapsed;
                    this.KeyDown += OnWindowKeyDown;
                }
                else
                {
                    // 主窗口：扩展标题栏 + 自定义拖拽区域
                    CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
                    Window.Current.SetTitleBar(TitleBarDragArea);
                    ThemeManager.UpdateTitleBarColors();
                }

                this.XamlRoot.Changed += OnXamlRootChanged;

                ThemeManager.ThemeChanged += OnThemeChanged;
                ApplyTheme();

                // ---- 加载设置 ----
                string askSetting = await SettingsManager.GetAsync("AskBeforeDownload");
                _downloadCoordinator.UpdateAskSetting(askSetting == "True" || askSetting == "true");

                string engineSetting = await SettingsManager.GetAsync("DefaultEngine");
                if (_pendingEngine == null)
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

                // ---- 创建首页标签 / 恢复会话 ----
                string homeUrl = await SettingsManager.GetAsync("HomePage") ?? "about:blank";
                if (_tabManager.TabCount == 0)
                {
                    if (!string.IsNullOrEmpty(_pendingUrl))
                    {
                        await _tabManager.CreateTabAsync(_defaultEngine, _pendingUrl);
                    }
                    else
                    {
                        string restoreSetting = await SettingsManager.GetAsync("RestoreSession") ?? "True";
                        bool restoreSession = (restoreSetting == "True" || restoreSetting == "true");
                        var sessionTabs = await SessionManager.LoadTabsAsync();

                        if (restoreSession && sessionTabs.Count > 0)
                        {
                            foreach (var rec in sessionTabs)
                            {
                                if (string.IsNullOrEmpty(rec.Url)) continue;
                                EngineType engine = (rec.Engine == "WebView2") ? EngineType.WebView2 : EngineType.EdgeHtml;
                                await _tabManager.CreateTabAsync(engine, rec.Url, rec.IsPinned);
                            }
                        }
                        else
                        {
                            await _tabManager.CreateTabAsync(_defaultEngine, homeUrl);
                        }
                    }
                }

                // ---- 快捷键 ----
                ShortcutManager.Enable();

                _shortcutNewTab = () =>
                {
                    if (!IsFocusedWindow()) return;
                    _ = CreateNewTabAndFocusAsync();
                };
                _shortcutCloseTab = () =>
                {
                    if (!IsFocusedWindow()) return;
                    if (_tabManager.CurrentTab != null)
                    {
                        var tabItem = _tabManager.Tabs.FirstOrDefault(v => v.Tab == _tabManager.CurrentTab);
                        if (tabItem != null)
                            _ = _tabManager.CloseTabAsync(tabItem);
                    }
                };
                _shortcutReopenTab = async () =>
                {
                    if (!IsFocusedWindow()) return;
                    if (_tabManager.TryReopenClosedTab(out EngineType engine, out string url, out _))
                        await _tabManager.CreateTabAsync(engine, url);
                };
                _shortcutFocusAddressBar = () =>
                {
                    if (!IsFocusedWindow()) return;
                    toolbarControl.FocusAddressBarAndClear();
                };
                _shortcutRefresh = () =>
                {
                    if (!IsFocusedWindow()) return;
                    _ = _tabManager.CurrentTab?.RefreshAsync();
                };
                _shortcutAddFavorite = () =>
                {
                    if (!IsFocusedWindow()) return;
                    ToolbarControl_AddFavoriteClicked();
                };
                _shortcutShowHistory = () =>
                {
                    if (!IsFocusedWindow()) return;
                    HubSplitView.IsPaneOpen = true;
                    hubPane.ShowHistory();
                };
                _shortcutShowDownloads = () =>
                {
                    if (!IsFocusedWindow()) return;
                    HubSplitView.IsPaneOpen = true;
                    hubPane.ShowDownloads();
                };
                _shortcutFind = () =>
                {
                    if (!IsFocusedWindow()) return;
                    FindOnPage();
                };
                _shortcutPrint = () =>
                {
                    if (!IsFocusedWindow()) return;
                    _ = PrintCurrentPageAsync();
                };
                _shortcutNextTab = () =>
                {
                    if (!IsFocusedWindow()) return;
                    SwitchToNextTab();
                    SetFocusToCurrentTabContent();
                };
                _shortcutPreviousTab = () =>
                {
                    if (!IsFocusedWindow()) return;
                    SwitchToPreviousTab();
                    SetFocusToCurrentTabContent();
                };
                _shortcutSwitchToTab = (index) =>
                {
                    if (!IsFocusedWindow()) return;
                    SwitchToTabByIndex(index);
                    SetFocusToCurrentTabContent();
                };

                ShortcutManager.OnNewTab += _shortcutNewTab;
                ShortcutManager.OnCloseTab += _shortcutCloseTab;
                ShortcutManager.OnReopenTab += _shortcutReopenTab;
                ShortcutManager.OnFocusAddressBar += _shortcutFocusAddressBar;
                ShortcutManager.OnRefresh += _shortcutRefresh;
                ShortcutManager.OnAddFavorite += _shortcutAddFavorite;
                ShortcutManager.OnShowHistory += _shortcutShowHistory;
                ShortcutManager.OnShowDownloads += _shortcutShowDownloads;
                ShortcutManager.OnFind += _shortcutFind;
                ShortcutManager.OnPrint += _shortcutPrint;
                ShortcutManager.OnNextTab += _shortcutNextTab;
                ShortcutManager.OnPreviousTab += _shortcutPreviousTab;
                ShortcutManager.OnSwitchToTab += _shortcutSwitchToTab;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OnLoaded] 异常: {ex}");
                if (_isNewWindow) Services.Diagnostics.Log($"[窗口] OnLoaded 异常: {ex}");
                await new ContentDialog
                {
                    Title = "界面初始化失败",
                    Content = ex.ToString(),
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                }.ShowAsync();
            }
            if (_isNewWindow) Services.Diagnostics.Log("[窗口] OnLoaded 完成");
        }

        // ---- TabManager 事件处理 ----
        private void OnTabOrderChanged() => SyncTabPanelOrder();

        // ---- 核心修复：新建标签后自动激活并焦点到地址栏 ----
        private async void OnTabManagerTabCreated(TabViewItem viewItem)
        {
            // 切换到新标签
            await _tabManager.SwitchToTabAsync(viewItem);
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
            string host = GetHostName(tab.CurrentUrl);
            toolbarControl.UpdateSiteInfo(isSecure, host);

            if (_appWindow != null)
            {
                try { _appWindow.Title = tab.Title ?? "新标签页"; } catch { }
            }

            SetFocusToCurrentTabContent();
        }

        private static string GetHostName(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "";
        }

        private void OnTabManagerTabCountChanged(int count)
        {
            UpdateTabLayout();
            if (count == 0 && !_isEngineSwitching)
                _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank");
        }

        private void OnTabManagerTabUrlChanged(IBrowserTab tab, string url)
        {
            if (_tabManager.CurrentTab == tab)
            {
                toolbarControl.UpdateNavState(tab.CanGoBack, tab.CanGoForward, url);
                UpdateStarButton();

                bool isSecure = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                string host = GetHostName(url);
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

        private void OnTabManagerTabClosedCleanup(TabViewItem viewItem) => _readerModeUrls.Remove(viewItem.Tab);

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
            // 保留原标签的 URL（空白标签直接传 about:blank，与应用新建标签约定一致，不替换为搜索引擎）
            string url = item.Tab.CurrentUrl;
            EngineType engine = item.Tab.Engine;

            Services.Diagnostics.Log($"[拖拽] 脱离请求 url={url} engine={engine}");
            bool shown = await WindowManager.OpenNewWindowAsync(engine, url);
            Services.Diagnostics.Log($"[拖拽] 新窗口显示结果={shown}");
            if (shown)
            {
                await _tabManager.CloseTabAsync(item);
                Services.Diagnostics.Log($"[拖拽] 原窗口标签已关闭，剩余标签数={_tabManager.TabCount}");
            }
            else
            {
                NotificationService.ShowToast("无法打开新窗口", "拖出标签页失败");
                Services.Diagnostics.Log("[拖拽] 新窗口打开失败，标签保留在原窗口");
            }
        }

        // ---- 焦点控制 ----
        private void SetFocusToCurrentTabContent()
        {
            if (_tabManager.CurrentTab == null) return;

            var element = _tabManager.CurrentTab.ViewElement;
            if (element == null) return;

            if (element is WebView2 wv2)
            {
                wv2.Focus(FocusState.Programmatic);
                return;
            }

            if (element is Control control)
            {
                control.Focus(FocusState.Programmatic);
            }
        }

        // ---- 阅读模式 ----
        private async void ToggleReaderMode()
        {
            var tab = _tabManager.CurrentTab;
            if (tab == null) return;

            if (_readerModeUrls.TryGetValue(tab, out string originalUrl))
            {
                _readerModeUrls.Remove(tab);
                await tab.NavigateAsync(originalUrl);
                return;
            }

            string currentUrl = tab.CurrentUrl;
            if (string.IsNullOrEmpty(currentUrl) || currentUrl == "about:blank")
            {
                NotificationService.ShowToast("无法使用阅读模式", "当前页面没有可阅读的内容");
                return;
            }

            bool entered = await ReaderService.EnterReaderModeAsync(tab);
            if (entered)
            {
                _readerModeUrls[tab] = currentUrl;
            }
            else
            {
                NotificationService.ShowToast("无法使用阅读模式", "此页面没有可提取的正文内容");
            }
        }

        // ---- 复制当前网址 ----
        private void CopyCurrentUrl()
        {
            if (_tabManager.CurrentTab != null)
            {
                var url = _tabManager.CurrentTab.CurrentUrl;
                if (!string.IsNullOrEmpty(url) && url != "about:blank")
                {
                    var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dp.SetText(url);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                    NotificationService.ShowToast("已复制", "网址已复制到剪贴板");
                }
            }
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
            int pinnedCount = _tabManager.GetPinnedTabCount();
            _layoutManager.Recalculate(TabBarBorder.ActualWidth, _tabManager.TabCount, pinnedCount);
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

        private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            if (!_isDragging)
                UpdateTabLayout();
        }

        // AppWindow 多窗口共享主 UI 线程，Window.Current 恒定指向主窗口，
        // 需用 XamlRoot 判断快捷键事件是否属于本窗口（是否有焦点元素）。
        private bool IsFocusedWindow()
        {
            try { return FocusManager.GetFocusedElement(this.XamlRoot) != null; }
            catch { return false; }
        }

        // ---- 子窗口（AppWindow）标题栏配色 ----
        // 仅用于给系统标题栏按钮（最小化/最大化/关闭）和条带着色；
        // 拖拽区域由系统管理（无 SetDragRectangles），交互控件不能放在条带内。
        private void ApplyAppWindowTitleBarColors()
        {
            if (_appWindow == null) return;
            bool isDark = ThemeManager.IsDarkTheme;
            var transparent = Color.FromArgb(0, 0, 0, 0);
            var fg = isDark
                ? Color.FromArgb(255, 255, 255, 255)
                : Color.FromArgb(255, 0, 0, 0);

            _appWindow.TitleBar.ForegroundColor = fg;
            _appWindow.TitleBar.BackgroundColor = transparent;
            _appWindow.TitleBar.InactiveBackgroundColor = transparent;
            _appWindow.TitleBar.ButtonForegroundColor = fg;
            _appWindow.TitleBar.ButtonBackgroundColor = transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor = transparent;
            _appWindow.TitleBar.ButtonHoverBackgroundColor = isDark
                ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x30, 0x00, 0x00, 0x00);
            _appWindow.TitleBar.ButtonPressedBackgroundColor = isDark
                ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x40, 0x00, 0x00, 0x00);
        }

        // ---- 子窗口（AppWindow）键盘快捷键 ----
        // AppWindow 的键盘输入不经过 CoreWindow（主窗口钩子收不到），
        // 键盘事件以 XAML 路由事件形式送达焦点所在的窗口，在页面根上处理。
        private void OnWindowKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (ShortcutManager.TryHandle(e.Key))
                e.Handled = true;
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
        private void OnThemeChanged(SkinManager.SkinColors colors, bool isDark)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ApplyTheme());
        }

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
            ApplyAppWindowTitleBarColors();
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
                SetFocusToCurrentTabContent();
            }
        }

        private async void ToolbarControl_BackRequested() { if (_tabManager.CurrentTab?.CanGoBack == true) { await _tabManager.CurrentTab.GoBackAsync(); SetFocusToCurrentTabContent(); } }
        private async void ToolbarControl_ForwardRequested() { if (_tabManager.CurrentTab?.CanGoForward == true) { await _tabManager.CurrentTab.GoForwardAsync(); SetFocusToCurrentTabContent(); } }
        private async void ToolbarControl_RefreshRequested() { if (_tabManager.CurrentTab != null) { await _tabManager.CurrentTab.RefreshAsync(); SetFocusToCurrentTabContent(); } }

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

                _isEngineSwitching = true;

                await _tabManager.CloseTabAsync(viewItem, false);

                await _tabManager.CreateTabAsync(newEngine, currentUrl);

                SetFocusToCurrentTabContent();

                Debug.WriteLine($"[EngineSwitch] 切换到 {newEngine} 成功");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"引擎切换失败: {ex}");
                await new ContentDialog
                {
                    Title = "引擎切换失败",
                    Content = ex.Message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                }.ShowAsync();
            }
            finally
            {
                _isEngineSwitching = false;
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
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => await _downloadCoordinator.HandleEdgeHtmlDownloadRequested(url));
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
                        PrimaryButtonText = "关闭",
                        XamlRoot = this.XamlRoot
                    }.ShowAsync();
                }
                catch (Exception ex)
                {
                    await new ContentDialog
                    {
                        Title = "错误",
                        Content = $"获取源代码失败：{ex.Message}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
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
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
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
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
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
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            }.ShowAsync();
        }

        private async void ShowNotImplementedDialog(string feature)
        {
            await new ContentDialog
            {
                Title = "即将推出",
                Content = $"功能“{feature}”尚未实现。",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
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
            if (e.Parameter is NewWindowArgs args)
            {
                _pendingUrl = args.Url;
                _pendingEngine = args.Engine;
                _defaultEngine = args.Engine;
                _appWindow = args.AppWindow;
                _isNewWindow = true;
                string windowId = _appWindow != null ? "AppWindow" : "main";
                Services.Diagnostics.Log($"[窗口] OnNavigatedTo window={windowId} url={args.Url}");
            }
            else if (e.Parameter is string url)
            {
                _pendingUrl = url;
            }
        }

        private async Task CreateNewTabAndFocusAsync()
        {
            await _tabManager.CreateTabAsync(_defaultEngine, "about:blank");
            toolbarControl.FocusAddressBarAndClear();
        }
    }
}