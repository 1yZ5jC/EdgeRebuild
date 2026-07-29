using EdgeRebuild.Controls;
using EdgeRebuild.Core;
using EdgeRebuild.Services;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

using TabViewItem = EdgeRebuild.Core.TabViewItem;
using WebView2 = Microsoft.UI.Xaml.Controls.WebView2;

namespace EdgeRebuild
{
    public sealed partial class MainPage : Page
    {
        public static MainPage Instance { get; private set; }

        // ---- 字段 ----
        private double _zoomFactor = 1.0;
        private bool _isLoaded;
        private string _pendingUrl;
        private bool _askBeforeDownload;
        private EngineType _defaultEngine = EngineType.EdgeHtml;
        private readonly Dictionary<string, WebView2> _downloadWebViews = new();

        // ---- 管理器 ----
        private TabManager _tabManager;
        private LayoutManager _layoutManager;

        // ---- 拖拽相关 ----
        private TabViewItem _dragItem;
        private Border _dragGhost;
        private Point _dragOffset;
        private Point _dragStartScreenPoint;
        private Point _dragStartCanvasPoint;
        private bool _isDragging;
        private bool _hasMoved;
        private double _totalDx;
        private bool _isDetaching = false;
        private Point _tabBarPanelOffset;
        private DateTime _lastReorderTime;

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
            CleanupAllDownloadWebViews();
            Window.Current.SizeChanged -= OnWindowSizeChanged;
            ShortcutManager.Disable();
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _layoutManager?.Dispose();
            _tabManager?.DisposeAll();

            // 取消订阅 TabManager 事件
            if (_tabManager != null)
            {
                _tabManager.TabCreated -= OnTabManagerTabCreated;
                _tabManager.TabChanged -= OnTabManagerTabChanged;
                _tabManager.TabCountChanged -= OnTabManagerTabCountChanged;
                _tabManager.TabUrlChanged -= OnTabManagerTabUrlChanged;
                _tabManager.TabCanGoBackChanged -= OnTabManagerCanGoBackChanged;
                _tabManager.TabCanGoForwardChanged -= OnTabManagerCanGoForwardChanged;
                _tabManager.TabSuspendedChanged -= OnTabManagerTabSuspendedChanged;
                _tabManager.NewWindowRequested -= OnTabManagerNewWindowRequested;
                _tabManager.ContextMenuRequested -= OnTabManagerContextMenuRequested;
            }
        }

        private void CleanupStaticEvents()
        {
            DownloadManager.DownloadProgressChanged -= OnDownloadProgress;
            DownloadManager.DownloadStatusChanged -= OnDownloadStatusChanged;
            DownloadManager.RetryDownloadRequested -= OnRetryDownloadRequested;
            EdgeHtmlTab.DownloadRequested -= OnEdgeHtmlDownloadRequested;
            SettingsManager.SettingChanged -= OnSettingChanged;
        }

        private void CleanupAllDownloadWebViews()
        {
            foreach (var kvp in _downloadWebViews.ToList())
            {
                try { (kvp.Value.Parent as Panel)?.Children.Remove(kvp.Value); kvp.Value.Close(); } catch { }
            }
            _downloadWebViews.Clear();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;

            try
            {
                // ---- 初始化布局管理器 ----
                _layoutManager = new LayoutManager(
                    TabBarBorder,
                    TabScrollViewer,
                    TabBarPanel,
                    RightSidePanel,
                    ScrollLeftBtn,
                    ScrollRightBtn
                );
                _layoutManager.LayoutUpdated += OnLayoutUpdated;

                // ---- 初始化标签管理器 ----
                _tabManager = new TabManager();
                _tabManager.Initialize(TabBarPanel, ContentContainer);
                _tabManager.TabCreated += OnTabManagerTabCreated;
                _tabManager.TabChanged += OnTabManagerTabChanged;
                _tabManager.TabCountChanged += OnTabManagerTabCountChanged;
                _tabManager.TabUrlChanged += OnTabManagerTabUrlChanged;
                _tabManager.TabCanGoBackChanged += OnTabManagerCanGoBackChanged;
                _tabManager.TabCanGoForwardChanged += OnTabManagerCanGoForwardChanged;
                _tabManager.TabSuspendedChanged += OnTabManagerTabSuspendedChanged;
                _tabManager.NewWindowRequested += OnTabManagerNewWindowRequested;
                _tabManager.ContextMenuRequested += OnTabManagerContextMenuRequested;

                // 为每个标签附加拖拽事件（标签创建时由 TabManager 触发）
                _tabManager.TabCreated += AttachDragEventsToTab;

                CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
                Window.Current.SetTitleBar(TitleBarDragArea);
                ThemeManager.UpdateTitleBarColors();

                Window.Current.SizeChanged += OnWindowSizeChanged;

                ThemeManager.ThemeChanged += OnThemeChanged;
                ApplyTheme();

                string askSetting = await SettingsManager.GetAsync("AskBeforeDownload");
                _askBeforeDownload = (askSetting == "True" || askSetting == "true");

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
                ShortcutManager.OnPrint += () => PrintCurrentPage();
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
        private void OnTabManagerTabCreated(TabViewItem viewItem)
        {
            // TabManager 已经创建了 UI，我们只需要响应事件
            UpdateTabLayout();
            toolbarControl.FocusAddressBarAndClear();
        }

        private void OnTabManagerTabChanged(TabViewItem viewItem)
        {
            // 更新工具栏
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
            ShowTabContextMenu(args);
        }

        // ---- 为每个标签附加拖拽事件 ----
        private void AttachDragEventsToTab(TabViewItem viewItem)
        {
            if (viewItem.Container != null)
            {
                viewItem.Container.PointerPressed += OnTabPointerPressed;
                viewItem.Container.PointerMoved += OnTabPointerMoved;
                viewItem.Container.PointerReleased += OnTabPointerReleased;
                viewItem.Container.PointerCanceled += OnTabPointerReleased;
                viewItem.Container.RightTapped += OnTabRightTapped;
            }
        }

        // ---- 上下文菜单 ----
        private void ShowTabContextMenu(TabContextMenuEventArgs args)
        {
            if (_tabManager.CurrentTab is EdgeHtmlTab) return;
            var flyout = new MenuFlyout();
            ApplyThemeToFlyout(flyout);

            var copyItem = new MenuFlyoutItem { Text = "复制", IsEnabled = args.HasSelection };
            copyItem.Click += async (_, _) =>
            {
                if (_tabManager.CurrentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                {
                    string selectedText = await wv2.CoreWebView2.ExecuteScriptAsync("window.getSelection().toString();");
                    if (!string.IsNullOrEmpty(selectedText))
                    {
                        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                        dp.SetText(selectedText);
                        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                    }
                }
            };
            flyout.Items.Add(copyItem);

            var pasteItem = new MenuFlyoutItem { Text = "粘贴", IsEnabled = args.IsEditable };
            pasteItem.Click += async (_, _) =>
                await new ContentDialog { Title = "粘贴", Content = "请使用键盘快捷键 Ctrl+V 进行粘贴。", CloseButtonText = "确定" }.ShowAsync();
            flyout.Items.Add(pasteItem);

            var selectAllItem = new MenuFlyoutItem { Text = "全选" };
            selectAllItem.Click += async (_, _) =>
            {
                if (_tabManager.CurrentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                    await wv2.CoreWebView2.ExecuteScriptAsync("document.execCommand('selectAll');");
            };
            flyout.Items.Add(selectAllItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var backItem = new MenuFlyoutItem { Text = "后退", IsEnabled = args.CanGoBack };
            backItem.Click += (_, _) => _tabManager.CurrentTab?.GoBackAsync();
            flyout.Items.Add(backItem);

            var forwardItem = new MenuFlyoutItem { Text = "前进", IsEnabled = args.CanGoForward };
            forwardItem.Click += (_, _) => _tabManager.CurrentTab?.GoForwardAsync();
            flyout.Items.Add(forwardItem);

            var refreshItem = new MenuFlyoutItem { Text = "刷新" };
            refreshItem.Click += (_, _) => _tabManager.CurrentTab?.RefreshAsync();
            flyout.Items.Add(refreshItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            if (args.MenuType == ContextMenuType.Link && !string.IsNullOrEmpty(args.LinkUrl))
            {
                var openInNewTabItem = new MenuFlyoutItem { Text = "在新标签页中打开" };
                openInNewTabItem.Click += async (_, _) =>
                    await _tabManager.CreateTabAsync(_tabManager.CurrentTab?.Engine ?? _defaultEngine, args.LinkUrl);
                flyout.Items.Add(openInNewTabItem);

                var copyLinkItem = new MenuFlyoutItem { Text = "复制链接" };
                copyLinkItem.Click += (_, _) =>
                {
                    var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dp.SetText(args.LinkUrl);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                };
                flyout.Items.Add(copyLinkItem);
            }

            if (args.MenuType == ContextMenuType.Image && !string.IsNullOrEmpty(args.ImageUrl))
            {
                var copyImageUrlItem = new MenuFlyoutItem { Text = "复制图片地址" };
                copyImageUrlItem.Click += (_, _) =>
                {
                    var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dp.SetText(args.ImageUrl);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                };
                flyout.Items.Add(copyImageUrlItem);
            }

            flyout.Items.Add(new MenuFlyoutSeparator());

            var printItem = new MenuFlyoutItem { Text = "打印" };
            printItem.Click += (_, _) => PrintCurrentPage();
            flyout.Items.Add(printItem);

            var sourceItem = new MenuFlyoutItem { Text = "查看页面源代码" };
            sourceItem.Click += async (_, _) => await ViewSourceAsync();
            flyout.Items.Add(sourceItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            if (_tabManager.CurrentTab is WebView2Tab wv2Tab && wv2Tab.CoreWebView2 != null)
            {
                var inspectItem = new MenuFlyoutItem { Text = "检查元素 (F12)" };
                inspectItem.Click += (_, _) => wv2Tab.CoreWebView2.OpenDevToolsWindow();
                flyout.Items.Add(inspectItem);
            }

            var targetElement = _tabManager.CurrentTab?.ViewElement;
            if (targetElement != null)
                flyout.ShowAt(targetElement, new Point(
                    Math.Max(0, Math.Min(args.Location.X, targetElement.ActualWidth)),
                    Math.Max(0, Math.Min(args.Location.Y, targetElement.ActualHeight))
                ));
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

            // 让 TabManager 更新所有标签的主题
            _tabManager?.UpdateTheme(colors);

            ThemeManager.UpdateTitleBarColors();
            UpdateTabLayout();
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

                // 创建新标签并替换
                IBrowserTab newTab = newEngine == EngineType.WebView2 ? new WebView2Tab() : new EdgeHtmlTab();
                viewItem.Tab = newTab;
                // 由于 TabManager 内部已绑定事件，需要重新绑定
                // 简单做法：移除旧标签，创建新标签
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

        // ---- 更新收藏按钮 ----
        private void UpdateStarButton()
        {
            if (_tabManager.CurrentTab != null)
                toolbarControl.UpdateFavoriteButton(FavoritesManager.Instance.ContainsUrl(_tabManager.CurrentTab.CurrentUrl));
        }

        // ---- 设置变化 ----
        private async void OnSettingChanged(string key, string newValue)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                switch (key)
                {
                    case "AskBeforeDownload":
                        _askBeforeDownload = (newValue == "True" || newValue == "true");
                        break;
                    case "DefaultEngine":
                        _defaultEngine = (newValue == "WebView2") ? EngineType.WebView2 : EngineType.EdgeHtml;
                        break;
                    case "DownloadFolder":
                        DownloadManager.SetCustomDownloadPath(newValue);
                        break;
                    case "DoNotTrack":
                        // 应用到所有现有标签（由 TabManager 处理）
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
            try
            {
                if (File.Exists(item.FullPath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                    await file.DeleteAsync();
                }
            }
            catch { }
            await DownloadViaHiddenWebViewAsync(item.Url);
        }

        private async void OnEdgeHtmlDownloadRequested(string url)
        {
            string defaultFileName = "download";
            try
            {
                string name = Path.GetFileName(new Uri(url).AbsolutePath);
                if (!string.IsNullOrWhiteSpace(name))
                    defaultFileName = name;
            }
            catch { }

            if (_askBeforeDownload)
            {
                StorageFolder defaultFolder = null;
                try { defaultFolder = await DownloadManager.GetDownloadFolderAsync(); } catch { }

                var dialog = new DownloadDialog(defaultFileName, url, defaultFolder, null);
                var result = await dialog.ShowAsync();

                if (result != ContentDialogResult.Primary)
                    return;

                if (!DownloadDialog.TryValidateFileName(dialog.FileName, out string safeName, out string error))
                {
                    await new ContentDialog { Title = "文件名无效", Content = error, CloseButtonText = "确定" }.ShowAsync();
                    return;
                }

                StorageFolder selectedFolder = dialog.SelectedFolder ?? defaultFolder;
                string token = Guid.NewGuid().ToString();
                StorageApplicationPermissions.FutureAccessList.AddOrReplace(token, selectedFolder);
                DownloadManager.SetCustomDownloadPath(selectedFolder.Path, token);

                await DownloadViaHiddenWebViewAsync(url, selectedFolder, safeName);
            }
            else
            {
                await DownloadViaHiddenWebViewAsync(url);
            }
        }

        private async Task DownloadViaHiddenWebViewAsync(string url, StorageFolder folder, string fileName)
        {
            string targetPath = Path.Combine(folder.Path, fileName);
            if (File.Exists(targetPath))
            {
                string ext = Path.GetExtension(fileName);
                string name = Path.GetFileNameWithoutExtension(fileName);
                int counter = 1;
                while (File.Exists(targetPath = Path.Combine(folder.Path, $"{name} ({counter++}){ext}"))) ;
            }
            await DownloadViaHiddenWebViewCoreAsync(url, targetPath);
        }

        private async Task DownloadViaHiddenWebViewAsync(string url)
        {
            StorageFolder folder = await DownloadManager.GetDownloadFolderAsync();
            string fileName = "download";
            try
            {
                string name = Path.GetFileName(new Uri(url).AbsolutePath);
                if (!string.IsNullOrWhiteSpace(name)) fileName = name;
            }
            catch { }

            string targetPath = Path.Combine(folder.Path, fileName);
            if (File.Exists(targetPath))
            {
                string ext = Path.GetExtension(fileName);
                string name = Path.GetFileNameWithoutExtension(fileName);
                int counter = 1;
                while (File.Exists(targetPath = Path.Combine(folder.Path, $"{name} ({counter++}){ext}"))) ;
            }
            await DownloadViaHiddenWebViewCoreAsync(url, targetPath);
        }

        private async Task DownloadViaHiddenWebViewCoreAsync(string url, string targetPath)
        {
            var downloadId = Guid.NewGuid().ToString();
            var wv = new WebView2 { Width = 0, Height = 0, Visibility = Visibility.Collapsed };
            _downloadWebViews[downloadId] = wv;
            RootGrid.Children.Add(wv);

            try
            {
                await wv.EnsureCoreWebView2Async();
                wv.CoreWebView2.DownloadStarting += async (s, e) =>
                {
                    e.Handled = true;
                    e.ResultFilePath = targetPath;

                    var item = await DownloadManager.AddAsync(e.DownloadOperation.Uri, targetPath, Path.GetFileName(targetPath));
                    item.WebViewOperation = e.DownloadOperation;
                    item.TotalBytesToReceive = e.DownloadOperation.TotalBytesToReceive;
                    item.SetCleanupAction(() => CleanupDownloadWebView(downloadId));
                    DownloadManager.StartDownloadOperation(item);
                };
                wv.CoreWebView2.Navigate(url);
            }
            catch
            {
                CleanupDownloadWebView(downloadId);
            }
        }

        private void CleanupDownloadWebView(string id)
        {
            if (_downloadWebViews.TryGetValue(id, out var wv))
            {
                _downloadWebViews.Remove(id);
                try { (wv.Parent as Panel)?.Children.Remove(wv); wv.Close(); } catch { }
            }
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
            var menu = new MenuFlyout();
            ApplyThemeToFlyout(menu);

            var engineSub = new MenuFlyoutSubItem { Text = "渲染引擎" };
            var edgeHtmlItem = new MenuFlyoutItem { Text = "EdgeHTML" };
            edgeHtmlItem.Click += (_, _) => ToolbarControl_EngineSwitched(EngineType.EdgeHtml);
            var webView2Item = new MenuFlyoutItem { Text = "WebView2" };
            webView2Item.Click += (_, _) => ToolbarControl_EngineSwitched(EngineType.WebView2);
            engineSub.Items.Add(edgeHtmlItem);
            engineSub.Items.Add(webView2Item);
            menu.Items.Add(engineSub);
            menu.Items.Add(new MenuFlyoutSeparator());

            var newWindowItem = new MenuFlyoutItem { Text = "新建窗口" };
            newWindowItem.Click += async (_, _) => { await _tabManager.CreateTabAsync(_defaultEngine, "about:blank"); await new ContentDialog { Title = "提示", Content = "独立窗口功能暂不可用，已为您在当前窗口打开新标签页。", CloseButtonText = "确定" }.ShowAsync(); };
            menu.Items.Add(newWindowItem);
            menu.Items.Add(new MenuFlyoutSeparator());

            var zoomSub = new MenuFlyoutSubItem { Text = "缩放" };
            var zoomInItem = new MenuFlyoutItem { Text = "放大" }; zoomInItem.Click += (_, _) => AdjustZoom(0.1);
            var zoomOutItem = new MenuFlyoutItem { Text = "缩小" }; zoomOutItem.Click += (_, _) => AdjustZoom(-0.1);
            var zoomResetItem = new MenuFlyoutItem { Text = "重置" }; zoomResetItem.Click += (_, _) => ResetZoom();
            zoomSub.Items.Add(zoomInItem); zoomSub.Items.Add(zoomOutItem); zoomSub.Items.Add(zoomResetItem); menu.Items.Add(zoomSub);

            var printItem = new MenuFlyoutItem { Text = "打印" }; printItem.Click += (_, _) => PrintCurrentPage(); menu.Items.Add(printItem);
            var findItem = new MenuFlyoutItem { Text = "查找" }; findItem.Click += (_, _) => FindOnPage(); menu.Items.Add(findItem);
            var sourceItem = new MenuFlyoutItem { Text = "查看源代码" }; sourceItem.Click += async (_, _) => await ViewSourceAsync(); menu.Items.Add(sourceItem);
            menu.Items.Add(new MenuFlyoutSeparator());
            var toolsSub = new MenuFlyoutSubItem { Text = "更多工具" };
            var devToolsItem = new MenuFlyoutItem { Text = "开发者工具" }; devToolsItem.Click += (_, _) => OpenDevTools(); toolsSub.Items.Add(devToolsItem); menu.Items.Add(toolsSub);
            menu.Items.Add(new MenuFlyoutSeparator());

            var reopenSub = new MenuFlyoutSubItem { Text = "重新打开关闭的标签" };
            if (!_tabManager.TryReopenClosedTab(out _, out _, out _))
            {
                var emptyItem = new MenuFlyoutItem { Text = "没有最近关闭的标签", IsEnabled = false };
                reopenSub.Items.Add(emptyItem);
            }
            else
            {
                var item = new MenuFlyoutItem { Text = "恢复最近关闭的标签" };
                item.Click += async (_, _) =>
                {
                    if (_tabManager.TryReopenClosedTab(out EngineType engine, out string url, out _))
                        await _tabManager.CreateTabAsync(engine, url);
                };
                reopenSub.Items.Add(item);
                var clearItem = new MenuFlyoutItem { Text = "清空列表" };
                clearItem.Click += (_, _) => _tabManager.ClearClosedTabs();
                reopenSub.Items.Add(clearItem);
            }
            menu.Items.Add(reopenSub);

            var bookmarkAllItem = new MenuFlyoutItem { Text = "将所有标签加入收藏" }; bookmarkAllItem.Click += (_, _) => BookmarkAllTabs(); menu.Items.Add(bookmarkAllItem);
            var historyItem = new MenuFlyoutItem { Text = "历史记录" }; historyItem.Click += (_, _) => { HubSplitView.IsPaneOpen = true; hubPane.ShowHistory(); }; menu.Items.Add(historyItem);
            var clearDataItem = new MenuFlyoutItem { Text = "清除浏览数据" }; clearDataItem.Click += async (_, _) => await ClearBrowsingDataAsync(); menu.Items.Add(clearDataItem);
            menu.Items.Add(new MenuFlyoutSeparator());
            var settingsItem = new MenuFlyoutItem { Text = "设置" }; settingsItem.Click += (_, _) => SettingsSplitView.IsPaneOpen = !SettingsSplitView.IsPaneOpen; menu.Items.Add(settingsItem);
            var aboutItem = new MenuFlyoutItem { Text = "关于 Edge Rebuild" }; aboutItem.Click += (_, _) => ShowAboutDialog(); menu.Items.Add(aboutItem);

            toolbarControl.ShowMenu(menu);
        }

        private void ApplyThemeToFlyout(MenuFlyout flyout)
        {
            var style = new Style(typeof(MenuFlyoutPresenter));
            bool isDark = ThemeManager.IsDarkTheme;
            var acrylic = new AcrylicBrush
            {
                BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                TintColor = isDark ? Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xFF, 0xE6, 0xF0, 0xFA),
                TintOpacity = 0.85,
                FallbackColor = isDark ? Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xFF, 0xE6, 0xF0, 0xFA)
            };
            style.Setters.Add(new Setter(MenuFlyoutPresenter.BackgroundProperty, acrylic));
            style.Setters.Add(new Setter(MenuFlyoutPresenter.CornerRadiusProperty, new CornerRadius(0)));
            flyout.MenuFlyoutPresenterStyle = style;
        }

        // ---- 拖拽逻辑 ----
        private void OnTabPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var properties = e.GetCurrentPoint((UIElement)sender).Properties;

            if (properties.IsMiddleButtonPressed)
            {
                var border = sender as Border;
                var viewItem = _tabManager.Tabs.FirstOrDefault(v => v.Container == border);
                if (viewItem != null)
                {
                    _ = _tabManager.CloseTabAsync(viewItem);
                    e.Handled = true;
                }
                return;
            }

            if (properties.IsRightButtonPressed) return;
            ResetDragState();
            if (_tabManager.TabCount <= 1) return;
            var borderLeft = sender as Border;
            if (borderLeft == null) return;
            var viewItemDrag = _tabManager.Tabs.FirstOrDefault(v => v.Container == borderLeft);
            if (viewItemDrag == null) return;

            _dragItem = viewItemDrag;
            _dragOffset = e.GetCurrentPoint(viewItemDrag.Container).Position;
            _hasMoved = false;
            _isDragging = false;
            _totalDx = 0;
            _isDetaching = false;
            _dragStartCanvasPoint = e.GetCurrentPoint(DragCanvas).Position;
            _dragStartScreenPoint = CoreWindow.GetForCurrentThread().PointerPosition;

            var colors = ThemeManager.Colors;
            _dragGhost = new Border
            {
                Width = _dragItem.Container.ActualWidth,
                Height = 32,
                Background = colors.TabActiveBackground,
                BorderBrush = ThemeManager.GetSeparatorBrush(),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = _dragItem.Tab.Title ?? "新标签页",
                    FontSize = 12,
                    Foreground = colors.ForegroundBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0)
                }
            };
            var initialPos = viewItemDrag.Container.TransformToVisual(DragCanvas).TransformPoint(new Point(0, 0));
            Canvas.SetLeft(_dragGhost, initialPos.X);
            Canvas.SetTop(_dragGhost, initialPos.Y);
            DragCanvas.Children.Add(_dragGhost);
            Canvas.SetZIndex(_dragGhost, 999);

            _dragItem.Container.Tag = _dragItem.Container.Child;
            _dragItem.Container.Child = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            _dragItem.Container.Opacity = 1.0;
            var tabBarTransform = TabBarPanel.TransformToVisual(DragCanvas);
            _tabBarPanelOffset = tabBarTransform.TransformPoint(new Point(0, 0));
            _lastReorderTime = DateTime.MinValue;
            borderLeft.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void OnTabPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_dragItem == null || _dragGhost == null) return;
            var posInCanvas = e.GetCurrentPoint(DragCanvas).Position;
            if (!_isDragging && (Math.Abs(posInCanvas.X - _dragStartCanvasPoint.X) > 3 || Math.Abs(posInCanvas.Y - _dragStartCanvasPoint.Y) > 3))
            {
                _hasMoved = true;
                _isDragging = true;
            }
            if (_isDragging)
            {
                double newLeft = posInCanvas.X - _dragOffset.X;
                Canvas.SetLeft(_dragGhost, newLeft);
                Canvas.SetTop(_dragGhost, posInCanvas.Y - _dragOffset.Y);
                _totalDx = posInCanvas.X - _dragStartCanvasPoint.X;
                if (!_isDetaching)
                    UpdateDragSort();
                var tabBarTransform = TabBarPanel.TransformToVisual(DragCanvas);
                var tabBarBounds = new Rect(
                    tabBarTransform.TransformPoint(new Point(0, 0)),
                    tabBarTransform.TransformPoint(new Point(TabBarPanel.ActualWidth, TabBarPanel.ActualHeight))
                );
                double threshold = 20;
                bool outside = posInCanvas.Y < tabBarBounds.Top - threshold || posInCanvas.Y > tabBarBounds.Bottom + threshold;
                if (!_isDetaching && outside)
                {
                    _isDetaching = true;
                    if (_dragGhost != null)
                    {
                        DragCanvas.Children.Remove(_dragGhost);
                        _dragGhost = null;
                    }
                    if (_dragItem != null && _dragItem.Container.Tag is UIElement originalChild)
                    {
                        _dragItem.Container.Child = originalChild;
                        _dragItem.Container.Tag = null;
                        _dragItem.Container.Opacity = 1.0;
                    }
                    MoveTabToNewTab(_dragItem);
                    _dragItem = null;
                    _isDragging = false;
                    _hasMoved = false;
                    _totalDx = 0;
                }
            }
            e.Handled = true;
        }

        private async void MoveTabToNewTab(TabViewItem draggedItem)
        {
            string url = draggedItem.Tab.CurrentUrl;
            EngineType engine = draggedItem.Tab.Engine;
            if (string.IsNullOrWhiteSpace(url) || url == "about:blank")
                url = "https://www.bing.com";
            await _tabManager.CreateTabAsync(engine, url);
            await _tabManager.CloseTabAsync(draggedItem);
        }

        private void UpdateDragSort()
        {
            if (_dragGhost == null) return;
            if ((DateTime.Now - _lastReorderTime).TotalMilliseconds < 200) return;
            double ghostCenterInCanvas = Canvas.GetLeft(_dragGhost) + _dragGhost.Width / 2;
            double ghostLocalX = ghostCenterInCanvas - _tabBarPanelOffset.X;
            int dragIndex = _tabManager.Tabs.ToList().IndexOf(_dragItem);
            double accumulatedWidth = 0;
            int targetIndex = -1;

            for (int i = 0; i < _tabManager.Tabs.Count; i++)
            {
                if (i == dragIndex) continue;
                var item = _tabManager.Tabs[i];
                double containerCenter = accumulatedWidth + item.Container.ActualWidth / 2;
                if (ghostLocalX < containerCenter)
                {
                    targetIndex = i;
                    break;
                }
                accumulatedWidth += item.Container.ActualWidth;
            }

            if (targetIndex == -1)
                targetIndex = _tabManager.Tabs.Count;

            if (targetIndex == dragIndex) return;

            // 重新排序
            var itemToMove = _dragItem;
            var tabsList = _tabManager.Tabs as List<TabViewItem>;
            if (tabsList != null)
            {
                tabsList.RemoveAt(dragIndex);
                int adjustedIndex = (targetIndex > dragIndex) ? targetIndex - 1 : targetIndex;
                tabsList.Insert(adjustedIndex, itemToMove);
                SyncTabPanelOrder();
            }
            _lastReorderTime = DateTime.Now;
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

        private void OnTabPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_dragItem == null) return;
            try
            {
                if (_isDetaching) return;
                if (!_hasMoved)
                {
                    _ = _tabManager.SwitchToTabAsync(_dragItem);
                    return;
                }
                SyncTabPanelOrder();
                UpdateTabLayout();
            }
            finally
            {
                ResetDragState();
                e.Handled = true;
            }
        }

        private void ResetDragState()
        {
            if (_dragGhost != null)
            {
                DragCanvas.Children.Remove(_dragGhost);
                _dragGhost = null;
            }
            if (_dragItem != null)
            {
                if (_dragItem.Container.Tag is UIElement originalChild)
                {
                    _dragItem.Container.Child = originalChild;
                    _dragItem.Container.Tag = null;
                }
                _dragItem.Container.Opacity = 1.0;
            }
            _dragItem = null;
            _isDragging = false;
            _hasMoved = false;
            _totalDx = 0;
            _isDetaching = false;
        }

        private void OnTabPointerCanceled(object sender, PointerRoutedEventArgs e) => ResetDragState();

        // ---- 右键菜单 ----
        private void OnTabRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var border = sender as Border;
            var viewItem = _tabManager.Tabs.FirstOrDefault(v => v.Container == border);
            if (viewItem == null) return;
            var menu = new MenuFlyout();
            ApplyThemeToFlyout(menu);

            var newTabItem = new MenuFlyoutItem { Text = "新建标签页" };
            newTabItem.Click += async (_, _) => await _tabManager.CreateTabAsync(_defaultEngine, "about:blank");
            menu.Items.Add(newTabItem);

            var reloadItem = new MenuFlyoutItem { Text = "重新加载" };
            reloadItem.Click += (_, _) => viewItem.Tab?.RefreshAsync();
            menu.Items.Add(reloadItem);

            var closeItem = new MenuFlyoutItem { Text = "关闭标签页" };
            closeItem.Click += (_, _) => _ = _tabManager.CloseTabAsync(viewItem);
            menu.Items.Add(closeItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var closeOthersItem = new MenuFlyoutItem { Text = "关闭其他标签页" };
            closeOthersItem.Click += (_, _) => CloseOtherTabs(viewItem);
            menu.Items.Add(closeOthersItem);

            var closeRightItem = new MenuFlyoutItem { Text = "关闭右侧标签页" };
            closeRightItem.Click += (_, _) => CloseTabsToRight(viewItem);
            menu.Items.Add(closeRightItem);

            menu.ShowAt(border, e.GetPosition(border));
        }

        private void CloseOtherTabs(TabViewItem keepItem)
        {
            foreach (var item in _tabManager.Tabs.Where(t => t != keepItem).ToList())
                _ = _tabManager.CloseTabAsync(item);
        }

        private void CloseTabsToRight(TabViewItem startItem)
        {
            int index = _tabManager.Tabs.ToList().IndexOf(startItem);
            if (index < 0) return;
            foreach (var item in _tabManager.Tabs.Skip(index + 1).ToList())
                _ = _tabManager.CloseTabAsync(item);
        }

        // ---- 其他功能 ----
        private async void AdjustZoom(double delta)
        {
            _zoomFactor = Math.Max(0.25, Math.Min(5.0, _zoomFactor + delta));
            if (_tabManager.CurrentTab is EdgeHtmlTab edgeTab)
                await edgeTab.ExecuteScriptAsync($"document.body.style.zoom = '{_zoomFactor}';");
            else if (_tabManager.CurrentTab is WebView2Tab wv2)
                await wv2.ExecuteScriptAsync($"document.body.style.zoom = '{_zoomFactor}';");
        }

        private async void ResetZoom()
        {
            _zoomFactor = 1.0;
            if (_tabManager.CurrentTab is EdgeHtmlTab edgeTab)
                await edgeTab.ExecuteScriptAsync("document.body.style.zoom = '1';");
            else if (_tabManager.CurrentTab is WebView2Tab wv2)
                await wv2.ExecuteScriptAsync("document.body.style.zoom = '1';");
        }

        private void PrintCurrentPage()
        {
            if (_tabManager.CurrentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                wv2.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
            else if (_tabManager.CurrentTab is EdgeHtmlTab edgeTab)
                _ = edgeTab.ExecuteScriptAsync("window.print();");
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