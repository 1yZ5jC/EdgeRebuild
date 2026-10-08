using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.WindowManagement;
using EdgeRebuild.Controls;
using EdgeRebuild.Core;
using EdgeRebuild.Services;

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
        private AppWindow _appWindow;
        private TabManager _tabManager;
        private EngineType _defaultEngine = EngineType.EdgeHtml;
        private string _pendingUrl;
        private string _presentation;
        private bool _isEngineSwitching;
        private bool _setupStarted;

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

            var colors = ThemeManager.Colors;
            toolbar.ApplySkin(colors, ThemeManager.IsDarkTheme);
            TabBarBorder.Background = TabStripBrush(ThemeManager.IsDarkTheme);
            ThemeManager.ThemeChanged += OnThemeChanged;

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

            if (_tabManager != null)
            {
                _tabManager.TabCreated -= OnTabCreated;
                _tabManager.TabChanged -= OnTabChanged;
                _tabManager.TabUrlChanged -= OnTabUrlChanged;
                _tabManager.TabCanGoBackChanged -= OnTabGoBackChanged;
                _tabManager.TabCanGoForwardChanged -= OnTabGoForwardChanged;
                _tabManager.TabCountChanged -= OnTabCountChanged;
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
            => await _tabManager.SwitchToTabAsync(viewItem);

        private void OnTabChanged(TabViewItem viewItem)
        {
            var tab = viewItem.Tab;
            toolbar.SetEngine(tab.Engine);
            ApplyNavState(tab);

            TitleText.Text = string.IsNullOrEmpty(tab.Title) ? "新标签页" : tab.Title;
            if (_appWindow != null)
            {
                try { _appWindow.Title = TitleText.Text; } catch { }
            }
            SetFocusToContent();
        }

        private void OnTabUrlChanged(IBrowserTab tab, string url)
        {
            if (_tabManager?.CurrentTab != tab) return;
            ApplyNavState(tab);
            TitleText.Text = string.IsNullOrEmpty(tab.Title) ? "新标签页" : tab.Title;
            if (_appWindow != null)
            {
                try { _appWindow.Title = TitleText.Text; } catch { }
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

        private static Brush TabStripBrush(bool isDark)
            => new SolidColorBrush(isDark
                ? Color.FromArgb(0xFF, 0x20, 0x20, 0x20)
                : Color.FromArgb(0xFF, 0xF2, 0xF2, 0xF2));

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
            TabBarBorder.Background = TabStripBrush(isDark);
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
