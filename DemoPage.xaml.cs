using EdgeRebuild.Controls;
using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace EdgeRebuild
{
    /// <summary>
    /// 多窗口 Demo 页。
    /// 已恢复：地址栏工具栏（ToolbarControl）、标签条、浏览器窗体（TabManager）。
    /// 暂未恢复：收藏夹栏、Hub/设置面板、网页捕获、快捷键、会话持久化。
    /// 主窗口 slot=0（App 直接导航），副窗口 slot=1..n（WindowManager 分配）。
    /// 底部调试条显示本窗口的呈现方案（原生多视图 / AppWindow 同线程 / 主窗口）。
    /// </summary>
    public sealed partial class DemoPage : Page
    {
        private int _slot;
        private bool _bound;
        private object _appWindow;   // AppWindow（18362+）；object 避免 17763 类型解析失败
        private IDisposable _sizeChangedSubscription;
        private TabManager _tabManager;
        private EngineType _defaultEngine = EngineType.EdgeHtml;
        private string _pendingUrl;
        private string _presentation;
        private bool _isEngineSwitching;
        private bool _setupStarted;

        // ---- 标签条布局 / 拖拽 / 右键菜单（Phase 1 恢复） ----
        private bool _isLoaded;
        private bool _isDragging;
        private LayoutManager _layoutManager;
        private TabDragManager _dragManager;
        private TabContextMenuManager _contextMenuManager;

        public DemoPage()
        {
            InitializeComponent();
            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is NewWindowArgs args)
            {
                _slot = args.SessionSlot;
                _appWindow = args.AppWindow;
                _pendingUrl = args.Url;
                _presentation = args.Presentation;
            }
        }

        // ================================================================
        //  初始化
        // ================================================================

        private async void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            // 必须在第一个 await 之前同步占位：SetupAsync 里 _tabManager 的赋值晚于
            // await SettingsManager，期间 Loaded 再次触发会跑两遍，开出重复标签
            if (!_setupStarted)
            {
                _setupStarted = true;
                await SetupAsync();
                _isLoaded = true;
                UpdateTabLayout();
            }

            if (!_bound)
            {
                _bound = true;
                GotFocus += (s, e2) => WindowManager.ReportSlotFocus(_slot);
                WindowManager.SlotActivated += OnSlotActivated;
            }
            OnSlotActivated(WindowManager.ActiveSlot);
        }

        private async Task SetupAsync()
        {
            Diagnostics.Log($"[Demo] 初始化 slot={_slot} thread={Environment.CurrentManagedThreadId}");

            var uiThread = Environment.CurrentManagedThreadId;
            var uiDispatcher = Dispatcher;
            string eng = await SettingsManager.GetAsync("DefaultEngine") ?? "EdgeHtml";
            Diagnostics.Log($"[Demo] GetAsync后续体 thread={Environment.CurrentManagedThreadId} " +
                $"(await前线程={uiThread}) " +
                $"ctx={(System.Threading.SynchronizationContext.Current?.GetType().Name ?? "NULL")} " +
                $"hasAccess={uiDispatcher.HasThreadAccess} slot={_slot}");
            _defaultEngine = eng == "WebView2" ? EngineType.WebView2 : EngineType.EdgeHtml;

            // ---- 标签条布局计算（标签宽度 / 溢出滚动 / 右侧安全区留白） ----
            _layoutManager = new LayoutManager(
                TabBarBorder,
                TabScrollViewer,
                TabBarPanel,
                RightSidePanel,
                ScrollLeftBtn,
                ScrollRightBtn);
            _layoutManager.LayoutUpdated += OnLayoutUpdated;
            // 窗口尺寸变化（含副窗口拖拽缩放）→ 重算标签宽度
            SubscribeWindowSizeChanged();

            _tabManager = new TabManager
            {
                // Demo 不写共享会话快照，避免覆盖真实浏览器窗口的恢复数据
                ParticipatesInSession = false,
                SessionSlot = _slot
            };
            _tabManager.Initialize(TabBarPanel, ContentContainer);

            _tabManager.TabCreated += OnTabCreated;
            _tabManager.TabChanged += OnTabChanged;
            _tabManager.TabUrlChanged += OnTabUrlChanged;
            _tabManager.TabCanGoBackChanged += OnTabGoBackChanged;
            _tabManager.TabCanGoForwardChanged += OnTabGoForwardChanged;
            _tabManager.TabCountChanged += OnTabCountChanged;
            _tabManager.TabOrderChanged += OnTabOrderChanged;

            // ---- 拖拽排序 / 拖出成新窗口 / 右键菜单 ----
            _dragManager = new TabDragManager(_tabManager, TabBarPanel, DragCanvas, _layoutManager);
            _dragManager.SortRequested += OnDragSortRequested;
            _dragManager.DetachRequested += OnDragDetachRequested;
            _dragManager.DragStarted += () => _isDragging = true;
            _dragManager.DragCompleted += () =>
            {
                _isDragging = false;
                UpdateTabLayout();
            };
            _dragManager.TabRightTapped += (border, viewItem) =>
                _contextMenuManager?.ShowTabRightClickMenu(border, viewItem);
            // 现有标签 + 后续新建标签都挂上拖拽事件
            foreach (var tab in _tabManager.Tabs)
                _dragManager.AttachEvents(tab);
            _tabManager.TabCreated += viewItem => _dragManager.AttachEvents(viewItem);

            _contextMenuManager = new TabContextMenuManager(
                _tabManager,
                _defaultEngine,
                () => _tabManager.CurrentTab?.Engine ?? _defaultEngine,
                PrintCurrentPageAsync,
                ViewSourceAsync,
                viewItem => _ = _tabManager.TogglePinTabAsync(viewItem),
                WebCaptureCurrentAsync,
                ShareCurrentPageAsync);

            var colors = ThemeManager.Colors;
            toolbar.ApplySkin(colors, ThemeManager.IsDarkTheme);
            // 背景放在 TitleBarHost（= 标题栏拖拽区）上；TabBarBorder 不设背景，
            // 否则会拦掉 Window.SetTitleBar 的命中，导致标签条空白处无法拖动窗口
            TitleBarHost.Background = colors.ToolbarBackground;
            ThemeManager.ThemeChanged += OnThemeChanged;

            SetupTitleBar();

            string homeUrl = await SettingsManager.GetAsync("HomePage") ?? "about:blank";
            string url = string.IsNullOrEmpty(_pendingUrl) ? homeUrl : _pendingUrl;
            await _tabManager.CreateTabAsync(_defaultEngine, url);

            UpdateModeText();
            Diagnostics.Log($"[Demo] 首个标签 engine={_defaultEngine} url={url}");
        }

        /// <summary>窗口关闭时由 NativeWindowManager 调用（AppWindow 销毁后 Unloaded 不可靠）。</summary>
        public void Shutdown()
        {
            Diagnostics.Log($"[Demo] 关闭清理 slot={_slot}");
            if (_bound)
            {
                _bound = false;
                WindowManager.SlotActivated -= OnSlotActivated;
            }
            ThemeManager.ThemeChanged -= OnThemeChanged;

            UnsubscribeWindowSizeChanged();
            if (_layoutManager != null)
            {
                _layoutManager.LayoutUpdated -= OnLayoutUpdated;
                _layoutManager.Dispose();
                _layoutManager = null;
            }

            if (_tabManager != null)
            {
                _tabManager.TabCreated -= OnTabCreated;
                _tabManager.TabChanged -= OnTabChanged;
                _tabManager.TabUrlChanged -= OnTabUrlChanged;
                _tabManager.TabCanGoBackChanged -= OnTabGoBackChanged;
                _tabManager.TabCanGoForwardChanged -= OnTabGoForwardChanged;
                _tabManager.TabCountChanged -= OnTabCountChanged;
                _tabManager.TabOrderChanged -= OnTabOrderChanged;
                _tabManager.DisposeAll();
                _tabManager = null;
            }
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            // 注意：AppWindow 内容销毁时本事件不保证触发，真正的清理走 Shutdown()
        }

        // ================================================================
        //  标签 → 工具栏 同步
        // ================================================================

        private async void OnTabCreated(TabViewItem viewItem)
        {
            await _tabManager.SwitchToTabAsync(viewItem);
            UpdateTabLayout();
        }

        private void OnTabChanged(TabViewItem viewItem)
        {
            var tab = viewItem.Tab;
            toolbar.SetEngine(tab.Engine);
            ApplyNavState(tab);

            TitleText.Text = string.IsNullOrEmpty(tab.Title) ? "新标签页" : tab.Title;
            if (_appWindow != null && PlatformCompat.HasAppWindow)
            {
                AppWindowCompat.SetTitle(_appWindow, TitleText.Text);
            }
            SetFocusToContent();
        }

        private void OnTabUrlChanged(IBrowserTab tab, string url)
        {
            if (_tabManager?.CurrentTab != tab) return;
            ApplyNavState(tab);
            TitleText.Text = string.IsNullOrEmpty(tab.Title) ? "新标签页" : tab.Title;
            if (_appWindow != null && PlatformCompat.HasAppWindow)
            {
                AppWindowCompat.SetTitle(_appWindow, TitleText.Text);
            }
        }

        private void OnTabGoBackChanged(IBrowserTab tab, bool canGoBack)
        {
            if (_tabManager?.CurrentTab == tab)
                toolbar.UpdateNavState(canGoBack, tab.CanGoForward, tab.CurrentUrl ?? "");
        }

        private void OnTabGoForwardChanged(IBrowserTab tab, bool canGoForward)
        {
            if (_tabManager?.CurrentTab == tab)
                toolbar.UpdateNavState(tab.CanGoBack, canGoForward, tab.CurrentUrl ?? "");
        }

        private void OnTabCountChanged(int count)
        {
            UpdateModeText();
            UpdateTabLayout();
            // 关掉最后一个标签 → 重建一个空标签（引擎切换时由 _isEngineSwitching 屏蔽）
            if (count == 0 && !_isEngineSwitching)
                _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank");
        }

        // ================================================================
        //  底部调试条：窗口呈现方案
        // ================================================================

        private void UpdateModeText()
        {
            ModeText.Text =
                $"{PresentationLabel()} · 线程 {Environment.CurrentManagedThreadId}" +
                $" · slot {_slot} · 标签 {_tabManager?.Tabs.Count ?? 0}";
        }

        private string PresentationLabel() => _presentation switch
        {
            nameof(NativeWindow.WindowMode.NativeView) => "原生多视图 CreateNewView",
            nameof(NativeWindow.WindowMode.AppWindow) => "AppWindow 同线程",
            _ => "主窗口"
        };

        // ================================================================
        //  标题栏：内容扩展进标题栏（系统标题栏条消失，按钮保留透明底）
        // ================================================================

        private void SetupTitleBar()
        {
            try
            {
                if (_appWindow != null)
                {
                    // AppWindow 副窗口（18362+）
                    if (PlatformCompat.HasAppWindow)
                        AppWindowCompat.ApplyChrome(_appWindow, ThemeManager.IsDarkTheme);
                }
                else
                {
                    // 主窗口 + 原生多视图窗口：各自是独立视图，按当前线程取视图（17763 可用）
                    WindowChrome.ApplyLegacy(TitleBarDragArea, ThemeManager.IsDarkTheme);
                }
                Diagnostics.Log($"[Demo] 已隐藏系统标题栏 slot={_slot} mode={PresentationLabel()}");
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[Demo] 隐藏标题栏失败 slot={_slot}: {ex.Message}");
            }
        }

        private void ApplyAppWindowTitleBarColors()
        {
            if (_appWindow == null || !PlatformCompat.HasAppWindow) return;
            AppWindowCompat.ApplyChrome(_appWindow, ThemeManager.IsDarkTheme);
        }

        private async void OnNewTab(object sender, RoutedEventArgs e)
        {
            if (_tabManager == null) return;
            await _tabManager.CreateTabAsync(_defaultEngine, "about:blank");
            SetFocusToContent();
        }

        private void ApplyNavState(IBrowserTab tab)
        {
            string url = tab.CurrentUrl ?? "";
            toolbar.UpdateNavState(tab.CanGoBack, tab.CanGoForward, url);
            bool secure = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            toolbar.UpdateSiteInfo(secure, GetHost(url), ToolbarControl.IsNtpUrl(url));
        }

        private static string GetHost(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "";
        }

        private void SetFocusToContent()
        {
            var element = _tabManager?.CurrentTab?.ViewElement;
            if (element == null) return;

            if (element is Microsoft.UI.Xaml.Controls.WebView2 wv2)
            {
                wv2.Focus(FocusState.Programmatic);
                return;
            }
            if (element is Control control)
                control.Focus(FocusState.Programmatic);
        }

        private void OnThemeChanged(SkinManager.SkinColors colors, bool isDark)
        {
            // 原生多视图模式下事件可能在别的窗口线程触发，回派到本页线程
            if (!Dispatcher.HasThreadAccess)
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () => OnThemeChanged(colors, isDark));
                return;
            }
            toolbar.ApplySkin(colors, isDark);
            TitleBarHost.Background = colors.ToolbarBackground;
            if (_appWindow != null) ApplyAppWindowTitleBarColors();
            else WindowChrome.ApplyApplicationViewTitleBarColors(isDark);
            _tabManager?.UpdateTheme(colors);
        }

        // ================================================================
        //  工具栏 → 网页
        // ================================================================

        private async void OnUrlSubmitted(string url)
        {
            if (_tabManager?.CurrentTab == null) return;
            await _tabManager.CurrentTab.NavigateAsync(url);
            SetFocusToContent();
        }

        private async void OnBackRequested()
        {
            var tab = _tabManager?.CurrentTab;
            if (tab?.CanGoBack != true) return;
            await tab.GoBackAsync();
            SetFocusToContent();
        }

        private async void OnForwardRequested()
        {
            var tab = _tabManager?.CurrentTab;
            if (tab?.CanGoForward != true) return;
            await tab.GoForwardAsync();
            SetFocusToContent();
        }

        private async void OnRefreshRequested()
        {
            var tab = _tabManager?.CurrentTab;
            if (tab == null) return;
            await tab.RefreshAsync();
            SetFocusToContent();
        }

        private async void OnEngineSwitched(EngineType newEngine)
        {
            try
            {
                var current = _tabManager?.CurrentTab;
                if (current == null || current.Engine == newEngine) return;

                var viewItem = _tabManager.Tabs.FirstOrDefault(v => v.Tab == current);
                if (viewItem == null) return;

                string currentUrl = current.CurrentUrl;
                // 新标签页地址是引擎特有的（EdgeHTML 用 ms-appx-web，WebView2 用
                // contentcache.local）：切换引擎时重置为 about:blank，由新引擎加载自己的主页
                if (ToolbarControl.IsNtpUrl(currentUrl)) currentUrl = "about:blank";

                _isEngineSwitching = true;
                await _tabManager.CloseTabAsync(viewItem, false);
                await _tabManager.CreateTabAsync(newEngine, currentUrl);
                SetFocusToContent();
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[Demo] 引擎切换失败: {ex.Message}");
            }
            finally
            {
                _isEngineSwitching = false;
            }
        }

        private void OnMenuClicked()
        {
            var menu = new MenuFlyout();
            var item = new MenuFlyoutItem { Text = "打开新窗口" };
            item.Click += async (_, _) => await OpenNewDemoWindowAsync();
            menu.Items.Add(item);
            toolbar.ShowMenu(menu);
        }

        // ================================================================
        //  标签条：布局计算 / 拖拽排序 / 右键菜单
        // ================================================================

        private void OnTabOrderChanged()
        {
            SyncTabPanelOrder();
            UpdateTabLayout();
        }

        private void OnDragSortRequested(int fromIndex, int toIndex)
        {
            _tabManager.ReorderTab(fromIndex, toIndex);
            SyncTabPanelOrder();
        }

        private async void OnDragDetachRequested(TabViewItem item)
        {
            string url = item.Tab.CurrentUrl;
            EngineType engine = item.Tab.Engine;
            Diagnostics.Log($"[Demo] 拖拽分离 url={url} engine={engine}");
            bool shown = await WindowManager.OpenNewWindowAsync(engine, url);
            Diagnostics.Log($"[Demo] 分离→新窗口 shown={shown}");
            if (shown)
            {
                await _tabManager.CloseTabAsync(item, false);
            }
            else
            {
                NotificationService.ShowToast("无法分离标签", "新窗口未能打开，标签保留在原窗口");
            }
        }

        private void OnLayoutUpdated()
        {
            if (_tabManager == null || _layoutManager == null) return;
            double tabWidth = _layoutManager.TabWidth;
            foreach (var item in _tabManager.Tabs)
            {
                if (item.Container != null)
                    item.Container.Width = tabWidth;
            }
        }

        private void UpdateTabLayout()
        {
            if (!_isLoaded || _tabManager == null || _layoutManager == null) return;
            if (_tabManager.TabCount == 0 || _isDragging) return;
            int pinnedCount = _tabManager.GetPinnedTabCount();
            _layoutManager.Recalculate(TabBarBorder.ActualWidth, _tabManager.TabCount, pinnedCount);
        }

        private void SyncTabPanelOrder()
        {
            if (_tabManager == null || TabBarPanel == null) return;
            TabBarPanel.Children.Clear();
            foreach (var item in _tabManager.Tabs)
            {
                if (item.Container != null)
                    TabBarPanel.Children.Add(item.Container);
            }
        }

        private void SubscribeWindowSizeChanged()
        {
            if (_sizeChangedSubscription != null) return;
            // 18362+ XamlRoot.Changed；17763 回退到本视图 Window.Current.SizeChanged
            _sizeChangedSubscription = PlatformCompat.SubscribeSizeChanged(
                this,
                () => { if (!_isDragging) UpdateTabLayout(); });
        }

        private void UnsubscribeWindowSizeChanged()
        {
            _sizeChangedSubscription?.Dispose();
            _sizeChangedSubscription = null;
        }

        private void TabBarBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isDragging) UpdateTabLayout();
        }

        private void ScrollLeftBtn_Click(object sender, RoutedEventArgs e) => _layoutManager?.ScrollToLeft();
        private void ScrollRightBtn_Click(object sender, RoutedEventArgs e) => _layoutManager?.ScrollToRight();
        private void TabScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) { }

        // ---- 右键菜单里尚未恢复的条目：点占位，Phase 6 再接真实实现 ----
        private Task PrintCurrentPageAsync()
        {
            Diagnostics.Log("[Demo] 打印：本阶段未恢复");
            return Task.CompletedTask;
        }

        private Task ViewSourceAsync()
        {
            Diagnostics.Log("[Demo] 查看源文件：本阶段未恢复");
            return Task.CompletedTask;
        }

        private Task WebCaptureCurrentAsync()
        {
            Diagnostics.Log("[Demo] 网页捕获：本阶段未恢复（Phase 6）");
            return Task.CompletedTask;
        }

        private Task ShareCurrentPageAsync()
        {
            Diagnostics.Log("[Demo] 共享：本阶段未恢复");
            return Task.CompletedTask;
        }

        // ================================================================
        //  多窗口
        // ================================================================

        private async void OnNewWindow(object sender, RoutedEventArgs e)
            => await OpenNewDemoWindowAsync();

        private async Task OpenNewDemoWindowAsync()
        {
            NewWindowBtn.IsEnabled = false;
            try
            {
                bool ok = await WindowManager.OpenDemoWindowAsync();
                Diagnostics.Log($"[Demo] 开新窗口结果={ok} 已注册副窗口={WindowManager.WindowCount}");
                if (!ok) LogText.Text = "开窗失败，详见 LocalState\\crash.log";
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[Demo] 开新窗口异常: {ex}");
                LogText.Text = $"异常: {ex.Message}";
            }
            finally
            {
                NewWindowBtn.IsEnabled = true;
            }
        }

        private async void OnOpenMainPageWindow(object sender, RoutedEventArgs e)
        {
            // 诊断入口：MainPage 副窗口是 G2 崩溃路径，用它稳定复现
            MainPageWinBtn.IsEnabled = false;
            try
            {
                bool ok = await WindowManager.OpenNewWindowAsync(EngineType.EdgeHtml, "about:blank");
                Diagnostics.Log($"[Demo] 开 MainPage 窗口结果={ok} 已注册副窗口={WindowManager.WindowCount}");
                LogText.Text = ok ? "MainPage 窗口已开" : "开窗失败，见 crash.log";
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[Demo] 开 MainPage 窗口异常: {ex}");
                LogText.Text = $"异常: {ex.Message}";
            }
            finally
            {
                MainPageWinBtn.IsEnabled = true;
            }
        }

        private void OnSlotActivated(int slot)
        {
            // SlotActivated 由触发线程同步广播；原生多视图下必须回派到本页线程
            if (!Dispatcher.HasThreadAccess)
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () => OnSlotActivated(slot));
                return;
            }
            ActiveText.Text = $"激活槽位：{slot}" + (slot == _slot ? "（本窗口）" : "");
        }
    }
}
