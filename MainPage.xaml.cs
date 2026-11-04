using EdgeRebuild.Controls;
using EdgeRebuild.Core;
using EdgeRebuild.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
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
        private bool _isInPrivate;

        // ---- 收藏夹栏状态 ----
        private bool _favoritesBarVisible;

        // ---- 阅读模式：tab -> 原始 URL ----
        private readonly Dictionary<IBrowserTab, string> _readerModeUrls = new Dictionary<IBrowserTab, string>();

        // ---- 辅助窗口引用（AppWindow 同线程模型；主窗口为 null）----
        // 类型为 object：17763 没有 AppWindow，避免字段类型解析失败；操作统一走 AppWindowCompat。
        private object _appWindow;
        private IDisposable _sizeChangedSubscription;

        // ---- 多窗口焦点守卫 ----
        // AppWindow 同线程模式：Window.Current 恒指主窗口，失活窗口的 XamlRoot 内
        // 焦点元素残留 → 守卫 = WindowManager 槽位前台标记 && 本 XamlRoot 有焦点元素。
        private bool _windowActive;
        private int _pendingSessionSlot;   // OnNavigatedTo 暂存，OnLoaded 使用
        private int _sessionSlot;    // 本窗口会话槽位（0=主窗口；副窗由 WindowManager 分配）
        private int _restoreSlot = -1;   // >=1：本窗口是启动恢复重建的槽位窗口

        private void OnSlotActivated(int slot) => _windowActive = (slot == _sessionSlot);

        // ---- 快捷键处理器引用（用于跨窗口守卫和反注册） ----
        private Action _shortcutNewTab, _shortcutCloseTab, _shortcutReopenTab, _shortcutFocusAddressBar,
            _shortcutRefresh, _shortcutAddFavorite, _shortcutShowHistory, _shortcutShowDownloads,
            _shortcutFind, _shortcutFindNext, _shortcutFindPrevious, _shortcutCloseFind,
            _shortcutPrint, _shortcutNextTab, _shortcutPreviousTab, _shortcutHighlightMark, _shortcutWebCapture;
        private Action<int> _shortcutSwitchToTab;
        private Action<PageShortcutKey> _shortcutPageKey;

        // ---- EdgeHTML 页面内快捷键轮询（ScriptNotify 受白名单限制，改用 marker 轮询） ----
        private DispatcherTimer _pageKeyTimer;
        private bool _pageKeyPolling;

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

        private bool _shutdownDone;   // 关闭清理幂等标志

        /// <summary>
        /// 显式关闭清理：AppWindow.Closed 时由 WindowManager 直接调用。
        /// 实测窗口销毁后 frame.Content=null 不一定触发 Unloaded，
        /// 因此不能依赖 Unloaded 事件执行释放链路。
        /// </summary>
        public void Shutdown() => RunShutdownCore();

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_isNewWindow && !_shutdownDone)
                Services.Diagnostics.Log("[窗口] OnUnloaded 触发（窗口被关闭）");
            RunShutdownCore();
        }

        private void RunShutdownCore()
        {
            if (_shutdownDone) return;
            _shutdownDone = true;
            Services.Diagnostics.Log("[窗口] 关闭清理开始");
            // 窗口销毁竞态下任何清理步骤都可能抛（WebView2 Close / XAML 卸载 /
            // 组合动画中止）——逐步设防 + 落日志，既定位异常源，又保证
            // 某一步失败不跳过后续释放（否则 WebView 进程泄漏）。
            void Safe(string name, Action action)
            {
                try { action(); }
                catch (Exception ex)
                {
                    Services.Diagnostics.Log($"[窗口关闭] {name} 清理异常: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Safe("静态事件", CleanupStaticEvents);
            Safe("会话Flush", () => _ = SessionManager.FlushAsync());
            Safe("下载协调器", () => _downloadCoordinator?.CleanupAll());
            Safe("悬停预览", () =>
            {
                _hoverTimer?.Stop();
                _previewCloseTimer?.Stop();
                if (_tabPreviewPopup != null) _tabPreviewPopup.IsOpen = false;
            });
            Safe("窗口尺寸事件", () =>
            {
                _sizeChangedSubscription?.Dispose();
                _sizeChangedSubscription = null;
            });
            Safe("快捷键Disable", () => ShortcutManager.Disable());
            Safe("槽位广播解绑", () => WindowManager.SlotActivated -= OnSlotActivated);
            Safe("主题事件", () => ThemeManager.ThemeChanged -= OnThemeChanged);
            Safe("布局管理器", () => _layoutManager?.Dispose());
            Safe("标签释放", () => _tabManager?.DisposeAll());
            Safe("拖拽管理器", () => _dragManager?.Dispose());
            if (_appWindow != null)
            {
                Safe("键盘解绑", () =>
                {
                    this.KeyDown -= OnWindowKeyDown;
                    this.KeyUp -= OnWindowKeyUp;
                });
            }
            Services.Diagnostics.Log("[窗口关闭] 清理完成");
        }

        private void CleanupStaticEvents()
        {
            DownloadManager.DownloadProgressChanged -= OnDownloadProgress;
            DownloadManager.DownloadStatusChanged -= OnDownloadStatusChanged;
            DownloadManager.RetryDownloadRequested -= OnRetryDownloadRequested;
            EdgeHtmlTab.DownloadRequested -= OnEdgeHtmlDownloadRequested;
            SettingsManager.SettingChanged -= OnSettingChanged;
            FavoritesManager.Instance.Favorites.CollectionChanged -= OnFavoritesCollectionChanged;

            ShortcutManager.OnNewTab -= _shortcutNewTab;
            ShortcutManager.OnCloseTab -= _shortcutCloseTab;
            ShortcutManager.OnReopenTab -= _shortcutReopenTab;
            ShortcutManager.OnFocusAddressBar -= _shortcutFocusAddressBar;
            ShortcutManager.OnRefresh -= _shortcutRefresh;
            ShortcutManager.OnAddFavorite -= _shortcutAddFavorite;
            ShortcutManager.OnShowHistory -= _shortcutShowHistory;
            ShortcutManager.OnShowDownloads -= _shortcutShowDownloads;
            ShortcutManager.OnFind -= _shortcutFind;
            ShortcutManager.OnFindNext -= _shortcutFindNext;
            ShortcutManager.OnFindPrevious -= _shortcutFindPrevious;
            ShortcutManager.OnCloseFind -= _shortcutCloseFind;
            ShortcutManager.OnPrint -= _shortcutPrint;
            ShortcutManager.OnNextTab -= _shortcutNextTab;
            ShortcutManager.OnPreviousTab -= _shortcutPreviousTab;
            ShortcutManager.OnSwitchToTab -= _shortcutSwitchToTab;
            if (_tabManager != null)
            {
                _tabManager.PageKeyPressed -= _shortcutPageKey;
                _tabManager.PageMarkActionRequested -= OnPageMarkActionRequested;
                _tabManager.TtsTextRequested -= OnTtsTextRequested;
                _tabManager.NtpSearchRequested -= OnNtpSearchRequested;
                _tabManager.TabHoverStarted -= OnTabHoverStarted;
                _tabManager.TabHoverEnded -= OnTabHoverEnded;
                _tabManager.TabUrlChanged -= OnPreviewCacheInvalidate;
                ReadAloudService.StateChanged -= OnReadAloudStateChanged;
            }
            _hoverTimer?.Stop();
            _previewCloseTimer?.Stop();
            if (_tabPreviewPopup != null) _tabPreviewPopup.IsOpen = false;
            ShortcutManager.OnHighlightMark -= _shortcutHighlightMark;
            ShortcutManager.OnWebCapture -= _shortcutWebCapture;
            WindowManager.SlotActivated -= OnSlotActivated;
            this.KeyDown -= OnWindowKeyDown;
            this.KeyUp -= OnWindowKeyUp;
            _pageKeyTimer?.Stop();
            _pageKeyTimer = null;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            if (_isNewWindow) Services.Diagnostics.Log($"[窗口] OnLoaded 开始 thread={Environment.CurrentManagedThreadId}");

            // 多窗口：激活态由 WindowManager 槽位广播维护（页面 GotFocus 上报 +
            // TryShow/Closed 时的槽位通知）。原生视图副窗口没有 _appWindow，
            // 必须按 _isNewWindow 判定，否则会误走主窗口分支（_windowActive=true、
            // ReportSlotFocus(0)），并且不订阅槽位广播/不接管键盘。
            _sessionSlot = _pendingSessionSlot;
            if (_isNewWindow)
            {
                _windowActive = false;   // 副窗口等首次前台标记
                WindowManager.SlotActivated += OnSlotActivated;
                this.GotFocus += (s2, e2) => WindowManager.ReportSlotFocus(_sessionSlot);
                this.KeyDown += OnWindowKeyDown;
                this.KeyUp += OnWindowKeyUp;
                OnSlotActivated(WindowManager.ActiveSlot);   // 订阅前可能已广播过，补同步一次
            }
            else
            {
                _windowActive = true;   // 主窗口启动即默认激活
                this.GotFocus += (s2, e2) => WindowManager.ReportSlotFocus(0);
            }

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
                // 多窗口会话：每个 MainPage 窗口持独立 Slot（0=主窗），互不覆盖；
                // InPrivate 不持久化；MetroPage ParticipatesInSession=false
                // （_sessionSlot 已在上面的激活态分支里赋值）
                _tabManager = new TabManager
                {
                    IsInPrivateWindow = _isInPrivate,
                    ParticipatesInSession = !_isInPrivate,
                    SessionSlot = _sessionSlot
                };
                _tabManager.Initialize(TabBarPanel, ContentContainer);
                if (_isInPrivate)
                {
                    InPrivateBadge.Visibility = Visibility.Visible;
                    toolbarControl.InPrivateMode = true;
                    hubPane.SuppressHistory = true;
                    Services.Diagnostics.Log("[InPrivate] InPrivate 窗口已启用（不写历史、不存会话）");
                }
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
                _tabManager.TabHoverStarted += OnTabHoverStarted;      // 标签悬停缩略图预览（旧版 Edge 交互）
                _tabManager.TabHoverEnded += OnTabHoverEnded;
                _tabManager.TabUrlChanged += OnPreviewCacheInvalidate;
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
                    (viewItem) => _ = _tabManager.TogglePinTabAsync(viewItem),
                    WebCaptureCurrentAsync,
                    ShareCurrentPageAsync
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
                    WebCaptureCurrentAsync,
                    () => { HubSplitView.IsPaneOpen = true; hubPane.ShowHistory(); },
                    () => SettingsSplitView.IsPaneOpen = !SettingsSplitView.IsPaneOpen,
                    ClearBrowsingDataAsync,
                    BookmarkAllTabs,
                    ShowAboutDialog,
                    async () => { if (!await WindowManager.OpenNewWindowAsync(_defaultEngine, "about:blank")) NotificationService.ShowToast("无法打开新窗口", "创建窗口失败"); },
                    async () => { if (!await WindowManager.OpenInPrivateWindowAsync(_defaultEngine, "about:blank")) NotificationService.ShowToast("无法打开 InPrivate 窗口", "创建窗口失败"); },
                    ToggleInPrivateMode,
                    () => _isInPrivate,
                    async () => { if (!await WindowManager.OpenMetroWindowAsync(_defaultEngine, "about:blank")) NotificationService.ShowToast("无法打开 Metro IE 窗口", "创建窗口失败"); },
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
                    ToggleReaderMode,
                    () => ReadAloudService.IsActive
                        ? (ReadAloudService.IsPaused ? "继续朗读" : "停止朗读")
                        : "大声朗读",
                    ToggleReadAloud,
                    () => { _ = WindowManager.OpenDemoWindowAsync(); }
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
                    // 副窗口（AppWindow，18362+）：默认扩展标题栏（系统拖拽区）；
                    // 用户可在设置→外观中开启「新窗口显示系统标题栏」，此时不扩展内容进标题栏
                    if (PlatformCompat.HasAppWindow)
                        AppWindowCompat.SetTitle(_appWindow, "EdgeRebuild");
                    string titleBarSetting = await SettingsManager.GetAsync("ShowSystemTitleBar") ?? "False";
                    bool showSystemTitleBar = (titleBarSetting == "True" || titleBarSetting == "true");
                    if (!showSystemTitleBar)
                    {
                        if (PlatformCompat.HasAppWindow)
                            AppWindowCompat.ApplyChrome(_appWindow, ThemeManager.IsDarkTheme);
                        NewTabButton.Visibility = Visibility.Collapsed;
                        // 垂直侧边栏按钮下移到系统拖拽区下方（32px），保持可用
                        VerticalButtonsHost.Margin = new Thickness(0, 32, 0, 0);
                    }
                    this.KeyDown += OnWindowKeyDown;
                    this.KeyUp += OnWindowKeyUp;
                }
                else
                {
                    // 主窗口 / 原生副窗口：17763 可用的标题栏方案
                    // （ExtendViewIntoTitleBar + Window.SetTitleBar + ApplicationViewTitleBar）
                    WindowChrome.ApplyLegacy(TitleBarDragArea, ThemeManager.IsDarkTheme);
                }

                SubscribeWindowSizeChanged();

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

                // ---- 收藏夹栏(参考规范 §6.1:工具栏下方常驻行)----
                string favBarSetting = await SettingsManager.GetAsync("ShowFavoritesBar") ?? "False";
                ShowFavoritesBar(favBarSetting == "True" || favBarSetting == "true");
                FavoritesManager.Instance.Favorites.CollectionChanged += OnFavoritesCollectionChanged;
                RefreshFavoritesBar();

                // ---- 创建首页标签 / 恢复会话 ----
                string homeUrl = await SettingsManager.GetAsync("HomePage") ?? "about:blank";
                if (_tabManager.TabCount == 0)
                {
                    if (_restoreSlot >= 1)
                    {
                        // 启动恢复重建的副窗口：读取本槽位记录，不落主页兜底
                        var slotTabs = await SessionManager.LoadTabsAsync(_restoreSlot);
                        foreach (var rec in slotTabs)
                        {
                            if (string.IsNullOrEmpty(rec.Url)) continue;
                            EngineType engine = (rec.Engine == "WebView2") ? EngineType.WebView2 : EngineType.EdgeHtml;
                            await _tabManager.CreateTabAsync(engine, rec.Url, rec.IsPinned);
                        }
                        if (_tabManager.TabCount == 0)
                            await _tabManager.CreateTabAsync(_defaultEngine, homeUrl);   // 空槽兜底
                    }
                    else if (!string.IsNullOrEmpty(_pendingUrl))
                    {
                        await _tabManager.CreateTabAsync(_defaultEngine, _pendingUrl);
                    }
                    else
                    {
                        string restoreSetting = _isInPrivate ? "False"
                            : await SettingsManager.GetAsync("RestoreSession") ?? "True";
                        bool restoreSession = (restoreSetting == "True" || restoreSetting == "true");
                        var sessionTabs = _isInPrivate ? new List<SessionTabRecord>() : await SessionManager.LoadTabsAsync(0);

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

                        // 主窗口标签恢复完毕 → 按持久化槽位重建上次的副主窗口
                        if (!_isInPrivate && restoreSession && !_isNewWindow)
                            _ = WindowManager.RestoreSecondaryWindowsAsync();
                    }
                }

                // ---- 恢复垂直标签栏状态 ----
                try
                {
                    string vtSetting = await SettingsManager.GetAsync("VerticalTabs");
                    if (vtSetting == "True" || vtSetting == "true")
                    {
                        _isVerticalTabs = true;
                        ApplyVerticalTabs();
                        ApplyTheme(); // 刷新垂直模式背景（ApplyTheme 在设置恢复前已执行）
                    }
                }
                catch { }

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
                _shortcutFindNext = () =>
                {
                    if (!IsFocusedWindow()) return;
                    if (FindBar.Visibility == Visibility.Visible)
                        _ = RunFindAsync(FindBox.Text, "next");
                    else
                        FindOnPage();
                };
                _shortcutFindPrevious = () =>
                {
                    if (!IsFocusedWindow()) return;
                    if (FindBar.Visibility == Visibility.Visible)
                        _ = RunFindAsync(FindBox.Text, "prev");
                    else
                        FindOnPage();
                };
                _shortcutCloseFind = () =>
                {
                    if (!IsFocusedWindow()) return;
                    if (FindBar.Visibility == Visibility.Visible)
                        CloseFindBar();
                };
                _shortcutPrint = () =>
                {
                    if (!IsFocusedWindow()) return;
                    _ = PrintCurrentPageAsync();
                };
                _shortcutHighlightMark = () =>
                {
                    if (!IsFocusedWindow()) return;
                    _ = MarkSelectionDefaultAsync();
                };
                _shortcutWebCapture = () =>
                {
                    if (!IsFocusedWindow()) return;
                    _ = WebCaptureCurrentAsync();
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
                ShortcutManager.OnFindNext += _shortcutFindNext;
                ShortcutManager.OnFindPrevious += _shortcutFindPrevious;
                ShortcutManager.OnCloseFind += _shortcutCloseFind;
                ShortcutManager.OnPrint += _shortcutPrint;
                ShortcutManager.OnHighlightMark += _shortcutHighlightMark;
                ShortcutManager.OnWebCapture += _shortcutWebCapture;
                ShortcutManager.OnNextTab += _shortcutNextTab;
                ShortcutManager.OnPreviousTab += _shortcutPreviousTab;
                ShortcutManager.OnSwitchToTab += _shortcutSwitchToTab;

                // 页面内快捷键（焦点在网页内容内时，键击被引擎吞掉、CoreWindow 收不到，
                // 由页面注入脚本经消息通道回传）——与上方 _shortcutFind 系列逻辑一致。
                _shortcutPageKey = (key) =>
                {
                    if (!IsFocusedWindow()) return;
                    HandlePageKey(key);
                };
                _tabManager.PageKeyPressed += _shortcutPageKey;
                _tabManager.PageMarkActionRequested += OnPageMarkActionRequested;

                // ---- 大声朗读（TTS）：网页正文提取 + EPUB 章节经消息通道回传 ----
                ReadAloudService.Initialize(TtsPlayer);
                ReadAloudService.StateChanged += OnReadAloudStateChanged;
                _tabManager.TtsTextRequested += OnTtsTextRequested;
                _tabManager.NtpSearchRequested += OnNtpSearchRequested;

                // ---- EdgeHTML 页面内快捷键：marker 轮询（见 EdgeHtmlTab.PageKeyBridgeJs 注释）----
                // R2-2：仅当活动标签为 EdgeHTML 时运行（SyncPageKeyPolling 随标签切换启停）
                _pageKeyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _pageKeyTimer.Tick += OnPageKeyTimerTick;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OnLoaded] 异常: {ex}");
                if (_isNewWindow) Services.Diagnostics.Log($"[窗口] OnLoaded 异常: {ex}");
                var errorDialog = new ContentDialog
                {
                    Title = "界面初始化失败",
                    Content = ex.ToString(),
                    CloseButtonText = "确定"
                };
                PlatformCompat.SetDialogRoot(errorDialog, this);
                await errorDialog.ShowAsync();
            }
            if (_isNewWindow) Services.Diagnostics.Log("[窗口] OnLoaded 完成");
        }

        // ---- 页面内快捷键的统一处理（CoreWindow 钩子、WebView2 消息、EdgeHTML marker 轮询共用） ----
        private void HandlePageKey(PageShortcutKey key)
        {
            switch (key)
            {
                case PageShortcutKey.Find:
                    FindOnPage();
                    break;
                case PageShortcutKey.FindNext:
                    if (FindBar.Visibility == Visibility.Visible)
                        _ = RunFindAsync(FindBox.Text, "next");
                    else
                        FindOnPage();
                    break;
                case PageShortcutKey.FindPrevious:
                    if (FindBar.Visibility == Visibility.Visible)
                        _ = RunFindAsync(FindBox.Text, "prev");
                    else
                        FindOnPage();
                    break;
                case PageShortcutKey.CloseFind:
                    if (FindBar.Visibility == Visibility.Visible)
                        CloseFindBar();
                    break;
                case PageShortcutKey.HighlightMark:
                    _ = MarkSelectionDefaultAsync();
                    break;
                case PageShortcutKey.WebCapture:
                    _ = WebCaptureCurrentAsync();
                    break;
                case PageShortcutKey.NewTab:
                    _ = CreateNewTabAndFocusAsync();
                    break;
                case PageShortcutKey.CloseTab:
                    {
                        var viewItem = _tabManager.Tabs.FirstOrDefault(v => v.Tab == _tabManager.CurrentTab);
                        if (viewItem != null) _ = _tabManager.CloseTabAsync(viewItem);
                        break;
                    }
                case PageShortcutKey.FocusUrl:
                    toolbarControl.FocusAddressBarAndClear();
                    break;
                    // 主窗口无全屏模式，忽略
            }
        }

        // R2-2：轮询启停治理——仅活动标签为 EdgeHTML 时运行，切换标签/清空时停表
        private void SyncPageKeyPolling()
        {
            if (_pageKeyTimer == null) return;
            bool need = _tabManager?.CurrentTab is EdgeHtmlTab;
            if (need && !_pageKeyTimer.IsEnabled) _pageKeyTimer.Start();
            else if (!need && _pageKeyTimer.IsEnabled) _pageKeyTimer.Stop();
        }

        // EdgeHTML 引擎无可靠的按键回传通道（ScriptNotify 需白名单、WebView 无输入事件、
        private async void OnPageKeyTimerTick(object sender, object e)
        {
            if (!IsFocusedWindow()) return;
            if (_pageKeyPolling) return;
            if (!(_tabManager?.CurrentTab is EdgeHtmlTab edgeTab)) return;
            _pageKeyPolling = true;
            try
            {
                // EPUB 朗读（EdgeHTML 无 postMessage 通道，走 marker 轮询）
                string tts = await edgeTab.PollTtsAsync();
                if (tts.StartsWith("tts:", StringComparison.Ordinal))
                {
                    OnTtsTextRequested(tts.Substring("tts:".Length));
                    return;
                }
                // NTP 搜索词（EdgeHTML 无 postMessage 通道，走 marker 轮询，与 WebView2 统一）
                string ntp = await edgeTab.PollNtpSearchAsync();
                if (!string.IsNullOrEmpty(ntp))
                {
                    OnNtpSearchRequested(ntp);
                    return;
                }
                // 页面右键（EdgeHTML 无控件级右键事件，走 marker 轮询，菜单与 WebView2 一致）
                string ctx = await edgeTab.PollContextMenuAsync();
                if (!string.IsNullOrEmpty(ctx) && ctx != "{}")
                {
                    ShowEdgeHtmlContextMenu(edgeTab, ctx);
                    return;
                }
                string key = await edgeTab.PollPageKeyAsync();
                if (string.IsNullOrEmpty(key)) return;
                var parsedKey = PageKeyMarkers.Parse(key);
                if (parsedKey != null)
                {
                    HandlePageKey(parsedKey.Value);
                    return;
                }
                if (key.StartsWith("mark:remove:", StringComparison.Ordinal)
                    && int.TryParse(key.Substring("mark:remove:".Length), out int markId))
                    _ = HandleMarkRemovedAsync(markId);
            }
            catch { }
            finally { _pageKeyPolling = false; }
        }

        /// <summary>
        /// 把页面右键 marker（"kind|x|y|encodeURIComponent(值)"）转成菜单参数并弹出，
        /// 与 WebView2 的右键菜单共用 TabContextMenuManager.ShowWebContextMenu。
        /// </summary>
        private void ShowEdgeHtmlContextMenu(EdgeHtmlTab edgeTab, string raw)
        {
            try
            {
                var parts = raw.Split('|');
                if (parts.Length < 4) return;
                string kind = parts[0];
                double x = 0, y = 0;
                double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out x);
                double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out y);
                string val = Uri.UnescapeDataString(parts[3]);

                var args = new TabContextMenuEventArgs
                {
                    CanGoBack = edgeTab.CanGoBack,
                    CanGoForward = edgeTab.CanGoForward,
                    Location = new Point(x, y),
                    MenuType = kind switch
                    {
                        "link" => ContextMenuType.Link,
                        "image" => ContextMenuType.Image,
                        "selection" => ContextMenuType.Selection,
                        _ => ContextMenuType.Page
                    }
                };
                if (args.MenuType == ContextMenuType.Link)
                    args.LinkUrl = val;
                else if (args.MenuType == ContextMenuType.Image)
                    args.ImageUrl = val;
                else if (args.MenuType == ContextMenuType.Selection)
                {
                    args.SelectionText = val;
                    args.HasSelection = !string.IsNullOrEmpty(val);
                }
                _contextMenuManager?.ShowWebContextMenu(args);
            }
            catch { }
        }

        // ---- 网页文本标记（高亮） ----
        private static async Task RestoreMarksDelayedAsync(IBrowserTab tab)
        {
            await Task.Delay(800);   // 等待页面加载完成
            await PageMarkManager.RestoreAsync(tab);
        }

        private async Task MarkSelectionDefaultAsync()
        {
            var tab = _tabManager?.CurrentTab;
            if (tab == null) return;
            var mark = await PageMarkManager.MarkSelectionAsync(tab, PageMarkManager.DefaultColor);
            if (mark == null)
                NotificationService.ShowToast("标记高亮", "请先选中网页中的文字");
            else
                NotificationService.ShowToast("已标记", "点击高亮文字可移除");
        }

        private async void OnPageMarkActionRequested(string payload)
        {
            if (payload?.StartsWith("remove:", StringComparison.Ordinal) == true
                && int.TryParse(payload.Substring("remove:".Length), out int id))
                await HandleMarkRemovedAsync(id);
        }

        private async Task HandleMarkRemovedAsync(int id)
        {
            await PageMarkManager.RemoveMarkAsync(id);
            NotificationService.ShowToast("已移除标记", "");
        }

        // ---- 大声朗读 ----
        private void OnTtsTextRequested(string text)
        {
            if (!IsFocusedWindow()) return;
            var tab = _tabManager?.CurrentTab;
            if (string.IsNullOrWhiteSpace(text)) return;
            ReadAloudService.Start(text, tab?.Title ?? "电子书");
        }

        // ---- 朗读控制浮层 ----
        // 真多视图：事件在播放器（主视图）线程上触发，副视图 UI 更新须封送回本视图线程
        private void OnReadAloudStateChanged()
        {
            var dispatcher = this.Dispatcher;
            var ignored = dispatcher.RunAsync(CoreDispatcherPriority.Normal, ApplyReadAloudUi);
        }

        private void ApplyReadAloudUi()
        {
            if (ReadAloudBar == null) return;
            if (ReadAloudService.IsActive)
            {
                ReadAloudBar.Visibility = Visibility.Visible;
                ReadAloudLabel.Text = "朗读：" + (ReadAloudService.CurrentLabel ?? "网页");
                ReadAloudPauseBtn.Content = ReadAloudService.IsPaused ? "\uE768" : "\uE769";
                ToolTipService.SetToolTip(ReadAloudPauseBtn,
                    ReadAloudService.IsPaused ? "继续" : "暂停");
            }
            else
            {
                ReadAloudBar.Visibility = Visibility.Collapsed;
            }
        }

        private void ReadAloudPauseBtn_Click(object sender, RoutedEventArgs e)
            => ReadAloudService.TogglePause();

        private void ReadAloudStopBtn_Click(object sender, RoutedEventArgs e)
            => ReadAloudService.Stop();

        private void ReadAloudCloseBtn_Click(object sender, RoutedEventArgs e)
        {
            // 仅隐藏控制面板，朗读继续
            ReadAloudBar.Visibility = Visibility.Collapsed;
        }

        // ---- 新标签页搜索（NTP 页面提交搜索词，走当前默认引擎） ----
        private async void OnNtpSearchRequested(string query)
        {
            if (!IsFocusedWindow()) return;
            var tab = _tabManager?.CurrentTab;
            if (tab == null || string.IsNullOrWhiteSpace(query)) return;
            string engine = "Google";
            try { engine = await SettingsManager.GetAsync("SearchEngine") ?? "Google"; } catch { }
            string url = engine switch
            {
                "Bing" => $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}",
                "DuckDuckGo" => $"https://duckduckgo.com/?q={Uri.EscapeDataString(query)}",
                _ => $"https://www.google.com/search?q={Uri.EscapeDataString(query)}"
            };
            await tab.NavigateAsync(url);
        }

        /// <summary>
        /// 朗读开关：未朗读时提取正文开始朗读；朗读中再点→停止；暂停中点→继续。
        /// </summary>
        private async void ToggleReadAloud()
        {
            if (!IsFocusedWindow()) return;
            var tab = _tabManager.CurrentTab;
            if (tab == null) return;

            if (ReadAloudService.IsActive)
            {
                if (ReadAloudService.IsPaused) ReadAloudService.TogglePause();
                else ReadAloudService.Stop();
                return;
            }

            string title = tab.Title ?? "网页";
            string text = null;
            try
            {
                if (_readerModeUrls.ContainsKey(tab))
                {
                    // 阅读模式中：直接读阅读视图正文
                    text = await tab.ExecuteScriptAsync(
                        "(function(){var c=document.querySelector('.reader-content');return c?c.innerText:'';})()");
                }
                else
                {
                    var article = await ReaderService.ExtractTextForTtsAsync(tab);
                    if (article != null)
                    {
                        text = article.Text;
                        if (!string.IsNullOrWhiteSpace(article.Title)) title = article.Title;
                    }
                }
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Log($"[朗读] 提取异常: {ex}");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                NotificationService.ShowToast("无法朗读", "未提取到可朗读的正文内容");
                return;
            }
            ReadAloudService.Start(text, title);
        }

        // ---- 网页捕获（截图 + 画笔批注） ----
        private async Task WebCaptureCurrentAsync()
        {
            var tab = _tabManager?.CurrentTab;
            if (tab == null) return;
            byte[] png = await WebCaptureService.CapturePagePngAsync(tab);
            if (png == null || png.Length == 0)
            {
                NotificationService.ShowToast("网页捕获", "截图失败");
                return;
            }
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(png))
            {
                await bmp.SetSourceAsync(ms.AsRandomAccessStream());
            }
            ShowCaptureOverlay(bmp);
        }

        private void ShowCaptureOverlay(BitmapImage bmp)
        {
            CaptureInkToolbar.TargetInkCanvas = CaptureInk;
            CaptureBorder.Background = ThemeManager.GetToolbarBackground();
            CaptureTitle.Foreground = ThemeManager.GetForegroundBrush();
            _cropMode = false;
            ClearCrop();
            CaptureCropBtn.Content = "裁剪";
            CaptureInk.InkPresenter.IsInputEnabled = true;
            double dispW = 900, dispH = 560;
            if (bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
            {
                double scale = Math.Min(dispW / bmp.PixelWidth, dispH / bmp.PixelHeight);
                if (scale > 1) scale = 1;   // 小图不放大
                dispW = bmp.PixelWidth * scale;
                dispH = bmp.PixelHeight * scale;
            }
            CaptureImage.Source = bmp;
            CaptureImageGrid.Width = dispW;
            CaptureImageGrid.Height = dispH;
            CaptureInk.InkPresenter.StrokeContainer.Clear();
            CaptureInk.InkPresenter.InputDeviceTypes =
                CoreInputDeviceTypes.Mouse | CoreInputDeviceTypes.Pen | CoreInputDeviceTypes.Touch;
            CaptureOverlay.Visibility = Visibility.Visible;
        }

        private void CloseCapture() => CaptureOverlay.Visibility = Visibility.Collapsed;

        private void CaptureOverlay_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (e.OriginalSource == CaptureOverlay) CloseCapture();
        }

        // ---- 区域裁剪：裁剪模式下拖动框选，保存/复制仅输出框内合成图 ----
        private bool _cropMode = false;
        private bool _hasCrop = false;
        private Windows.Foundation.Point _cropStart;
        private Windows.Foundation.Rect _cropRect;

        private void CaptureCropBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_cropMode) return;
            // 进入裁剪模式（有旧框先清除重画）
            _cropMode = true;
            ClearCrop();
            CaptureInk.InkPresenter.IsInputEnabled = false;
            CaptureCropBtn.Content = "裁剪中…";
            CaptureTitle.Text = "拖动框选要保留的区域，松开鼠标即确认";
        }

        private void ClearCrop()
        {
            _hasCrop = false;
            CaptureCropRect.Visibility = Visibility.Collapsed;
            CaptureCropSize.Visibility = Visibility.Collapsed;
        }

        private void CaptureImageGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_cropMode) return;
            _cropStart = e.GetCurrentPoint(CaptureImageGrid).Position;
            _hasCrop = false;
            CaptureCropRect.Visibility = Visibility.Visible;
            CaptureCropSize.Visibility = Visibility.Visible;
            CaptureCropRect.Width = 0;
            CaptureCropRect.Height = 0;
            Canvas.SetLeft(CaptureCropRect, _cropStart.X);
            Canvas.SetTop(CaptureCropRect, _cropStart.Y);
            Canvas.SetLeft(CaptureCropSize, _cropStart.X + 4);
            Canvas.SetTop(CaptureCropSize, _cropStart.Y + 4);
            CaptureImageGrid.CapturePointer(e.Pointer);
        }

        private void CaptureImageGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_cropMode || CaptureCropRect.Visibility != Visibility.Visible) return;
            var pos = e.GetCurrentPoint(CaptureImageGrid).Position;
            double x = Math.Min(_cropStart.X, pos.X);
            double y = Math.Min(_cropStart.Y, pos.Y);
            double w = Math.Abs(pos.X - _cropStart.X);
            double h = Math.Abs(pos.Y - _cropStart.Y);
            Canvas.SetLeft(CaptureCropRect, x);
            Canvas.SetTop(CaptureCropRect, y);
            CaptureCropRect.Width = w;
            CaptureCropRect.Height = h;
            Canvas.SetLeft(CaptureCropSize, x + 4);
            Canvas.SetTop(CaptureCropSize, y + 4);
            CaptureCropSize.Text = Math.Round(w) + "×" + Math.Round(h);
        }

        private void CaptureImageGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_cropMode || CaptureCropRect.Visibility != Visibility.Visible) return;
            var pos = e.GetCurrentPoint(CaptureImageGrid).Position;
            double x = Math.Min(_cropStart.X, pos.X);
            double y = Math.Min(_cropStart.Y, pos.Y);
            double w = Math.Abs(pos.X - _cropStart.X);
            double h = Math.Abs(pos.Y - _cropStart.Y);
            CaptureImageGrid.ReleasePointerCapture(e.Pointer);
            if (w <= 8 || h <= 8)
            {
                ClearCrop();
                ExitCropMode(false);
                return;
            }
            // 松开鼠标即确认选区
            _cropRect = new Windows.Foundation.Rect(x, y, w, h);
            _hasCrop = true;
            ExitCropMode(true);
        }

        private void ExitCropMode(bool hasCrop)
        {
            _cropMode = false;
            CaptureInk.InkPresenter.IsInputEnabled = true;
            CaptureCropBtn.Content = hasCrop ? "重新裁剪" : "裁剪";
            CaptureTitle.Text = hasCrop
                ? "已框选区域（" + Math.Round(_cropRect.Width) + "×" + Math.Round(_cropRect.Height)
                    + "），保存/复制将输出所选区域"
                : "网页捕获 - 在图上批注，然后保存或复制";
        }

        private async Task<byte[]> RenderCaptureCompositeAsync()
        {
            // 渲染前隐藏裁剪选框/尺寸文字，避免被“烤”进导出图（还原 UI 状态见下方）
            var cropLayerVisibility = CaptureCropLayer.Visibility;
            CaptureCropLayer.Visibility = Visibility.Collapsed;
            CaptureImageGrid.UpdateLayout();

            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(CaptureImageGrid);

            CaptureCropLayer.Visibility = cropLayerVisibility;   // 还原裁剪框显示状态

            var pixels = await rtb.GetPixelsAsync();
            var ras = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                (uint)rtb.PixelWidth,
                (uint)rtb.PixelHeight,
                96, 96,
                pixels.ToArray());
            await encoder.FlushAsync();

            if (!_hasCrop || CaptureImageGrid.ActualWidth <= 0 || CaptureImageGrid.ActualHeight <= 0)
            {
                using (var full = new MemoryStream())
                {
                    await ras.AsStreamForRead().CopyToAsync(full);
                    return full.ToArray();
                }
            }

            // 裁剪：显示坐标 → 渲染像素
            double kx = rtb.PixelWidth / CaptureImageGrid.ActualWidth;
            double ky = rtb.PixelHeight / CaptureImageGrid.ActualHeight;
            uint cropX = (uint)Math.Max(0, _cropRect.X * kx);
            uint cropY = (uint)Math.Max(0, _cropRect.Y * ky);
            uint cropW = (uint)Math.Max(1, Math.Min(rtb.PixelWidth - cropX, _cropRect.Width * kx));
            uint cropH = (uint)Math.Max(1, Math.Min(rtb.PixelHeight - cropY, _cropRect.Height * ky));
            var decoder = await BitmapDecoder.CreateAsync(ras);
            var transform = new BitmapTransform
            {
                Bounds = new BitmapBounds { X = cropX, Y = cropY, Width = cropW, Height = cropH }
            };
            var cropped = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
            var outRas = new InMemoryRandomAccessStream();
            var enc2 = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outRas);
            enc2.SetSoftwareBitmap(cropped);
            await enc2.FlushAsync();
            using (var ms = new MemoryStream())
            {
                await outRas.AsStreamForRead().CopyToAsync(ms);
                return ms.ToArray();
            }
        }

        private async void CaptureSaveBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                byte[] bytes = await RenderCaptureCompositeAsync();
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.Downloads,
                    SuggestedFileName = $"网页捕获{(_hasCrop ? "-裁剪" : "")} {DateTime.Now:yyyyMMdd-HHmmss}"
                };
                picker.FileTypeChoices.Add("PNG 图片", new List<string> { ".png" });
                var file = await picker.PickSaveFileAsync();
                if (file == null) return;   // 用户取消：保留批注继续编辑
                await FileIO.WriteBytesAsync(file, bytes);
                NotificationService.ShowToast("已保存", $"已保存到：{file.Path}");
            }
            catch (Exception ex)
            {
                NotificationService.ShowToast("保存失败", ex.Message);
            }
            CloseCapture();
        }

        private async void CaptureCopyBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                byte[] bytes = await RenderCaptureCompositeAsync();
                var ras = new InMemoryRandomAccessStream();
                await ras.WriteAsync(bytes.AsBuffer());
                ras.Seek(0);
                var dp = new DataPackage();
                dp.SetBitmap(RandomAccessStreamReference.CreateFromStream(ras));
                Clipboard.SetContent(dp);
                NotificationService.ShowToast("已复制", "图片已复制到剪贴板");
            }
            catch (Exception ex)
            {
                NotificationService.ShowToast("复制失败", ex.Message);
            }
            CloseCapture();
        }

        private void CaptureCloseBtn_Click(object sender, RoutedEventArgs e) => CloseCapture();

        // ---- TabManager 事件处理 ----
        private void OnTabOrderChanged() => SyncTabPanelOrder();

        // ---- 核心修复：新建标签后自动激活并焦点到地址栏 ----
        private async void OnTabManagerTabCreated(TabViewItem viewItem)
        {
            // 垂直标签模式：新建标签被 TabManager 加到水平面板，需迁移到垂直面板
            if (_isVerticalTabs && viewItem.Container != null)
            {
                var border = viewItem.Container;
                if (border.Parent == TabBarPanel)
                {
                    TabBarPanel.Children.Remove(border);
                    ApplyVerticalTabStyle(border);
                    VerticalTabPanel.Children.Add(border);
                }
            }
            // 切换到新标签
            await _tabManager.SwitchToTabAsync(viewItem);
            UpdateTabLayout();
            toolbarControl.FocusAddressBarAndClear();
        }

        private void OnTabManagerTabChanged(TabViewItem viewItem)
        {
            var tab = viewItem.Tab;
            string currentUrl = tab.CurrentUrl ?? "";
            SyncPageKeyPolling();   // R2-2：活动引擎变化 → 轮询启停
            toolbarControl.SetEngine(tab.Engine);
            toolbarControl.UpdateNavState(tab.CanGoBack, tab.CanGoForward, currentUrl);
            UpdateStarButton();

            if (FindBar.Visibility == Visibility.Visible)
                CloseFindBar();

            bool isSecure = currentUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            string host = GetHostName(GetSiteUrl(currentUrl));
            toolbarControl.UpdateSiteInfo(isSecure, host, ToolbarControl.IsNtpUrl(currentUrl));

            // 副窗口（AppWindow）用 AppWindow.Title 同步标题；主窗口标题随系统
            if (_appWindow != null && PlatformCompat.HasAppWindow)
            {
                AppWindowCompat.SetTitle(_appWindow, tab.Title ?? "新标签页");
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
            SyncPageKeyPolling();   // R2-2：最后一个标签关闭 → 停轮询
            if (count == 0 && !_isEngineSwitching)
                _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank");
        }

        private void OnTabManagerTabUrlChanged(IBrowserTab tab, string url)
        {
            // 导航完成后恢复该页面的文本标记（后台标签页也恢复）
            _ = RestoreMarksDelayedAsync(tab);

            if (_tabManager.CurrentTab == tab)
            {
                if (FindBar.Visibility == Visibility.Visible)
                    CloseFindBar();

                string safeUrl = url ?? "";
                toolbarControl.UpdateNavState(tab.CanGoBack, tab.CanGoForward, safeUrl);
                UpdateStarButton();

                // 阅读器场景下站点信息显示来源网站（src 参数），非阅读器场景用当前地址
                bool isSecure = safeUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                string host = GetHostName(GetSiteUrl(safeUrl));
                toolbarControl.UpdateSiteInfo(isSecure, host, ToolbarControl.IsNtpUrl(safeUrl));
            }
        }

        /// <summary>
        /// 本地阅读器 URL 返回其 src 参数（来源网址），否则原样返回。
        /// </summary>
        private static string GetSiteUrl(string url)
        {
            string src = ExtractQueryParam(url, "src");
            return !string.IsNullOrEmpty(src) ? src : url;
        }

        private static string ExtractQueryParam(string url, string name)
        {
            // 用 TryCreate 而非 new Uri：新建副窗时本方法会在导航完成前收到空/畸形
            // CurrentUrl，抛出的 UriFormatException 即便被吞也会触发调试器首轮中断
            if (string.IsNullOrEmpty(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
                return null;
            var query = parsed.Query;
            if (string.IsNullOrEmpty(query)) return null;
            foreach (var part in query.TrimStart('?').Split('&'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && kv[0] == name)
                    return Uri.UnescapeDataString(kv[1]);
            }
            return null;
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

        // ---- 标签悬停缩略图预览（旧版 Edge 交互：悬停 250ms 出预览卡） ----
        private Windows.UI.Xaml.Controls.Primitives.Popup _tabPreviewPopup;
        private Border _tabPreviewCard;
        private Image _tabPreviewImage;
        private TextBlock _tabPreviewTitle;
        private Windows.UI.Xaml.Controls.ProgressRing _tabPreviewRing;
        private TabViewItem _tabPreviewTarget;
        private readonly Dictionary<IBrowserTab, byte[]> _previewCache = new Dictionary<IBrowserTab, byte[]>();
        private DispatcherTimer _hoverTimer;
        private DispatcherTimer _previewCloseTimer;   // 鼠标离开标签后延迟关闭预览卡
        private bool _isPreviewHovered;               // 鼠标是否在预览卡内
        private const double PreviewW = 240, PreviewH = 150;
        private const int PreviewCloseDelayMs = 300;  // 鼠标离开后 300ms 再关闭

        private void BuildTabPreviewPopup()
        {
            _tabPreviewImage = new Image { Width = PreviewW - 2, Height = PreviewH - 2, Stretch = Stretch.Uniform };
            _tabPreviewRing = new Windows.UI.Xaml.Controls.ProgressRing { IsActive = true, Width = 28, Height = 28 };
            var thumbHost = new Grid { Width = PreviewW - 2, Height = PreviewH - 2, Background = ThemeManager.GetToolbarBackground() };
            thumbHost.Children.Add(_tabPreviewImage);
            thumbHost.Children.Add(_tabPreviewRing);
            _tabPreviewTitle = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(8, 5, 8, 7),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            };
            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(thumbHost);
            stack.Children.Add(_tabPreviewTitle);
            _tabPreviewCard = new Border
            {
                Child = stack,
                Background = ThemeManager.GetToolbarBackground(),
                BorderBrush = ThemeManager.GetSeparatorBrush(),
                BorderThickness = new Thickness(1),
                Width = PreviewW
            };
            // 鼠标移入预览卡 → 取消关闭定时器，保持预览显示
            _tabPreviewCard.PointerEntered += (s, e) =>
            {
                _isPreviewHovered = true;
                _previewCloseTimer?.Stop();
            };
            // 鼠标移出预览卡 → 启动延迟关闭
            _tabPreviewCard.PointerExited += (s, e) =>
            {
                _isPreviewHovered = false;
                SchedulePreviewClose();
            };
            _tabPreviewPopup = new Windows.UI.Xaml.Controls.Primitives.Popup
            {
                IsLightDismissEnabled = false,
                Child = _tabPreviewCard
            };
            PlatformCompat.SetPopupRoot(_tabPreviewPopup, this);
            _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _hoverTimer.Tick += async (s, e) => { _hoverTimer.Stop(); await ShowTabPreviewAsync(); };
            _previewCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PreviewCloseDelayMs) };
            _previewCloseTimer.Tick += (s, e) =>
            {
                _previewCloseTimer.Stop();
                if (!_isPreviewHovered)
                {
                    _tabPreviewTarget = null;
                    _tabPreviewPopup.IsOpen = false;
                }
            };
        }

        private void SchedulePreviewClose()
        {
            if (_previewCloseTimer != null && !_isPreviewHovered)
                _previewCloseTimer.Start();
        }

        private void OnTabHoverStarted(TabViewItem viewItem)
        {
            if (_isInPrivate || viewItem == null) return;   // 隐身窗口不预览
            if (_tabPreviewPopup == null) BuildTabPreviewPopup();
            _tabPreviewTarget = viewItem;
            _hoverTimer.Stop();
            _hoverTimer.Start();
        }

        private void OnTabHoverEnded(TabViewItem viewItem)
        {
            if (_tabPreviewTarget != null && _tabPreviewTarget == viewItem)
            {
                _hoverTimer.Stop();
                // 不再立即关闭：如果鼠标在预览卡内，等 PointerExited 再关
                if (!_isPreviewHovered)
                    SchedulePreviewClose();
            }
        }

        // 导航后旧缩略图作废
        private void OnPreviewCacheInvalidate(IBrowserTab tab, string url) => _previewCache.Remove(tab);

        private async Task ShowTabPreviewAsync()
        {
            var target = _tabPreviewTarget;
            if (target == null || target.Container == null || !_tabManager.Tabs.Contains(target)) return;
            PlatformCompat.SetPopupRoot(_tabPreviewPopup, this);

            // 定位：水平模式→标签上方居中；垂直模式→侧边栏右侧、与标签顶部对齐
            try
            {
                var topLeft = target.Container.TransformToVisual(null).TransformPoint(new Point(0, 0));
                var windowSize = PlatformCompat.GetWindowSize(this);
                double maxW = windowSize.Width;
                double maxH = windowSize.Height;
                if (_isVerticalTabs)
                {
                    // 垂直模式：显示在侧边栏右侧，与标签顶部对齐
                    double sidebarRight = VerticalTabSidebar.ActualWidth;
                    _tabPreviewPopup.HorizontalOffset = Math.Min(sidebarRight + 4, maxW - PreviewW - 8);
                    _tabPreviewPopup.VerticalOffset = Math.Max(8,
                        Math.Min(topLeft.Y, maxH - PreviewH - 40));
                }
                else
                {
                    // 水平模式：标签上方居中（标签栏高 32px，上方需留 34px 间距）
                    double aboveY = topLeft.Y - PreviewH - 6;
                    double belowY = topLeft.Y + target.Container.ActualHeight + 6;
                    // 优先上方；空间不足则放下方
                    _tabPreviewPopup.VerticalOffset = (aboveY >= 0) ? aboveY : belowY;
                    _tabPreviewPopup.HorizontalOffset = Math.Max(8,
                        Math.Min(topLeft.X + target.Container.ActualWidth / 2 - PreviewW / 2, maxW - PreviewW - 8));
                }
            }
            catch { }
            _tabPreviewTitle.Text = target.Tab.Title ?? "";
            _tabPreviewRing.Visibility = Visibility.Visible;
            _tabPreviewImage.Source = null;
            _tabPreviewPopup.IsOpen = true;

            string url = target.Tab.CurrentUrl;
            if (string.IsNullOrEmpty(url) || url == "about:blank" || ToolbarControl.IsNtpUrl(url))
            {
                _tabPreviewRing.Visibility = Visibility.Collapsed;   // 空白/主页无内容可截
                return;
            }

            try
            {
                if (!_previewCache.TryGetValue(target.Tab, out byte[] png))
                {
                    // 后台标签容器 Collapsed → 捕获前临时置可见；期间切到该标签则不还原
                    var container = target.ContentContainer;
                    bool restore = container != null && container.Visibility != Visibility.Visible;
                    if (restore) container.Visibility = Visibility.Visible;
                    try { png = await WebCaptureService.CapturePagePngAsync(target.Tab); }
                    finally
                    {
                        if (restore && container != null && _tabManager.CurrentTab != target.Tab)
                            container.Visibility = Visibility.Collapsed;
                    }
                    if (png != null && png.Length > 0 && _tabPreviewTarget == target)
                        _previewCache[target.Tab] = png;
                }
                if (_tabPreviewTarget != target || png == null || png.Length == 0) return;

                var bmp = new BitmapImage();
                var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var writer = new Windows.Storage.Streams.DataWriter(ras))
                {
                    writer.WriteBytes(png);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                }
                ras.Seek(0);
                await bmp.SetSourceAsync(ras);
                if (_tabPreviewTarget != target) return;
                _tabPreviewImage.Source = bmp;
                _tabPreviewRing.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Log($"[标签预览] 捕获失败: {ex.Message}");
                if (_tabPreviewTarget == target) _tabPreviewRing.Visibility = Visibility.Collapsed;
            }
        }

        // ---- 系统分享（Share contract：标题 + URL 交给系统分享面板） ----
        private async Task ShareCurrentPageAsync()
        {
            var tab = _tabManager?.CurrentTab;
            string url = tab?.CurrentUrl;
            if (string.IsNullOrEmpty(url) || url == "about:blank")
            {
                NotificationService.ShowToast("分享", "当前页面不可分享");
                return;
            }
            try
            {
                var dtm = Windows.ApplicationModel.DataTransfer.DataTransferManager.GetForCurrentView();
                Windows.Foundation.TypedEventHandler<
                    Windows.ApplicationModel.DataTransfer.DataTransferManager,
                    Windows.ApplicationModel.DataTransfer.DataRequestedEventArgs> handler = null;
                handler = (s, args) =>
                {
                    try
                    {
                        var data = args.Request.Data;
                        data.Properties.Title = string.IsNullOrEmpty(tab.Title) ? url : tab.Title;
                        data.SetWebLink(new Uri(url));
                    }
                    finally
                    {
                        s.DataRequested -= handler;   // 一次性挂接，防重复叠加
                    }
                };
                dtm.DataRequested += handler;
                Windows.ApplicationModel.DataTransfer.DataTransferManager.ShowShareUI();
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Log($"[分享] 失败: {ex.Message}");
                NotificationService.ShowToast("分享", "当前环境不支持系统分享");
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
            // 垂直标签模式：LayoutManager 不干预宽度，由 ApplyVerticalTabStyle 统一管理
            if (_isVerticalTabs) return;
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
            // 垂直标签模式：不调用 LayoutManager.Recalculate，避免干预标签宽度
            if (_isVerticalTabs) return;
            int pinnedCount = _tabManager.GetPinnedTabCount();
            _layoutManager.Recalculate(TabBarBorder.ActualWidth, _tabManager.TabCount, pinnedCount);
        }

        private void SyncTabPanelOrder()
        {
            // 垂直模式：同步到 VerticalTabPanel；水平模式：同步到 TabBarPanel
            var targetPanel = _isVerticalTabs ? VerticalTabPanel as Panel : TabBarPanel as Panel;
            if (targetPanel == null) return;
            targetPanel.Children.Clear();
            foreach (var item in _tabManager.Tabs)
            {
                if (item.Container != null)
                    targetPanel.Children.Add(item.Container);
            }
        }

        private void SubscribeWindowSizeChanged()
        {
            if (_sizeChangedSubscription != null) return;
            // 18362+：XamlRoot.Changed（含 AppWindow 同线程副窗）；
            // 17763：每个原生视图有自己的 Window.Current，用 SizeChanged 等价替代。
            _sizeChangedSubscription = PlatformCompat.SubscribeSizeChanged(
                this,
                () => { if (!_isDragging) UpdateTabLayout(); });
        }

        // 真·多视图：每个窗口拥有独立 CoreWindow/XamlRoot。
        // 守卫 = 本窗口处于激活态（本视图 CoreWindow.Activated 维护） && 本窗口有焦点元素。
        private bool IsFocusedWindow()
        {
            if (!_windowActive) return false;
            try { return PlatformCompat.GetFocusedElement(this) != null; }
            catch { return false; }
        }

        // ---- 副视图键盘快捷键（XAML 路由；副视图不挂 CoreWindow 全局钩子） ----
        private void OnWindowKeyDown(object sender, KeyRoutedEventArgs e)
        {
            ShortcutManager.UpdateModifiers(e.Key, true);
            if (ShortcutManager.TryHandle(e.Key))
                e.Handled = true;
        }

        private void OnWindowKeyUp(object sender, KeyRoutedEventArgs e)
        {
            ShortcutManager.UpdateModifiers(e.Key, false);
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

            // 同步 Page 级 XAML 资源（主题字典被 Page.Resources 遮蔽）
            this.Resources["ForegroundBrush"] = colors.ForegroundBrush;
            this.Resources["MutedForegroundBrush"] = colors.MutedForegroundBrush;
            this.Resources["SeparatorBrush"] = colors.SeparatorBrush;
            this.Resources["TabActiveBackgroundBrush"] = colors.TabActiveBackground;
            this.Resources["TabInactiveBackgroundBrush"] = colors.TabInactiveBackground;
            this.Resources["TabHoverBackgroundBrush"] = colors.TabHoverBackground;

            TabBarBorder.Background = colors.ToolbarBackground;
            VerticalTabSidebar.Background = _isVerticalTabs
                ? new SolidColorBrush(Colors.Transparent)
                : colors.ToolbarBackground;
            if (_isVerticalTabs)
            {
                var sidebarGrid = VerticalTabSidebar.Child as Grid;
                if (sidebarGrid != null) sidebarGrid.Background = colors.ToolbarBackground;
            }
            TitleBarHost.Background = colors.ToolbarBackground;
            toolbarControl.ApplySkin(colors, isDark);

            // 垂直侧边栏标题 + 按钮前景色
            VerticalTabTitle.Foreground = colors.ForegroundBrush;
            VerticalNewTabBtn.Foreground = colors.ForegroundBrush;
            VerticalTabToggleBtn.Foreground = colors.ForegroundBrush;
            VerticalTabEmptyText.Foreground = colors.MutedForegroundBrush;
            HubSplitView.PaneBackground = colors.ToolbarBackground;
            SettingsSplitView.PaneBackground = colors.ToolbarBackground;
            FavoritesBar.Background = colors.ToolbarBackground;
            FavoritesBarEmpty.Foreground = colors.MutedForegroundBrush;
            RefreshFavoritesBar();

            hubPane.ApplySkinColors(colors.ForegroundBrush, colors.MutedForegroundBrush, isDark);
            SettingsPaneControl.ApplySkinColors(colors.ToolbarBackground);

            ContentContainer.Background = isDark
                ? new SolidColorBrush(Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B))
                : new SolidColorBrush(Colors.White);

            FindBar.Background = colors.ToolbarBackground;
            FindBar.BorderBrush = colors.MutedForegroundBrush;
            FindResultText.Foreground = colors.MutedForegroundBrush;
            FindPrevBtn.Foreground = colors.ForegroundBrush;
            FindNextBtn.Foreground = colors.ForegroundBrush;
            FindCloseBtn.Foreground = colors.ForegroundBrush;

            // 朗读控制浮层：亚克力背景 + 主题配色（与工具栏一致）
            ReadAloudBar.Background = colors.ToolbarBackground;
            ReadAloudBar.BorderBrush = colors.MutedForegroundBrush;
            ReadAloudIcon.Foreground = colors.MutedForegroundBrush;
            ReadAloudLabel.Foreground = colors.ForegroundBrush;
            ReadAloudPauseBtn.Foreground = colors.ForegroundBrush;
            ReadAloudStopBtn.Foreground = colors.ForegroundBrush;
            ReadAloudCloseBtn.Foreground = colors.ForegroundBrush;

            _tabManager?.UpdateTheme(colors);

            ThemeManager.UpdateTitleBarColors();
            if (_appWindow != null && PlatformCompat.HasAppWindow)
                AppWindowCompat.ApplyChrome(_appWindow, isDark, extendContentIntoTitleBar: false);
            UpdateTabLayout();
        }

        // ---- 工具栏事件 ----
        private async void ToolbarControl_UrlSubmitted(string url)
        {
            if (_tabManager.CurrentTab != null)
            {
                // 历史由 TabManager.UrlChanged 统一记录（占位标题 + TitleChanged 修正），此处不重复添加
                await _tabManager.CurrentTab.NavigateAsync(url);
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

        private async void ToolbarControl_CaptureClicked()
        {
            if (!IsFocusedWindow()) return;
            await WebCaptureCurrentAsync();
        }

        private void ToolbarControl_HubClicked()
        {
            HubSplitView.IsPaneOpen = !HubSplitView.IsPaneOpen;
            if (HubSplitView.IsPaneOpen) hubPane.RefreshAll();
        }

        private void ToolbarControl_MenuClicked()
        {
            var menu = _menuManager.BuildMenu();

            // 垂直标签栏切换（实验功能）
            menu.Items.Add(new MenuFlyoutSeparator());
            var verticalTabItem = new MenuFlyoutItem
            {
                Text = VerticalTabSidebar.Visibility == Visibility.Visible ? "切换到水平标签栏" : "切换到垂直标签栏",
                Icon = new FontIcon { Glyph = "\xE8A0", FontFamily = new FontFamily("Segoe MDL2 Assets") }
            };
            verticalTabItem.Click += (_, _) => ToggleVerticalTabs();
            menu.Items.Add(verticalTabItem);

            toolbarControl.ShowMenu(menu);
        }

        private bool _isVerticalTabs;

        private async void ToggleVerticalTabs()
        {
            _isVerticalTabs = !_isVerticalTabs;
            await Services.SettingsManager.SetAsync("VerticalTabs", _isVerticalTabs.ToString());
            ApplyVerticalTabs();
            ApplyTheme(); // 刷新垂直/水平模式的背景配色
        }

        private void ApplyVerticalTabs()
        {
            if (_isVerticalTabs)
            {
                TabBarBorder.Visibility = Visibility.Collapsed;
                VerticalTabSidebar.Visibility = Visibility.Visible;
                while (TabBarPanel.Children.Count > 0)
                {
                    var child = TabBarPanel.Children[0] as Border;
                    TabBarPanel.Children.RemoveAt(0);
                    if (child != null) ApplyVerticalTabStyle(child);
                    VerticalTabPanel.Children.Add(child);
                }
                _dragManager?.SetTargetPanel(VerticalTabPanel);
                _dragManager.IsVertical = true;
            }
            else
            {
                VerticalTabSidebar.Visibility = Visibility.Collapsed;
                TabBarBorder.Visibility = Visibility.Visible;
                while (VerticalTabPanel.Children.Count > 0)
                {
                    var child = VerticalTabPanel.Children[0] as Border;
                    VerticalTabPanel.Children.RemoveAt(0);
                    if (child != null) ApplyHorizontalTabStyle(child);
                    TabBarPanel.Children.Add(child);
                }
                _dragManager?.SetTargetPanel(TabBarPanel);
                _dragManager.IsVertical = false;
            }
        }

        /// <summary>将标签 Border 从水平样式调整为垂直样式：宽填满、高固定 36px、底部分隔线</summary>
        private static void ApplyVerticalTabStyle(Border border)
        {
            border.Width = Double.NaN;          // 清除固定宽度，允许拉伸
            border.MinWidth = 0;
            border.MaxWidth = Double.PositiveInfinity;
            border.Height = 36;
            border.HorizontalAlignment = HorizontalAlignment.Stretch;
            border.VerticalAlignment = VerticalAlignment.Top;
            border.BorderThickness = new Thickness(0, 0, 0, 1);  // 底部分隔线替代右侧
            border.Margin = new Thickness(0);
        }

        /// <summary>将标签 Border 恢复为水平样式：固定宽度 160px（由 LayoutManager 重算）、高 32px、右侧分隔线</summary>
        private static void ApplyHorizontalTabStyle(Border border)
        {
            border.Width = LayoutManager.MaxTabWidth;
            border.MinWidth = LayoutManager.MinTabWidth;
            border.MaxWidth = LayoutManager.MaxTabWidth;
            border.Height = 32;
            border.HorizontalAlignment = HorizontalAlignment.Left;
            border.VerticalAlignment = VerticalAlignment.Stretch;
            border.BorderThickness = new Thickness(0, 0, 1, 0);  // 右侧分隔线
            border.Margin = new Thickness(0);
        }

        private void VerticalTabToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            ToggleVerticalTabs();
        }

        private async void VerticalNewTabBtn_Click(object sender, RoutedEventArgs e)
        {
            await _tabManager.CreateTabAsync(_defaultEngine, "about:blank");
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

                // 新标签页地址是引擎特有的（EdgeHTML 用 ms-appx-web，WebView2 用
                // contentcache.local）：切换引擎时重置为 about:blank，由新引擎加载自己的主页
                if (ToolbarControl.IsNtpUrl(currentUrl))
                    currentUrl = "about:blank";

                _isEngineSwitching = true;

                await _tabManager.CloseTabAsync(viewItem, false);

                await _tabManager.CreateTabAsync(newEngine, currentUrl);

                SetFocusToCurrentTabContent();

                Debug.WriteLine($"[EngineSwitch] 切换到 {newEngine} 成功");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"引擎切换失败: {ex}");
                var switchErrorDialog = new ContentDialog
                {
                    Title = "引擎切换失败",
                    Content = ex.Message,
                    CloseButtonText = "确定"
                };
                PlatformCompat.SetDialogRoot(switchErrorDialog, this);
                await switchErrorDialog.ShowAsync();
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
                    case "ShowFavoritesBar":
                        ShowFavoritesBar(newValue == "True" || newValue == "true");
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

        // ---- 收藏夹栏(参考规范 §6.1)----
        private void OnFavoritesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, RefreshFavoritesBar);
        }

        private void ShowFavoritesBar(bool visible)
        {
            _favoritesBarVisible = visible;
            if (FavoritesBar == null) return;
            FavoritesBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshFavoritesBar()
        {
            if (FavoritesBarPanel == null) return;
            FavoritesBarPanel.Children.Clear();
            bool isEmpty = true;
            foreach (var fav in FavoritesManager.Instance.Favorites)
            {
                isEmpty = false;
                var star = new FontIcon
                {
                    Glyph = "\uE734",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gold),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                var title = new TextBlock
                {
                    Text = fav.Title ?? fav.Url,
                    FontSize = 13,
                    MaxWidth = 110,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = ThemeManager.Colors.ForegroundBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(star);
                stack.Children.Add(title);
                var btn = new Button
                {
                    Content = stack,
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(6, 3, 6, 3),
                    Margin = new Thickness(0, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                btn.Click += (s, args) => { if (FavoritesBar != null) _ = _tabManager.CurrentTab?.NavigateAsync(fav.Url); };
                btn.RightTapped += (s, args) =>
                {
                    var menu = new MenuFlyout();
                    var openItem = new MenuFlyoutItem { Text = "打开" };
                    openItem.Click += (_, _) => { if (FavoritesBar != null) _ = _tabManager.CurrentTab?.NavigateAsync(fav.Url); };
                    menu.Items.Add(openItem);
                    var removeItem = new MenuFlyoutItem { Text = "从收藏夹移除" };
                    removeItem.Click += (_, _) => _ = FavoritesManager.Instance.RemoveAsync(fav);
                    menu.Items.Add(removeItem);
                    menu.ShowAt(btn, args.GetPosition(btn));
                };
                FavoritesBarPanel.Children.Add(btn);
            }
            FavoritesBarEmpty.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
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
            // PDF：EdgeHTML 引擎无法运行 pdf.js 4.x（现代 JS 语法），转开 WebView2 标签由本地查看器渲染。
            // EPUB：epub.min.js/reader.js 均为 ES5，EdgeHTML 可直接内嵌阅读器（ms-appx-web + ms-appdata）。
            if (url?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    async () => await _tabManager.CreateTabAsync(EngineType.WebView2, url));
                return;
            }
            if (url?.EndsWith(".epub", StringComparison.OrdinalIgnoreCase) == true)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    if (!(_tabManager?.CurrentTab is EdgeHtmlTab edgeTab)) return;
                    string fileParam = await DownloadDocToCacheAsync("epub", url);
                    if (string.IsNullOrEmpty(fileParam)) return;
                    var pageUrl = new Uri(
                        "ms-appx-web:///Assets/EpubReader/reader.html?file=" + Uri.EscapeDataString(fileParam)
                        + "&src=" + Uri.EscapeDataString(url));
                    Services.Diagnostics.Log($"[EPUB 阅读器] EdgeHTML 实际地址: {pageUrl}");
                    await edgeTab.NavigateAsync(pageUrl.ToString());
                });
                return;
            }
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => await _downloadCoordinator.HandleEdgeHtmlDownloadRequested(url));
        }

        /// <summary>
        /// 下载 PDF/EPUB 到 contentcache，返回可供阅读器加载的本地 URL。
        /// </summary>
        private async Task<string?> DownloadDocToCacheAsync(string kind, string url)
        {
            try
            {
                using var client = new Windows.Web.Http.HttpClient();
                var response = await client.GetAsync(new Uri(url));
                response.EnsureSuccessStatusCode();
                var buffer = await response.Content.ReadAsBufferAsync();
                byte[] bytes = WindowsRuntimeBufferExtensions.ToArray(buffer);
                string fileName = kind == "pdf"
                    ? $"doc_{DateTime.Now:yyyyMMdd_HHmmss_fff}.pdf"
                    : "doc_reader.epub";
                var cacheDir = Path.Combine(ApplicationData.Current.LocalFolder.Path, "contentcache");
                Directory.CreateDirectory(cacheDir);
                var filePath = Path.Combine(cacheDir, fileName);
                await File.WriteAllBytesAsync(filePath, bytes);
                Services.Diagnostics.Log($"[阅读器] {kind} 已缓存: {filePath} ({bytes.Length} 字节)");
                return "ms-appdata:///local/contentcache/" + fileName;
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Log($"[阅读器] 缓存 {kind} 失败: {ex.Message}");
                return null;
            }
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
            FindBar.Visibility = Visibility.Visible;
            FindBox.Focus(FocusState.Programmatic);
            FindBox.SelectAll();
            _ = RunFindAsync(FindBox.Text, "new");
        }

        // ---- 页内查找（双引擎注入，带高亮/计数/上一下一）----
        private int _findGeneration;

        private async void FindBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _findGeneration++;
            int gen = _findGeneration;
            await Task.Delay(150);
            if (gen != _findGeneration || FindBar.Visibility != Visibility.Visible)
                return;
            _ = RunFindAsync(FindBox.Text, "new");
        }

        private void FindBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            bool shift = CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Shift) == CoreVirtualKeyStates.Down;
            if (e.Key == VirtualKey.Enter || e.Key == VirtualKey.Down)
            {
                _ = RunFindAsync(FindBox.Text, shift ? "prev" : "next");
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Up)
            {
                _ = RunFindAsync(FindBox.Text, "prev");
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                CloseFindBar();
                e.Handled = true;
            }
        }

        private void FindPrevBtn_Click(object sender, RoutedEventArgs e) => _ = RunFindAsync(FindBox.Text, "prev");

        private void FindNextBtn_Click(object sender, RoutedEventArgs e) => _ = RunFindAsync(FindBox.Text, "next");

        private void FindCloseBtn_Click(object sender, RoutedEventArgs e) => CloseFindBar();

        private async void CloseFindBar()
        {
            FindBar.Visibility = Visibility.Collapsed;
            await RunFindAsync("", "new");
        }

        private async Task RunFindAsync(string query, string mode)
        {
            var tab = _tabManager.CurrentTab;
            if (tab == null) return;
            try
            {
                string result = await tab.ExecuteScriptAsync(BuildFindScript(query ?? "", mode));
                var info = ParseFindResult(result);
                Services.Diagnostics.Log($"[查找] query='{query}' mode={mode} engine={tab.Engine} " +
                    $"result=({(result?.Length ?? -1)}字符) raw={(result == null ? "null" : result.Length > 120 ? result.Substring(0, 120) : result)} " +
                    $"parsed={(info != null ? $"{info.Count}/{info.Index}" : "null")}");
                if (FindBar.Visibility != Visibility.Visible)
                    return;
                FindResultText.Text = (info != null && info.Count > 0) ? $"{info.Index + 1}/{info.Count}" : "0/0";
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Log($"[查找] 异常: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static FindResult ParseFindResult(string raw) => Services.FindService.ParseFindResult(raw);

        private static string BuildFindScript(string query, string mode) => Services.FindService.BuildFindScript(query, mode);

        private async Task ViewSourceAsync()
        {
            if (_tabManager.CurrentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
            {
                try
                {
                    var html = await wv2.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML;");
                    html = html?.Trim('"').Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t");
                    var sourceDialog = new ContentDialog
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
                    };
                    PlatformCompat.SetDialogRoot(sourceDialog, this);
                    await sourceDialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    var sourceErrorDialog = new ContentDialog
                    {
                        Title = "错误",
                        Content = $"获取源代码失败：{ex.Message}",
                        CloseButtonText = "确定"
                    };
                    PlatformCompat.SetDialogRoot(sourceErrorDialog, this);
                    await sourceErrorDialog.ShowAsync();
                }
            }
            else if (_tabManager.CurrentTab is EdgeHtmlTab edge)
            {
                try
                {
                    var html = await edge.ExecuteScriptAsync("document.documentElement.outerHTML;");
                    html = html?.Trim('"').Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t");
                    var sourceDialog = new ContentDialog
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
                    };
                    PlatformCompat.SetDialogRoot(sourceDialog, this);
                    await sourceDialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    var sourceErrorDialog = new ContentDialog
                    {
                        Title = "错误",
                        Content = $"获取源代码失败：{ex.Message}",
                        CloseButtonText = "确定"
                    };
                    PlatformCompat.SetDialogRoot(sourceErrorDialog, this);
                    await sourceErrorDialog.ShowAsync();
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
            // InvariantCulture：zh-CN 环境下浮点会输出 "1,25"，CSS zoom 解析失败
            string z = _zoomFactor.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_tabManager.CurrentTab is EdgeHtmlTab edgeTab)
                _ = edgeTab.ExecuteScriptAsync($"document.body.style.zoom = '{z}';");
            else if (_tabManager.CurrentTab is WebView2Tab wv2)
                _ = wv2.ExecuteScriptAsync($"document.body.style.zoom = '{z}';");
        }

        private void ResetZoom()
        {
            if (_tabManager.CurrentTab is EdgeHtmlTab edgeTab)
                _ = edgeTab.ExecuteScriptAsync("document.body.style.zoom = '1';");
            else if (_tabManager.CurrentTab is WebView2Tab wv2)
                _ = wv2.ExecuteScriptAsync("document.body.style.zoom = '1';");
            _zoomFactor = 1.0;
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
            var bookmarkAllDialog = new ContentDialog
            {
                Title = "完成",
                Content = $"已将 {count} 个标签页加入收藏。",
                CloseButtonText = "确定"
            };
            PlatformCompat.SetDialogRoot(bookmarkAllDialog, this);
            await bookmarkAllDialog.ShowAsync();
        }

        private async Task ClearBrowsingDataAsync()
        {
            await HistoryManager.ClearAsync();
            DownloadManager.ClearCompleted();
            _tabManager.ClearClosedTabs();   // 与 Metro 设置面板语义一致
            if (HubSplitView.IsPaneOpen)
                hubPane.RefreshAll();
            await ClearAllCookiesInternalAsync();
            var clearedDialog = new ContentDialog
            {
                Title = "已清除",
                Content = "浏览数据已清除。",
                CloseButtonText = "确定"
            };
            PlatformCompat.SetDialogRoot(clearedDialog, this);
            await clearedDialog.ShowAsync();
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
            var dialog = new ContentDialog
            {
                Title = "Edge Rebuild",
                Content = "版本 0.2 Alpha\n基于 UWP 的双内核浏览器外壳。",
                CloseButtonText = "确定"
            };
            PlatformCompat.SetDialogRoot(dialog, this);
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
            PlatformCompat.SetDialogRoot(dialog, this);
            await dialog.ShowAsync();
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
                _isInPrivate = args.IsInPrivate;
                _pendingSessionSlot = Math.Max(0, args.SessionSlot);
                _restoreSlot = args.RestoreSlot;
                Services.Diagnostics.Log($"[窗口] OnNavigatedTo window=secondary url={args.Url} slot={_pendingSessionSlot} restore={_restoreSlot} thread={Environment.CurrentManagedThreadId} presentation={args.Presentation}");
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

        /// 进入/退出 InPrivate 模式（当前窗口内切换，不依赖 AppWindow 子窗口）：
        /// 进入——不写历史、不存会话、新标签页为隐身主页；退出——关闭全部标签，
        /// 相当于结束一次隐身会话。
        private void ToggleInPrivateMode()
        {
            _isInPrivate = !_isInPrivate;
            _tabManager.IsInPrivateWindow = _isInPrivate;
            toolbarControl.InPrivateMode = _isInPrivate;
            hubPane.SuppressHistory = _isInPrivate;
            InPrivateBadge.Visibility = _isInPrivate ? Visibility.Visible : Visibility.Collapsed;

            if (_isInPrivate)
            {
                Services.Diagnostics.Log("[InPrivate] 已进入 InPrivate 模式");
            }
            else
            {
                Services.Diagnostics.Log("[InPrivate] 已退出 InPrivate 模式，清除会话标签");
                _tabManager.ClearClosedTabs();
                _ = ClearInPrivateTabsAsync();
            }
        }

        private async Task ClearInPrivateTabsAsync()
        {
            foreach (var viewItem in _tabManager.Tabs.ToList())
                await _tabManager.CloseTabAsync(viewItem, false);
            await _tabManager.CreateTabAsync(_defaultEngine, "about:blank");
        }
    }

    public class FindResult
    {
        public int Count { get; set; }
        public int Index { get; set; }
    }
}