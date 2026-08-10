using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using EdgeRebuild.Core;
using EdgeRebuild.Services;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.WindowManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

namespace EdgeRebuild.Metro
{
    /// <summary>
    /// Metro IE 风格窗口：底部只有标签条 + 地址栏，其余功能收进底部命令栏（Windows 8.1 AppBar
    /// 形态，平时隐藏、右键唤出）；网页空白处右键同样唤出命令栏，链接/图片/选区右键弹就近
    /// 小菜单。色板固定（MetroColorPalette），与主窗口皮肤完全隔离；快捷键自处理。
    /// </summary>
    public sealed partial class MetroPage : Page
    {
        private AppWindow _appWindow;
        private EngineType _defaultEngine;
        private TabManager _tabManager;
        private bool _gridOpen;
        private bool _commandBarOpen;
        private bool _settingsOpen;
        private bool _isFullscreen;
        private bool _isReadingAloud;
        private double _zoomFactor = 1.0;
        private readonly Dictionary<IBrowserTab, string> _readerModeUrls = new Dictionary<IBrowserTab, string>();
        private readonly DispatcherTimer _stripRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };

        // EdgeHTML 页面右键 marker 轮询（与 MainPage 同机制：UWP WebView 无控件级右键事件，
        // 页面脚本写入 window.__edgCtxMarker，宿主定时读取；WebView2 走 ContextMenuRequested）
        private readonly DispatcherTimer _ctxPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };

        public MetroPage()
        {
            InitializeComponent();
            _stripRefreshTimer.Tick += (s, e) =>
            {
                _stripRefreshTimer.Stop();
                TabStrip.Refresh(_tabManager.Tabs, FindViewItem(_tabManager.CurrentTab));
            };
            _ctxPollTimer.Tick += CtxPollTimer_Tick;
            _ctxPollTimer.Start();
        }

        // 标签条内容变化（标题/图标/URL/加载态）防抖刷新，避免高频事件刷屏重建
        private void RefreshStripSoon()
        {
            _stripRefreshTimer.Stop();
            _stripRefreshTimer.Start();
        }

        private void AttachStripRefresh(TabViewItem viewItem)
        {
            var tab = viewItem.Tab;
            tab.TitleChanged += _ => RefreshStripSoon();
            tab.FaviconChanged += _ => RefreshStripSoon();
            tab.UrlChanged += _ => RefreshStripSoon();
            tab.LoadingChanged += _ => RefreshStripSoon();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var args = e.Parameter as NewWindowArgs;
            _defaultEngine = args?.Engine ?? EngineType.WebView2;
            _appWindow = args?.AppWindow;
            string url = args?.Url ?? "about:blank";

            _tabManager = new TabManager(() => MetroColorPalette.GetSkinColors());
            _tabManager.Initialize(null, ContentPanel);
            _tabManager.TabCreated += AttachStripRefresh;
            _tabManager.TabClosed += viewItem => _readerModeUrls.Remove(viewItem.Tab);
            _tabManager.TabCountChanged += count =>
            {
                RefreshStripSoon();
                if (_commandBarOpen) CommandBar.UpdateTabCount(count);
                // Metro IE 不允许无标签窗口：最后一个标签关闭后自动新建
                if (count == 0)
                    _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank", false, true);
            };
            _tabManager.TabChanged += viewItem => OnTabChanged(viewItem);
            _tabManager.TabUrlChanged += (tab, tabUrl) =>
            {
                if (tab == _tabManager.CurrentTab)
                    AddressBar.SetUrl(tabUrl);
            };
            _tabManager.ContextMenuRequested += OnTabContextMenuRequested;

            if (_appWindow != null)
            {
                try { _appWindow.Title = "Metro IE"; } catch { }
                try
                {
                    _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                    _appWindow.TitleBar.BackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x2D, 0x2D, 0x30);
                    _appWindow.TitleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x2D, 0x2D, 0x30);
                    _appWindow.TitleBar.ForegroundColor = Windows.UI.Colors.White;
                    _appWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x2D, 0x2D, 0x30);
                    _appWindow.TitleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x2D, 0x2D, 0x30);
                    _appWindow.TitleBar.ButtonForegroundColor = Windows.UI.Colors.White;
                    _appWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
                }
                catch { }
            }

            _ = _tabManager.CreateTabAsync(_defaultEngine, url, false, true);
        }

        private void OnTabChanged(TabViewItem viewItem)
        {
            var tab = viewItem?.Tab;
            if (tab == null) return;
            AddressBar.SetUrl(tab.CurrentUrl);
            AddressBar.UpdateNavButtons(tab.CanGoBack, tab.CanGoForward);
            TopBarTitle.Text = tab.Title ?? "Metro IE";
            if (_appWindow != null)
            {
                try { _appWindow.Title = tab.Title ?? "Metro IE"; } catch { }
            }
            TabStrip.Refresh(_tabManager.Tabs, FindViewItem(_tabManager.CurrentTab));
            RefreshGridIfOpen();
            FadeContentIn();
        }

        // ---- 全屏（AppWindowPresenter.RequestPresentation(FullScreen)；进入时隐藏标题栏） ----
        private void ToggleFullscreen()
        {
            if (_appWindow == null) return;
            try
            {
                bool ok;
                if (_isFullscreen)
                    ok = _appWindow.Presenter.RequestPresentation(AppWindowPresentationKind.Default);
                else
                    ok = _appWindow.Presenter.RequestPresentation(AppWindowPresentationKind.FullScreen);

                if (!ok)
                {
                    NotificationService.ShowToast("Metro IE", "当前系统不支持全屏模式");
                    return;
                }
                _isFullscreen = !_isFullscreen;
                TopBar.Visibility = _isFullscreen ? Visibility.Collapsed : Visibility.Visible;
                if (_commandBarOpen) CommandBar.SetFullscreenText(_isFullscreen);
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Log($"[Metro] 全屏切换失败: {ex.Message}");
                NotificationService.ShowToast("Metro IE", "全屏切换失败");
            }
        }

        // ---- 底部命令栏（Windows 8.1 AppBar：平时隐藏，右键/命令唤出，滑入动画） ----
        private void OpenCommandBar()
        {
            if (_commandBarOpen) return;
            _commandBarOpen = true;
            CommandBar.UpdateTabCount(_tabManager.Tabs.Count);
            CommandBar.SetFullscreenText(_isFullscreen);
            CommandBar.Visibility = Visibility.Visible;
            CommandBar.RenderTransform = new TranslateTransform { Y = 48 };
            var sb = new Storyboard();
            var slide = new DoubleAnimation
            {
                From = 48,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(slide, CommandBar);
            Storyboard.SetTargetProperty(slide, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            sb.Children.Add(slide);
            sb.Begin();
        }

        private void CloseCommandBar()
        {
            if (!_commandBarOpen) return;
            _commandBarOpen = false;
            var sb = new Storyboard();
            var slide = new DoubleAnimation { From = 0, To = 48, Duration = TimeSpan.FromMilliseconds(120) };
            Storyboard.SetTarget(slide, CommandBar);
            Storyboard.SetTargetProperty(slide, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            sb.Children.Add(slide);
            sb.Completed += (s, e) => { if (!_commandBarOpen) CommandBar.Visibility = Visibility.Collapsed; };
            sb.Begin();
        }

        private void ToggleCommandBar()
        {
            if (_commandBarOpen) CloseCommandBar();
            else OpenCommandBar();
        }

        // ---- 设置面板（Windows 8 形态：从右侧滑入，点外部/Esc/关闭按钮收起） ----
        private void OpenSettingsPanel()
        {
            if (_settingsOpen) return;
            _settingsOpen = true;
            SettingsPanel.UpdateZoomPercent((int)Math.Round(_zoomFactor * 100));
            SettingsPanel.Visibility = Visibility.Visible;
            SettingsPanel.RenderTransform = new TranslateTransform { X = 346 };
            var sb = new Storyboard();
            var slide = new DoubleAnimation
            {
                From = 346,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(slide, SettingsPanel);
            Storyboard.SetTargetProperty(slide, "(UIElement.RenderTransform).(TranslateTransform.X)");
            sb.Children.Add(slide);
            sb.Begin();
        }

        private void CloseSettingsPanel()
        {
            if (!_settingsOpen) return;
            _settingsOpen = false;
            var sb = new Storyboard();
            var slide = new DoubleAnimation { From = 0, To = 346, Duration = TimeSpan.FromMilliseconds(120) };
            Storyboard.SetTarget(slide, SettingsPanel);
            Storyboard.SetTargetProperty(slide, "(UIElement.RenderTransform).(TranslateTransform.X)");
            sb.Children.Add(slide);
            sb.Completed += (s, e) => { if (!_settingsOpen) SettingsPanel.Visibility = Visibility.Collapsed; };
            sb.Begin();
        }

        // 缩放（与主窗口同机制：双引擎 document.body.style.zoom 注入；Culture 无关格式防 JS 解析失败）
        private void ApplyZoom(double factor)
        {
            _zoomFactor = Math.Max(0.25, Math.Min(5.0, factor));
            var tab = _tabManager?.CurrentTab;
            if (tab is EdgeHtmlTab edgeTab)
                _ = edgeTab.ExecuteScriptAsync($"document.body.style.zoom = '{_zoomFactor.ToString(CultureInfo.InvariantCulture)}';");
            else if (tab is WebView2Tab wv2)
                _ = wv2.ExecuteScriptAsync($"document.body.style.zoom = '{_zoomFactor.ToString(CultureInfo.InvariantCulture)}';");
            if (_settingsOpen) SettingsPanel.UpdateZoomPercent((int)Math.Round(_zoomFactor * 100));
        }

        private void SettingsPanel_CloseRequested() => CloseSettingsPanel();
        private void SettingsPanel_ZoomInRequested() => ApplyZoom(_zoomFactor + 0.25);
        private void SettingsPanel_ZoomOutRequested() => ApplyZoom(_zoomFactor - 0.25);
        private void SettingsPanel_ZoomResetRequested() => ApplyZoom(1.0);

        private async void SettingsPanel_ClearHistoryRequested()
        {
            await HistoryManager.ClearAsync();
            _tabManager.ClearClosedTabs();
            NotificationService.ShowToast("设置", "已清除浏览历史与关闭的标签页");
        }

        // ---- 地址栏 ----
        private void AddressBar_BackRequested() => _ = _tabManager.CurrentTab?.GoBackAsync();
        private void AddressBar_ForwardRequested() => _ = _tabManager.CurrentTab?.GoForwardAsync();
        private void AddressBar_RefreshRequested() => _ = _tabManager.CurrentTab?.RefreshAsync();
        private void AddressBar_HomeRequested() => _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank", false, true);
        private void AddressBar_FullscreenRequested() => ToggleFullscreen();
        private async void AddressBar_UrlSubmitted(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;
            // 网页/搜索二合一 + 中文域名 Punycode：无点号单词 → 搜索；www。cn 全角点也可当网址
            string searchEngine = "Google";
            try { searchEngine = await SettingsManager.GetAsync("SearchEngine") ?? "Google"; } catch { }
            string url = UrlInputResolver.Resolve(input, searchEngine);
            if (string.IsNullOrEmpty(url)) return;
            _ = _tabManager.CreateTabAsync(_defaultEngine, url, false, true);
        }

        // ---- 底部标签条 ----
        private void TabStrip_TabSelected(TabViewItem viewItem) => _ = _tabManager.SwitchToTabAsync(viewItem);
        private void TabStrip_TabCloseRequested(TabViewItem viewItem) => _ = _tabManager.CloseTabAsync(viewItem);
        private void TabStrip_NewTabRequested() => _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank", false, true);

        // ---- 命令栏按钮 ----
        private void CommandBar_NewTabClicked()
        {
            CloseCommandBar();
            _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank", false, true);
        }
        private void CommandBar_TabsClicked()
        {
            CloseCommandBar();
            ToggleGrid();
        }
        private void CommandBar_RefreshClicked()
        {
            CloseCommandBar();
            _ = _tabManager.CurrentTab?.RefreshAsync();
        }
        private void CommandBar_FullscreenClicked()
        {
            CloseCommandBar();
            ToggleFullscreen();
        }
        private void CommandBar_SettingsClicked()
        {
            CloseCommandBar();
            OpenSettingsPanel();
        }

        // ---- 「更多」菜单：主界面功能补全（阅读模式/朗读/打印/收藏/复制网址） ----
        private void CommandBar_MoreClicked()
        {
            CloseCommandBar();
            var menu = new MenuFlyout();
            var tab = _tabManager?.CurrentTab;

            var readerItem = new MenuFlyoutItem
            {
                Text = (tab != null && _readerModeUrls.ContainsKey(tab)) ? "退出阅读模式" : "阅读模式",
                Icon = new FontIcon { Glyph = "\xE8A5", FontFamily = new FontFamily("Segoe MDL2 Assets") }
            };
            readerItem.Click += (s, a) => ToggleReaderMode();
            menu.Items.Add(readerItem);

            var ttsItem = new MenuFlyoutItem
            {
                Text = _isReadingAloud ? "停止朗读" : "朗读此页",
                Icon = new FontIcon { Glyph = "\xE744", FontFamily = new FontFamily("Segoe MDL2 Assets") }
            };
            ttsItem.Click += (s, a) => ToggleReadAloud();
            menu.Items.Add(ttsItem);

            var printItem = new MenuFlyoutItem
            {
                Text = "打印",
                Icon = new FontIcon { Glyph = "\xE749", FontFamily = new FontFamily("Segoe MDL2 Assets") }
            };
            printItem.Click += (s, a) => _ = PrintCurrentTabAsync();
            menu.Items.Add(printItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var favItem = new MenuFlyoutItem
            {
                Text = "添加到收藏夹",
                Icon = new FontIcon { Glyph = "\xE734", FontFamily = new FontFamily("Segoe MDL2 Assets") }
            };
            favItem.Click += (s, a) => AddCurrentToFavorites();
            menu.Items.Add(favItem);

            var copyItem = new MenuFlyoutItem
            {
                Text = "复制网址",
                Icon = new FontIcon { Glyph = "\xE8C8", FontFamily = new FontFamily("Segoe MDL2 Assets") }
            };
            copyItem.Click += (s, a) => CopyCurrentUrl();
            menu.Items.Add(copyItem);

            menu.ShowAt(this, new Point(ActualWidth / 2, ActualHeight - 40));
        }

        // ---- 阅读模式（与主窗口同机制：ReaderService 注入阅读视图，可再点退出） ----
        private async void ToggleReaderMode()
        {
            var tab = _tabManager?.CurrentTab;
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
                _readerModeUrls[tab] = currentUrl;
            else
                NotificationService.ShowToast("无法使用阅读模式", "此页面没有可提取的正文内容");
        }

        // ---- 朗读（与主窗口同机制：ReaderService 提取 + ReadAloudService 播放） ----
        private void ToggleReadAloud()
        {
            var tab = _tabManager?.CurrentTab;
            if (tab == null) return;
            if (_isReadingAloud)
            {
                ReadAloudService.Stop();
                _isReadingAloud = false;
                return;
            }
            _ = StartReadAloudAsync(tab);
        }

        private async Task StartReadAloudAsync(IBrowserTab tab)
        {
            string text = null;
            string title = null;
            try
            {
                var article = await ReaderService.ExtractTextForTtsAsync(tab);
                if (article != null)
                {
                    text = article.Text;
                    if (!string.IsNullOrWhiteSpace(article.Title)) title = article.Title;
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
            _isReadingAloud = true;
            ReadAloudService.Start(text, title);
        }

        // ---- 打印（双引擎：WebView2 系统打印 UI / EdgeHTML window.print） ----
        private async Task PrintCurrentTabAsync()
        {
            var tab = _tabManager?.CurrentTab;
            if (tab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                wv2.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
            else if (tab is EdgeHtmlTab edgeTab)
                await edgeTab.ExecuteScriptAsync("window.print();");
        }

        // ---- 收藏 / 复制网址 ----
        private async void AddCurrentToFavorites()
        {
            var tab = _tabManager?.CurrentTab;
            if (tab == null) return;
            string url = tab.CurrentUrl;
            if (string.IsNullOrEmpty(url) || url == "about:blank")
            {
                NotificationService.ShowToast("添加到收藏夹", "当前页面不可收藏");
                return;
            }
            if (FavoritesManager.Instance.ContainsUrl(url))
            {
                NotificationService.ShowToast("添加到收藏夹", "该网址已在收藏夹中");
                return;
            }
            await FavoritesManager.Instance.AddAsync(tab.Title ?? url, url);
            NotificationService.ShowToast("已添加到收藏夹", tab.Title ?? url);
        }

        private void CopyCurrentUrl()
        {
            var tab = _tabManager?.CurrentTab;
            if (tab == null) return;
            string url = tab.CurrentUrl;
            if (string.IsNullOrEmpty(url) || url == "about:blank") return;
            var dp = new DataPackage();
            dp.SetText(url);
            Clipboard.SetContent(dp);
            NotificationService.ShowToast("已复制", "网址已复制到剪贴板");
        }

        // ---- 页内查找（Ctrl+F，双引擎经 FindService 注入；Esc 关闭） ----
        private void FindBar_CloseRequested()
        {
            _ = RunFindAsync("", "new");   // 清空页面高亮
            FindBar.Visibility = Visibility.Collapsed;
        }

        private void FindBar_FindRequested(string query, string mode) => _ = RunFindAsync(query ?? "", mode);

        private async Task RunFindAsync(string query, string mode)
        {
            var tab = _tabManager?.CurrentTab;
            if (tab == null) return;
            try
            {
                string result = await tab.ExecuteScriptAsync(FindService.BuildFindScript(query ?? "", mode));
                var info = FindService.ParseFindResult(result);
                if (info != null)
                    FindBar.SetCount(info.Index, info.Count);
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Log($"[查找] 异常: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ---- 右键：Chrome 区（标题栏/标签条/地址栏/空白）→ 设置面板开着则收起，否则切换命令栏 ----
        private void Root_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (_settingsOpen) CloseSettingsPanel();
            else ToggleCommandBar();
            e.Handled = true;
        }

        // 鼠标左键在面板外点击 → 逐层收起（设置面板 → 命令栏；WebView2 内容区为独立
        // HWND 不进 XAML，仅 EdgeHTML 内容与 Chrome 区可触发，与真实 Metro 行为一致）
        private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType != Windows.Devices.Input.PointerDeviceType.Mouse) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (_settingsOpen && !IsWithinElement(e.OriginalSource as DependencyObject, SettingsPanel))
            {
                CloseSettingsPanel();
                return;
            }
            if (_commandBarOpen && !IsWithinElement(e.OriginalSource as DependencyObject, CommandBar))
                CloseCommandBar();
        }

        private static bool IsWithinElement(DependencyObject src, DependencyObject target)
        {
            DependencyObject cur = src;
            while (cur != null)
            {
                if (cur == target) return true;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return false;
        }

        // ---- 网页右键：空白 → 命令栏；链接/图片/选区 → 就近小菜单 ----
        private void OnTabContextMenuRequested(TabContextMenuEventArgs args)
        {
            double dy = (TopBar.Visibility == Visibility.Visible) ? TopBar.ActualHeight : 0;
            ShowMetroContextMenu(args.MenuType, args.LinkUrl, args.ImageUrl, args.SelectionText,
                new Point(args.Location.X, args.Location.Y + dy));
        }

        private async void CtxPollTimer_Tick(object sender, object e)
        {
            if (_tabManager?.CurrentTab is EdgeHtmlTab edge)
            {
                string raw = await edge.PollContextMenuAsync();
                if (!string.IsNullOrEmpty(raw))
                    ShowEdgeHtmlContextMenu(raw);
            }
        }

        private void ShowEdgeHtmlContextMenu(string raw)
        {
            // marker 格式：kind|x|y|encodeURIComponent(值)
            var parts = raw.Split('|');
            if (parts.Length < 3) return;
            string kind = parts[0];
            if (!double.TryParse(parts[1], out double x) || !double.TryParse(parts[2], out double y)) return;
            string val = parts.Length > 3 ? Uri.UnescapeDataString(parts[3]) : "";
            double dy = (TopBar.Visibility == Visibility.Visible) ? TopBar.ActualHeight : 0;
            switch (kind)
            {
                case "link":
                    ShowMetroContextMenu(ContextMenuType.Link, val, null, "", new Point(x, y + dy));
                    break;
                case "image":
                    ShowMetroContextMenu(ContextMenuType.Image, null, val, "", new Point(x, y + dy));
                    break;
                case "selection":
                    ShowMetroContextMenu(ContextMenuType.Selection, null, null, val, new Point(x, y + dy));
                    break;
                default:
                    OpenCommandBar();
                    break;
            }
        }

        private void ShowMetroContextMenu(ContextMenuType type, string linkUrl, string imageUrl, string selection, Point pt)
        {
            var tabManager = _tabManager;
            if (tabManager == null) return;

            // 空白处右键 → 唤出底部命令栏（Windows 8.1：内容区右键 = AppBar）
            if (type == ContextMenuType.Page)
            {
                OpenCommandBar();
                return;
            }

            EngineType engine = tabManager.CurrentTab?.Engine ?? _defaultEngine;
            var menu = new MenuFlyout();

            if (type == ContextMenuType.Link && !string.IsNullOrEmpty(linkUrl))
            {
                var openItem = new MenuFlyoutItem { Text = "在新标签打开" };
                openItem.Click += (s, a) => _ = tabManager.CreateTabAsync(engine, linkUrl, false, true);
                menu.Items.Add(openItem);

                var copyItem = new MenuFlyoutItem { Text = "复制链接" };
                copyItem.Click += (s, a) => CopyText(linkUrl);
                menu.Items.Add(copyItem);
            }
            else if (type == ContextMenuType.Image && !string.IsNullOrEmpty(imageUrl))
            {
                var openItem = new MenuFlyoutItem { Text = "在新标签打开图片" };
                openItem.Click += (s, a) => _ = tabManager.CreateTabAsync(engine, imageUrl, false, true);
                menu.Items.Add(openItem);

                var copyItem = new MenuFlyoutItem { Text = "复制图片地址" };
                copyItem.Click += (s, a) => CopyText(imageUrl);
                menu.Items.Add(copyItem);
            }
            else if (type == ContextMenuType.Selection && !string.IsNullOrEmpty(selection))
            {
                var copyItem = new MenuFlyoutItem { Text = "复制" };
                copyItem.Click += (s, a) => CopyText(selection);
                menu.Items.Add(copyItem);
            }

            if (menu.Items.Count > 0)
                menu.ShowAt(this, pt);
        }

        private static void CopyText(string text)
        {
            try
            {
                var dp = new DataPackage();
                dp.SetText(text);
                Clipboard.SetContent(dp);
            }
            catch { }
        }

        // ---- 缩略图网格（淡入动画） ----
        // 诊断输出：VS 输出窗口即时可见 + crash.log 落盘（含完整堆栈）
        private static void MetroDebug(string msg)
        {
            System.Diagnostics.Debug.WriteLine("[Metro] " + msg);
            Services.Diagnostics.Log("[Metro] " + msg);
        }

        private void ToggleGrid()
        {
            MetroDebug($"ToggleGrid: 当前状态={(_gridOpen ? "开" : "关")}");
            if (_gridOpen) CloseGrid();
            else OpenGrid();
        }

        private void OpenGrid()
        {
            MetroDebug($"OpenGrid: 标签数={_tabManager.Tabs.Count} 当前={( _tabManager.CurrentTab == null ? "null" : _tabManager.CurrentTab.Title)}");
            _gridOpen = true;
            // 覆盖层始终 Visible(构造即可见,窗口显示时完成首次布局——延迟可见化的
            // ScrollViewer 首次真实布局会冻结本 AppWindow 的 UI 线程),这里只恢复
            // 命中与透明度。打开期间收起网页容器,避免 EdgeHTML WebView 气泡区遮挡。
            SetWebContentVisible(false);
            TabGridOverlay.IsHitTestVisible = true;
            MetroDebug($"OpenGrid 可见后: Overlay Actual={TabGridOverlay.ActualWidth}x{TabGridOverlay.ActualHeight}");
            try
            {
                TabGridOverlay.Refresh(_tabManager.Tabs, FindViewItem(_tabManager.CurrentTab));
            }
            catch (Exception ex)
            {
                MetroDebug($"标签网格刷新异常: {ex}");
            }
            var sb = new Storyboard();
            var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(160) };
            Storyboard.SetTarget(fade, TabGridOverlay);
            Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(fade);
            sb.Completed += (s, e) => MetroDebug("标签视图淡入完成");
            sb.Begin();
            MetroDebug("OpenGrid 完成");
        }

        private void CloseGrid()
        {
            _gridOpen = false;
            var sb = new Storyboard();
            var fade = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(120) };
            Storyboard.SetTarget(fade, TabGridOverlay);
            Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Completed += (s, e) =>
            {
                // 淡出完成后恢复网页容器并取消命中(控件保持 Visible,避免触发延迟布局)
                SetWebContentVisible(true);
                TabGridOverlay.IsHitTestVisible = false;
                TabGridOverlay.Opacity = 0;
            };
            sb.Begin();
        }

        private void RefreshGridIfOpen()
        {
            if (_gridOpen)
            {
                MetroDebug("RefreshGridIfOpen: 重建网格");
                TabGridOverlay.Refresh(_tabManager.Tabs, FindViewItem(_tabManager.CurrentTab));
            }
        }

        // 标签视图开启期间收起全部网页容器(当前标签也收起,由 TabManager 的切换语义恢复),
        // 避免 EdgeHTML WebView 气泡区问题影响覆盖层渲染;关闭时仅恢复当前标签可见
        private void SetWebContentVisible(bool show)
        {
            try
            {
                foreach (var item in _tabManager.Tabs)
                {
                    var c = item.ContentContainer;
                    if (c == null) continue;
                    c.Visibility = show
                        ? (item == FindViewItem(_tabManager?.CurrentTab) ? Visibility.Visible : Visibility.Collapsed)
                        : Visibility.Collapsed;
                }
                MetroDebug($"SetWebContentVisible({show}): 已切 {_tabManager.Tabs.Count} 个网页容器");
            }
            catch (Exception ex)
            {
                MetroDebug($"SetWebContentVisible 异常: {ex}");
            }
        }

        private void FadeContentIn()
        {
            ContentPanel.Opacity = 0.55;
            var sb = new Storyboard();
            var fade = new DoubleAnimation { From = 0.55, To = 1, Duration = TimeSpan.FromMilliseconds(130) };
            Storyboard.SetTarget(fade, ContentPanel);
            Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(fade);
            sb.Begin();
        }

        private TabViewItem FindViewItem(IBrowserTab tab)
        {
            if (tab == null) return null;
            foreach (var item in _tabManager.Tabs)
                if (item.Tab == tab) return item;
            return null;
        }

        private void TabGrid_TabSelected(TabViewItem viewItem)
        {
            MetroDebug($"网格选择标签: {viewItem.Tab.Title}");
            CloseGrid();
            _ = _tabManager.SwitchToTabAsync(viewItem);
        }

        private void TabGrid_TabCloseRequested(TabViewItem viewItem)
        {
            MetroDebug($"网格关闭标签: {viewItem.Tab.Title}");
            _ = _tabManager.CloseTabAsync(viewItem);
            RefreshGridIfOpen();
        }

        private void TabGrid_NewTabRequested()
        {
            MetroDebug("网格新建标签");
            CloseGrid();
            _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank", false, true);
        }

        private void TabGrid_DismissRequested() => CloseGrid();

        // ---- 键盘（Metro 窗口独立快捷键，不经过全局 ShortcutManager） ----
        private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrl = Windows.UI.Core.CoreWindow.GetForCurrentThread()?.GetKeyState(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) ?? false;
            var shift = Windows.UI.Core.CoreWindow.GetForCurrentThread()?.GetKeyState(VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) ?? false;

            if (ctrl)
            {
                if (TryHandleCtrlNumber(e.Key)) { e.Handled = true; return; }
                switch (e.Key)
                {
                    case VirtualKey.Tab:
                        e.Handled = true;
                        CycleTab(shift ? -1 : 1);
                        break;
                    case VirtualKey.T:
                        e.Handled = true;
                        _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank", false, true);
                        break;
                    case VirtualKey.W:
                        e.Handled = true;
                        _ = _tabManager.CloseTabAsync(FindViewItem(_tabManager.CurrentTab));
                        break;
                    case VirtualKey.L:
                        e.Handled = true;
                        AddressBar.FocusUrl();
                        break;
                    case VirtualKey.N:
                        e.Handled = true;
                        _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank", false, true);
                        break;
                    case VirtualKey.R:
                        e.Handled = true;
                        _ = _tabManager.CurrentTab?.RefreshAsync();
                        break;
                    case VirtualKey.F:
                        e.Handled = true;
                        FindBar.Show();
                        break;
                }
            }
            else if (e.Key == VirtualKey.F11)
            {
                e.Handled = true;
                ToggleFullscreen();
            }
            else if (e.Key == VirtualKey.Escape)
            {
                // Esc 逐层收起：查找栏 → 设置面板 → 命令栏 → 缩略图网格 → 全屏
                if (FindBar.Visibility == Visibility.Visible)
                {
                    e.Handled = true;
                    FindBar_CloseRequested();
                }
                else if (_settingsOpen)
                {
                    e.Handled = true;
                    CloseSettingsPanel();
                }
                else if (_commandBarOpen)
                {
                    e.Handled = true;
                    CloseCommandBar();
                }
                else if (_gridOpen)
                {
                    e.Handled = true;
                    CloseGrid();
                }
                else if (_isFullscreen)
                {
                    e.Handled = true;
                    ToggleFullscreen();
                }
            }
        }

        // Ctrl+Tab / Ctrl+Shift+Tab → 循环切换标签（与主窗口 ShortcutManager 一致）
        private void CycleTab(int delta)
        {
            var list = _tabManager.Tabs;
            if (list.Count == 0) return;
            int idx = _tabManager.GetTabIndex(FindViewItem(_tabManager.CurrentTab));
            if (idx < 0) idx = 0;
            else idx = (idx + delta + list.Count) % list.Count;
            _ = _tabManager.SwitchToTabAsync(list[idx]);
        }

        // Ctrl+1..8 → 第 N 个标签；Ctrl+9 → 最后一个标签
        private bool TryHandleCtrlNumber(VirtualKey key)
        {
            int target = -1;
            switch (key)
            {
                case VirtualKey.Number1: target = 0; break;
                case VirtualKey.Number2: target = 1; break;
                case VirtualKey.Number3: target = 2; break;
                case VirtualKey.Number4: target = 3; break;
                case VirtualKey.Number5: target = 4; break;
                case VirtualKey.Number6: target = 5; break;
                case VirtualKey.Number7: target = 6; break;
                case VirtualKey.Number8: target = 7; break;
                case VirtualKey.Number9: target = -2; break;   // -2 = 最后一个
            }
            if (target == -1) return false;
            if (target == -2)
            {
                if (_tabManager.Tabs.Count == 0) return true;
                target = _tabManager.Tabs.Count - 1;
            }
            if (target >= _tabManager.Tabs.Count) return true;
            _ = _tabManager.SwitchToTabAsync(_tabManager.Tabs[target]);
            return true;
        }

        private void Page_KeyUp(object sender, KeyRoutedEventArgs e) { }
    }
}
