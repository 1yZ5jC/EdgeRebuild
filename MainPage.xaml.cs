using EdgeRebuild.Controls;
using EdgeRebuild.Core;
using EdgeRebuild.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Storage;
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

using TabView = Microsoft.UI.Xaml.Controls.TabView;
using TabViewItem = Microsoft.UI.Xaml.Controls.TabViewItem;
using WebView2 = Microsoft.UI.Xaml.Controls.WebView2;

namespace EdgeRebuild
{
    public sealed partial class MainPage : Page
    {
        private readonly List<TabItemInfo> _tabViews = new List<TabItemInfo>();
        private readonly Dictionary<IBrowserTab, TabItemInfo> _tabDataMap = new Dictionary<IBrowserTab, TabItemInfo>();
        private IBrowserTab _currentTab;
        private bool _isLoaded;
        private string _pendingUrl;
        private string _currentSkin = "Spartan";

        private double _zoomFactor = 1.0;
        private readonly Stack<string> _closedTabUrls = new Stack<string>();

        private SolidColorBrush _foregroundBrush;
        private SolidColorBrush _mutedForegroundBrush;

        private WebView2 _stealthDownloader;
        private bool _askBeforeDownload;
        private EngineType _defaultEngine = EngineType.EdgeHtml;

        private readonly Dictionary<IBrowserTab, DispatcherTimer> _suspendTimers = new Dictionary<IBrowserTab, DispatcherTimer>();
        private const int SuspendDelaySeconds = 120;
        private bool _enableTabSuspend = true;

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;
            _foregroundBrush = new SolidColorBrush(Colors.Black);
            _mutedForegroundBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x66, 0x66, 0x66));
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            CleanupStaticEvents();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CleanupStaticEvents();
            DestroyStealthDownloader();
        }

        private void CleanupStaticEvents()
        {
            DownloadManager.DownloadProgressChanged -= OnDownloadProgress;
            DownloadManager.DownloadStatusChanged -= OnDownloadStatusChanged;
            DownloadManager.RetryDownloadRequested -= OnRetryDownloadRequested;
            EdgeHtmlTab.DownloadRequested -= OnEdgeHtmlDownloadRequested;
            WebView2Tab.DownloadRequested -= OnWebView2DownloadRequested;
            SettingsManager.SettingChanged -= OnSettingChanged;

            foreach (var timer in _suspendTimers.Values)
                timer.Stop();
            _suspendTimers.Clear();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            try
            {
                CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
                Window.Current.SetTitleBar(TitleBarDragArea);
                var titleBar = ApplicationView.GetForCurrentView().TitleBar;
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                titleBar.ButtonForegroundColor = Colors.Black;

                string savedSkin = await SettingsManager.GetAsync("Skin") ?? "Spartan";
                _currentSkin = savedSkin;
                ApplySkinColors(savedSkin);

                string askSetting = await SettingsManager.GetAsync("AskBeforeDownload");
                _askBeforeDownload = (askSetting == "True" || askSetting == "true");

                string engineSetting = await SettingsManager.GetAsync("DefaultEngine");
                _defaultEngine = (engineSetting == "WebView2") ? EngineType.WebView2 : EngineType.EdgeHtml;

                string suspendSetting = await SettingsManager.GetAsync("EnableTabSuspend") ?? "True";
                _enableTabSuspend = (suspendSetting == "True" || suspendSetting == "true");

                await DownloadManager.UpdateSystemDownloadFolderAccessAsync();
                await DownloadManager.LoadDownloadsAsync();

                DownloadManager.DownloadProgressChanged += OnDownloadProgress;
                DownloadManager.DownloadStatusChanged += OnDownloadStatusChanged;
                DownloadManager.RetryDownloadRequested += OnRetryDownloadRequested;
                EdgeHtmlTab.DownloadRequested += OnEdgeHtmlDownloadRequested;
                WebView2Tab.DownloadRequested += OnWebView2DownloadRequested;
                SettingsManager.SettingChanged += OnSettingChanged;

                if (_tabViews.Count == 0)
                {
                    if (!string.IsNullOrEmpty(_pendingUrl))
                        await CreateNewTabAsync(_defaultEngine, _pendingUrl);
                    else
                        await CreateNewTabAsync(_defaultEngine, "about:blank");
                }
            }
            catch (Exception ex)
            {
                await new ContentDialog
                {
                    Title = "界面初始化失败",
                    Content = ex.ToString(),
                    CloseButtonText = "确定"
                }.ShowAsync();
            }
        }

        private async void OnSettingChanged(string key, string newValue)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                switch (key)
                {
                    case "Skin":
                        _currentSkin = newValue;
                        ApplySkinColors(_currentSkin);
                        break;
                    case "AskBeforeDownload":
                        _askBeforeDownload = (newValue == "True" || newValue == "true");
                        break;
                    case "DefaultEngine":
                        _defaultEngine = (newValue == "WebView2") ? EngineType.WebView2 : EngineType.EdgeHtml;
                        break;
                    case "EnableTabSuspend":
                        _enableTabSuspend = (newValue == "True" || newValue == "true");
                        if (!_enableTabSuspend)
                        {
                            foreach (var timer in _suspendTimers.Values)
                                timer.Stop();
                            _suspendTimers.Clear();
                        }
                        break;
                }
            });
        }

        // 下载事件处理
        private void OnDownloadProgress(DownloadItem item) => hubPane.UpdateDownloadItem(item);
        private void OnDownloadStatusChanged(DownloadItem item) => hubPane.UpdateDownloadItem(item);

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
            await PrepareStealthDownloaderAsync(item.Url, item.FullPath);
        }

        private async void OnEdgeHtmlDownloadRequested(string url) => await PrepareStealthDownloaderAsync(url);
        private async void OnWebView2DownloadRequested(string url, StorageFolder folder, string fileName)
        {
            await PrepareStealthDownloaderWithPathAsync(url, folder, fileName);
        }

        private Task PrepareStealthDownloaderAsync(string url) => PrepareStealthDownloaderAsync(url, null);

        private async Task PrepareStealthDownloaderAsync(string url, string targetFilePath = null)
        {
            if (_stealthDownloader != null) DestroyStealthDownloader();
            _stealthDownloader = new WebView2 { Width = 0, Height = 0, Visibility = Visibility.Collapsed };
            RootGrid.Children.Add(_stealthDownloader);
            try
            {
                await _stealthDownloader.EnsureCoreWebView2Async();
                _stealthDownloader.CoreWebView2.DownloadStarting += (s, args) =>
                {
                    if (!string.IsNullOrEmpty(targetFilePath))
                    {
                        args.ResultFilePath = targetFilePath;
                        args.Handled = true;
                        var op = args.DownloadOperation;
                        var existing = DownloadManager.Downloads.FirstOrDefault(d => d.FullPath == targetFilePath);
                        if (existing != null)
                        {
                            existing.WebViewOperation = op;
                            existing.TotalBytesToReceive = op.TotalBytesToReceive;
                            existing.SetCleanupAction(() => DestroyStealthDownloader());
                            DownloadManager.StartDownloadOperation(existing);
                        }
                        else
                        {
                            var item = DownloadManager.AddAsync(url, targetFilePath, Path.GetFileName(targetFilePath)).Result;
                            item.WebViewOperation = op;
                            item.TotalBytesToReceive = op.TotalBytesToReceive;
                            item.SetCleanupAction(() => DestroyStealthDownloader());
                            DownloadManager.StartDownloadOperation(item);
                        }
                    }
                    else
                    {
                        OnStealthDownloadStarting(s, args);
                    }
                };
                _stealthDownloader.CoreWebView2.Navigate(url);
            }
            catch { DestroyStealthDownloader(); }
        }

        private async void OnStealthDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs args)
        {
            var op = args.DownloadOperation;
            string url = op.Uri ?? "";
            string fileName = Path.GetFileName(op.ResultFilePath);

            if (_askBeforeDownload)
            {
                args.Cancel = true; args.Handled = true; DestroyStealthDownloader();
                StorageFolder defaultFolder = null;
                try { defaultFolder = await DownloadManager.GetDownloadFolderAsync(); } catch { }
                var dialog = new DownloadDialog(fileName, url, defaultFolder);
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    if (dialog.SelectedFolder != null && !string.IsNullOrWhiteSpace(dialog.FileName))
                        await PrepareStealthDownloaderWithPathAsync(url, dialog.SelectedFolder, dialog.FileName);
                }
                return;
            }

            var existing = DownloadManager.FindCompletedByFileNameSync(fileName, url);
            if (existing != null)
            {
                args.Cancel = true; args.Handled = true; DestroyStealthDownloader();
                try { await Windows.System.Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(existing.FullPath)); } catch { }
                return;
            }

            args.Handled = true;
            string edgeFolder = DownloadManager.SystemDownloadFolderPath ?? Path.Combine(ApplicationData.Current.LocalFolder.Path, "Downloads");
            string filePath = Path.Combine(edgeFolder, fileName);
            if (File.Exists(filePath))
            {
                string ext = Path.GetExtension(fileName), name = Path.GetFileNameWithoutExtension(fileName);
                int counter = 1;
                while (File.Exists(filePath = Path.Combine(edgeFolder, $"{name} ({counter++}){ext}"))) ;
            }
            args.ResultFilePath = filePath;
            var item = await DownloadManager.AddAsync(url, filePath, Path.GetFileName(filePath));
            item.WebViewOperation = op; item.TotalBytesToReceive = op.TotalBytesToReceive;
            item.SetCleanupAction(() => DestroyStealthDownloader());
            DownloadManager.StartDownloadOperation(item);
        }

        private async Task PrepareStealthDownloaderWithPathAsync(string url, StorageFolder folder, string fileName)
        {
            if (_stealthDownloader != null) DestroyStealthDownloader();
            _stealthDownloader = new WebView2 { Width = 0, Height = 0, Visibility = Visibility.Collapsed };
            RootGrid.Children.Add(_stealthDownloader);
            try
            {
                await _stealthDownloader.EnsureCoreWebView2Async();
                _stealthDownloader.CoreWebView2.DownloadStarting += async (_, args) =>
                {
                    var op = args.DownloadOperation;
                    string path = Path.Combine(folder.Path, fileName);
                    if (File.Exists(path)) { string ext = Path.GetExtension(fileName); string name = Path.GetFileNameWithoutExtension(fileName); int counter = 1; while (File.Exists(path = Path.Combine(folder.Path, $"{name} ({counter++}){ext}"))) ; }
                    args.ResultFilePath = path; args.Handled = true;
                    var item = await DownloadManager.AddAsync(url, path, Path.GetFileName(path));
                    item.WebViewOperation = op; item.TotalBytesToReceive = op.TotalBytesToReceive;
                    item.SetCleanupAction(() => DestroyStealthDownloader());
                    DownloadManager.StartDownloadOperation(item);
                };
                _stealthDownloader.CoreWebView2.Navigate(url);
            }
            catch { DestroyStealthDownloader(); }
        }

        private void DestroyStealthDownloader()
        {
            if (_stealthDownloader != null)
            {
                try { if (_stealthDownloader.CoreWebView2 != null) _stealthDownloader.CoreWebView2.DownloadStarting -= OnStealthDownloadStarting; if (_stealthDownloader.Parent is Panel p) p.Children.Remove(_stealthDownloader); _stealthDownloader.Close(); } catch { }
                _stealthDownloader = null;
            }
        }

        // 皮肤
        private async void SwitchSkin(string name) { _currentSkin = name; ApplySkinColors(name); await SettingsManager.SetAsync("Skin", name); }
        private void ApplySkinColors(string skinName)
        {
            var colors = SkinManager.GetSkinColors(skinName);
            toolbarControl.ApplySkin(colors);
            HubSplitView.PaneBackground = colors.ToolbarBackground;
            SettingsSplitView.PaneBackground = colors.ToolbarBackground;
            MainTabView.Background = colors.ToolbarBackground;
            _foregroundBrush = colors.ForegroundBrush;
            _mutedForegroundBrush = colors.MutedForegroundBrush;
            hubPane.ForegroundBrush = _foregroundBrush;
            hubPane.MutedForegroundBrush = _mutedForegroundBrush;
        }

        // Toolbar 事件
        private async void ToolbarControl_UrlSubmitted(string url) => await _currentTab?.NavigateAsync(url);
        private async void ToolbarControl_BackRequested() { if (_currentTab?.CanGoBack == true) await _currentTab.GoBackAsync(); }
        private async void ToolbarControl_ForwardRequested() { if (_currentTab?.CanGoForward == true) await _currentTab.GoForwardAsync(); }
        private async void ToolbarControl_RefreshRequested() { if (_currentTab != null) await _currentTab.RefreshAsync(); }

        private void ToolbarControl_AddFavoriteClicked()
        {
            if (_currentTab == null || string.IsNullOrEmpty(_currentTab.CurrentUrl)) return;
            string url = _currentTab.CurrentUrl;
            if (FavoritesManager.Instance.ContainsUrl(url)) { var item = FavoritesManager.Instance.Favorites.FirstOrDefault(f => f.Url == url); if (item != null) FavoritesManager.Instance.Remove(item); }
            else FavoritesManager.Instance.Add(!string.IsNullOrEmpty(_currentTab.Title) ? _currentTab.Title : url, url);
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
            var engineSub = new MenuFlyoutSubItem { Text = "渲染引擎" };
            var edgeHtmlItem = new MenuFlyoutItem { Text = "EdgeHTML" }; edgeHtmlItem.Click += async (_, _) => await SwitchCurrentTabEngine(EngineType.EdgeHtml);
            var webView2Item = new MenuFlyoutItem { Text = "WebView2" }; webView2Item.Click += async (_, _) => await SwitchCurrentTabEngine(EngineType.WebView2);
            engineSub.Items.Add(edgeHtmlItem); engineSub.Items.Add(webView2Item); menu.Items.Add(engineSub);
            menu.Items.Add(new MenuFlyoutSeparator());
            var zoomSub = new MenuFlyoutSubItem { Text = "缩放" };
            var zoomInItem = new MenuFlyoutItem { Text = "放大" }; zoomInItem.Click += (_, _) => AdjustZoom(0.1);
            var zoomOutItem = new MenuFlyoutItem { Text = "缩小" }; zoomOutItem.Click += (_, _) => AdjustZoom(-0.1);
            var zoomResetItem = new MenuFlyoutItem { Text = "重置" }; zoomResetItem.Click += (_, _) => ResetZoom();
            zoomSub.Items.Add(zoomInItem); zoomSub.Items.Add(zoomOutItem); zoomSub.Items.Add(zoomResetItem);
            menu.Items.Add(zoomSub);
            var printItem = new MenuFlyoutItem { Text = "打印" }; printItem.Click += (_, _) => PrintCurrentPage(); menu.Items.Add(printItem);
            var findItem = new MenuFlyoutItem { Text = "查找" }; findItem.Click += (_, _) => FindOnPage(); menu.Items.Add(findItem);
            var sourceItem = new MenuFlyoutItem { Text = "查看源代码" }; sourceItem.Click += async (_, _) => await ViewSourceAsync(); menu.Items.Add(sourceItem);
            menu.Items.Add(new MenuFlyoutSeparator());
            var toolsSub = new MenuFlyoutSubItem { Text = "更多工具" };
            var devToolsItem = new MenuFlyoutItem { Text = "开发者工具" }; devToolsItem.Click += (_, _) => OpenDevTools();
            toolsSub.Items.Add(devToolsItem); menu.Items.Add(toolsSub);
            menu.Items.Add(new MenuFlyoutSeparator());
            var reopenItem = new MenuFlyoutItem { Text = "重新打开关闭的标签" }; reopenItem.Click += async (_, _) => await ReopenClosedTabAsync(); menu.Items.Add(reopenItem);
            var bookmarkAllItem = new MenuFlyoutItem { Text = "将所有标签加入收藏" }; bookmarkAllItem.Click += (_, _) => BookmarkAllTabs(); menu.Items.Add(bookmarkAllItem);
            var historyItem = new MenuFlyoutItem { Text = "历史记录" };
            historyItem.Click += (_, _) => { HubSplitView.IsPaneOpen = true; hubPane.ShowHistory(); };
            menu.Items.Add(historyItem);
            var clearDataItem = new MenuFlyoutItem { Text = "清除浏览数据" }; clearDataItem.Click += async (_, _) => await ClearBrowsingDataAsync(); menu.Items.Add(clearDataItem);
            menu.Items.Add(new MenuFlyoutSeparator());
            var settingsItem = new MenuFlyoutItem { Text = "设置" }; settingsItem.Click += (_, _) => SettingsSplitView.IsPaneOpen = !SettingsSplitView.IsPaneOpen; menu.Items.Add(settingsItem);
            var aboutItem = new MenuFlyoutItem { Text = "关于 Edge Rebuild" }; aboutItem.Click += (_, _) => ShowAboutDialog(); menu.Items.Add(aboutItem);
            toolbarControl.ShowMenu(menu);
        }

        private async void ToolbarControl_EngineSwitched(EngineType newEngine)
        {
            if (_currentTab == null || _currentTab.Engine == newEngine) return;
            var tabInfo = _tabDataMap[_currentTab];
            string currentUrl = _currentTab.CurrentUrl;
            _currentTab.Dispose();
            IBrowserTab newTab = newEngine == EngineType.WebView2 ? new WebView2Tab() : new EdgeHtmlTab();
            tabInfo.Tab = newTab;
            _tabDataMap.Remove(_currentTab);
            _tabDataMap[newTab] = tabInfo;
            _currentTab = null;
            BindTabEvents(tabInfo, newTab);
            await SwitchToTabAsync(tabInfo);
            if (!string.IsNullOrEmpty(currentUrl)) await newTab.NavigateAsync(currentUrl);
        }

        // 标签事件绑定
        private void BindTabEvents(TabItemInfo tabInfo, IBrowserTab tab)
        {
            tab.TitleChanged += (title) => _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                UpdateTabHeader(tabInfo);
            });
            tab.UrlChanged += (u) =>
            {
                if (_currentTab == tab)
                {
                    toolbarControl.UpdateNavState(tab.CanGoBack, tab.CanGoForward, u);
                    UpdateStarButton();
                }
                if (!string.IsNullOrEmpty(u) && u != "about:blank") HistoryManager.Add(tab.Title ?? u, u);
            };
            tab.CanGoBackChanged += (can) => { if (_currentTab == tab) toolbarControl.UpdateNavState(can, tab.CanGoForward, tab.CurrentUrl); };
            tab.CanGoForwardChanged += (can) => { if (_currentTab == tab) toolbarControl.UpdateNavState(tab.CanGoBack, can, tab.CurrentUrl); };
            tab.FaviconChanged += (faviconUrl) => { };
            tab.ContextMenuRequested += OnTabContextMenuRequested;
        }

        private void UpdateTabHeader(TabItemInfo tabInfo)
        {
            if (tabInfo.TabViewItem == null) return;
            string title = tabInfo.Tab?.Title ?? "新标签页";
            if (string.IsNullOrEmpty(title)) title = "新标签页";
            string prefix = "";
            if (tabInfo.Tab is WebView2Tab) prefix = "W ";
            else if (tabInfo.Tab is EdgeHtmlTab) prefix = "E ";
            if (tabInfo.IsSuspended) prefix += "[已挂起] ";
            tabInfo.TabViewItem.Header = prefix + title;
        }

        private void UpdateTabSuspendedVisual(TabItemInfo tabInfo, bool suspended)
        {
            tabInfo.IsSuspended = suspended;
            UpdateTabHeader(tabInfo);
        }

        private bool HasActiveDownload(IBrowserTab tab)
        {
            return DownloadManager.Downloads.Any(d => d.Status == "下载中" || d.Status == "已暂停");
        }

        // 标签切换（含挂起）
        private async Task SwitchToTabAsync(TabItemInfo tabInfo)
        {
            if (!_isLoaded || _currentTab == tabInfo.Tab) return;

            if (_enableTabSuspend && _currentTab != null && _suspendTimers.ContainsKey(_currentTab))
            {
                _suspendTimers[_currentTab].Stop();
                _suspendTimers.Remove(_currentTab);
            }

            ContentContainer.Child = null;
            var previousTab = _currentTab;
            _currentTab = tabInfo.Tab;

            if (_currentTab is WebView2Tab wv2 && wv2.IsSuspended)
            {
                await wv2.ResumeAsync();
                UpdateTabSuspendedVisual(tabInfo, false);
            }
            else if (_currentTab is EdgeHtmlTab edgeTab && edgeTab.IsSuspended)
            {
                await edgeTab.ResumeAsync();
                UpdateTabSuspendedVisual(tabInfo, false);
            }

            ContentContainer.Child = _currentTab.ViewElement;
            if (_currentTab is WebView2Tab wv2Tab) await wv2Tab.EnsureInitializedAsync();

            toolbarControl.SetEngine(_currentTab.Engine);
            toolbarControl.UpdateNavState(_currentTab.CanGoBack, _currentTab.CanGoForward, _currentTab.CurrentUrl);
            UpdateStarButton();

            // 安全设置 SelectedItem，忽略 LeftRadiusRender COMException
            try
            {
                MainTabView.SelectedItem = tabInfo.TabViewItem;
            }
            catch (COMException ex) when (ex.HResult == -2147012608) // 0x800F1000
            {
                System.Diagnostics.Debug.WriteLine($"TabView selection COMException ignored: {ex.Message}");
            }

            if (_enableTabSuspend && previousTab != null && previousTab != _currentTab)
            {
                if (_suspendTimers.ContainsKey(previousTab))
                {
                    _suspendTimers[previousTab].Stop();
                    _suspendTimers.Remove(previousTab);
                }

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SuspendDelaySeconds) };
                timer.Tick += async (s, e) =>
                {
                    timer.Stop();
                    var tab = previousTab;
                    if (tab == _currentTab || tab.IsSuspended) return;
                    if (HasActiveDownload(tab)) return;

                    if (tab is WebView2Tab wv2s)
                    {
                        await wv2s.SuspendAsync();
                        if (_tabDataMap.TryGetValue(tab, out var prevTabInfo))
                            UpdateTabSuspendedVisual(prevTabInfo, true);
                    }
                    else if (tab is EdgeHtmlTab edgeS)
                    {
                        await edgeS.SuspendAsync();
                        if (_tabDataMap.TryGetValue(tab, out var prevTabInfo))
                            UpdateTabSuspendedVisual(prevTabInfo, true);
                    }
                    _suspendTimers.Remove(tab);
                };
                _suspendTimers[previousTab] = timer;
                timer.Start();
            }
        }

        private void UpdateStarButton() { if (_currentTab != null) toolbarControl.UpdateFavoriteButton(FavoritesManager.Instance.ContainsUrl(_currentTab.CurrentUrl)); }

        // TabView 事件处理
        private async void TabView_AddTabButtonClick(TabView sender, object args)
        {
            await CreateNewTabAsync(_defaultEngine, "about:blank");
        }

        private void TabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            var tabViewItem = args.Tab;
            var tabInfo = tabViewItem?.Tag as TabItemInfo;
            if (tabInfo != null)
            {
                CloseTabInternal(tabInfo);
            }
        }

        private void TabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var newItem = e.AddedItems.FirstOrDefault() as TabViewItem;
            if (newItem != null)
            {
                var tabInfo = newItem.Tag as TabItemInfo;
                if (tabInfo != null && tabInfo.Tab != _currentTab)
                {
                    _ = SwitchToTabAsync(tabInfo);
                }
            }
        }

        // 创建标签页
        private async Task CreateNewTabAsync(EngineType engine, string url = null)
        {
            if (!_isLoaded) return;
            IBrowserTab tab = engine == EngineType.WebView2 ? new WebView2Tab() : new EdgeHtmlTab();
            var tabInfo = new TabItemInfo
            {
                Tab = tab,
                IsSuspended = false,
                EngineMark = new TextBlock { Text = engine == EngineType.EdgeHtml ? "E" : "W" }
            };

            var tabViewItem = new TabViewItem
            {
                Content = tab.ViewElement,
                Header = "新标签页",
                Tag = tabInfo,
                CornerRadius = new CornerRadius(0) // 显式设置圆角为0
            };
            tabInfo.TabViewItem = tabViewItem;

            MainTabView.TabItems.Add(tabViewItem);
            _tabViews.Add(tabInfo);
            _tabDataMap[tab] = tabInfo;

            BindTabEvents(tabInfo, tab);

            // 安全设置 SelectedItem
            try
            {
                MainTabView.SelectedItem = tabViewItem;
            }
            catch (COMException ex) when (ex.HResult == -2147012608)
            {
                System.Diagnostics.Debug.WriteLine($"TabView selection COMException ignored: {ex.Message}");
            }

            await SwitchToTabAsync(tabInfo);

            if (!string.IsNullOrEmpty(url))
                await tab.NavigateAsync(url);
        }

        private void CloseTabInternal(TabItemInfo tabInfo)
        {
            var tab = tabInfo.Tab;
            if (tab == null) return;

            var tabViewItem = tabInfo.TabViewItem;
            if (tabViewItem != null && MainTabView.TabItems.Contains(tabViewItem))
                MainTabView.TabItems.Remove(tabViewItem);

            _tabViews.Remove(tabInfo);
            _tabDataMap.Remove(tab);

            if (_currentTab == tab)
            {
                _currentTab = null;
                if (_tabViews.Count > 0)
                {
                    var nextInfo = _tabViews.Last();
                    _ = SwitchToTabAsync(nextInfo);
                }
            }

            if (_suspendTimers.ContainsKey(tab))
            {
                _suspendTimers[tab].Stop();
                _suspendTimers.Remove(tab);
            }
            tab.Dispose();

            if (_tabViews.Count == 0)
                _ = CreateNewTabAsync(_defaultEngine, "about:blank");
        }

        private async Task SwitchCurrentTabEngine(EngineType newEngine)
        {
            if (_currentTab == null || _currentTab.Engine == newEngine) return;
            var tabInfo = _tabDataMap[_currentTab];
            string currentUrl = _currentTab.CurrentUrl;
            _currentTab.Dispose();
            IBrowserTab newTab = newEngine == EngineType.WebView2 ? new WebView2Tab() : new EdgeHtmlTab();
            tabInfo.Tab = newTab;
            _tabDataMap.Remove(_currentTab);
            _tabDataMap[newTab] = tabInfo;
            _currentTab = null;
            BindTabEvents(tabInfo, newTab);
            await SwitchToTabAsync(tabInfo);
            if (!string.IsNullOrEmpty(currentUrl)) await newTab.NavigateAsync(currentUrl);
        }

        // 缩放、打印等辅助功能
        private async void AdjustZoom(double delta) { _zoomFactor = Math.Max(0.25, Math.Min(5.0, _zoomFactor + delta)); if (_currentTab is EdgeHtmlTab edgeTab) await edgeTab.ExecuteScriptAsync($"document.body.style.zoom = '{_zoomFactor}';"); else if (_currentTab is WebView2Tab wv2) await wv2.ExecuteScriptAsync($"document.body.style.zoom = '{_zoomFactor}';"); }
        private async void ResetZoom() { _zoomFactor = 1.0; if (_currentTab is EdgeHtmlTab edgeTab) await edgeTab.ExecuteScriptAsync("document.body.style.zoom = '1';"); else if (_currentTab is WebView2Tab wv2) await wv2.ExecuteScriptAsync("document.body.style.zoom = '1';"); }
        private void PrintCurrentPage() { if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null) wv2.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); else if (_currentTab is EdgeHtmlTab edgeTab) _ = edgeTab.ExecuteScriptAsync("window.print();"); }
        private void FindOnPage() { if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null) _ = wv2.CoreWebView2.ExecuteScriptAsync("window.find('');"); else ShowNotImplementedDialog("查找（请切换到 WebView2）"); }
        private async Task ViewSourceAsync()
        {
            if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
            {
                try
                {
                    var html = await wv2.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML;");
                    html = html?.Trim('"').Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t");
                    await new ContentDialog { Title = "页面源代码", Content = new ScrollViewer { Content = new TextBlock { Text = html, FontSize = 10, IsTextSelectionEnabled = true } }, PrimaryButtonText = "关闭" }.ShowAsync();
                }
                catch (Exception ex) { await new ContentDialog { Title = "错误", Content = $"获取源代码失败：{ex.Message}", CloseButtonText = "确定" }.ShowAsync(); }
            }
            else ShowNotImplementedDialog("查看源代码（请切换到 WebView2）");
        }
        private void OpenDevTools() { if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null) wv2.CoreWebView2.OpenDevToolsWindow(); else ShowNotImplementedDialog("开发者工具（请切换到 WebView2）"); }
        private async Task ReopenClosedTabAsync() { if (_closedTabUrls.Count > 0) await CreateNewTabAsync(_defaultEngine, _closedTabUrls.Pop()); else await new ContentDialog { Title = "提示", Content = "没有可恢复的标签页。", CloseButtonText = "确定" }.ShowAsync(); }
        private void BookmarkAllTabs()
        {
            int count = 0;
            foreach (var info in _tabViews)
            {
                var tab = info.Tab;
                if (tab == null) continue;
                string url = tab.CurrentUrl;
                if (!string.IsNullOrEmpty(url) && url != "about:blank" && !FavoritesManager.Instance.ContainsUrl(url))
                {
                    FavoritesManager.Instance.Add(string.IsNullOrEmpty(tab.Title) ? url : tab.Title, url);
                    count++;
                }
            }
            UpdateStarButton();
            if (HubSplitView.IsPaneOpen) hubPane.RefreshFavorites();
            _ = new ContentDialog { Title = "完成", Content = $"已将 {count} 个标签页加入收藏。", CloseButtonText = "确定" }.ShowAsync();
        }
        private async Task ClearBrowsingDataAsync()
        {
            HistoryManager.Clear(); DownloadManager.ClearCompleted();
            if (HubSplitView.IsPaneOpen) hubPane.RefreshAll();
            if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null) wv2.CoreWebView2.Profile.CookieManager.DeleteAllCookies();
            await new ContentDialog { Title = "已清除", Content = "浏览数据已清除。", CloseButtonText = "确定" }.ShowAsync();
        }
        private void ShowAboutDialog() => _ = new ContentDialog { Title = "Edge Rebuild", Content = "版本 0.2 Alpha\n基于 UWP 的双内核浏览器外壳。", CloseButtonText = "确定" }.ShowAsync();
        private async void ShowNotImplementedDialog(string feature) => await new ContentDialog { Title = "即将推出", Content = $"功能“{feature}”尚未实现。", CloseButtonText = "确定" }.ShowAsync();

        // 网页右键菜单
        private void OnTabContextMenuRequested(TabContextMenuEventArgs args)
        {
            if (_currentTab is EdgeHtmlTab) return;
            var flyout = new MenuFlyout();
            var copyItem = new MenuFlyoutItem { Text = "复制", IsEnabled = args.HasSelection };
            copyItem.Click += async (_, _) =>
            {
                if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                {
                    string selectedText = await wv2.CoreWebView2.ExecuteScriptAsync("window.getSelection().toString();");
                    if (!string.IsNullOrEmpty(selectedText))
                    {
                        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage(); dp.SetText(selectedText);
                        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                    }
                }
            };
            flyout.Items.Add(copyItem);
            var pasteItem = new MenuFlyoutItem { Text = "粘贴", IsEnabled = args.IsEditable };
            pasteItem.Click += async (_, _) => await new ContentDialog { Title = "粘贴", Content = "请使用键盘快捷键 Ctrl+V 进行粘贴。", CloseButtonText = "确定" }.ShowAsync();
            flyout.Items.Add(pasteItem);
            var selectAllItem = new MenuFlyoutItem { Text = "全选" };
            selectAllItem.Click += async (_, _) => { if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null) await wv2.CoreWebView2.ExecuteScriptAsync("document.execCommand('selectAll');"); };
            flyout.Items.Add(selectAllItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            var backItem = new MenuFlyoutItem { Text = "后退", IsEnabled = args.CanGoBack }; backItem.Click += (_, _) => _currentTab?.GoBackAsync(); flyout.Items.Add(backItem);
            var forwardItem = new MenuFlyoutItem { Text = "前进", IsEnabled = args.CanGoForward }; forwardItem.Click += (_, _) => _currentTab?.GoForwardAsync(); flyout.Items.Add(forwardItem);
            var refreshItem = new MenuFlyoutItem { Text = "刷新" }; refreshItem.Click += (_, _) => _currentTab?.RefreshAsync(); flyout.Items.Add(refreshItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            if (args.MenuType == ContextMenuType.Link && !string.IsNullOrEmpty(args.LinkUrl))
            {
                var openInNewTabItem = new MenuFlyoutItem { Text = "在新标签页中打开" };
                openInNewTabItem.Click += async (_, _) => await CreateNewTabAsync(_currentTab?.Engine ?? _defaultEngine, args.LinkUrl); flyout.Items.Add(openInNewTabItem);
                var copyLinkItem = new MenuFlyoutItem { Text = "复制链接" }; copyLinkItem.Click += (_, _) => { var dp = new Windows.ApplicationModel.DataTransfer.DataPackage(); dp.SetText(args.LinkUrl); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp); }; flyout.Items.Add(copyLinkItem);
            }
            if (args.MenuType == ContextMenuType.Image && !string.IsNullOrEmpty(args.ImageUrl))
            {
                var copyImageUrlItem = new MenuFlyoutItem { Text = "复制图片地址" }; copyImageUrlItem.Click += (_, _) => { var dp = new Windows.ApplicationModel.DataTransfer.DataPackage(); dp.SetText(args.ImageUrl); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp); }; flyout.Items.Add(copyImageUrlItem);
            }
            flyout.Items.Add(new MenuFlyoutSeparator());
            var printItem = new MenuFlyoutItem { Text = "打印" }; printItem.Click += (_, _) => { if (_currentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null) wv2.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); }; flyout.Items.Add(printItem);
            var sourceItem = new MenuFlyoutItem { Text = "查看页面源代码" }; sourceItem.Click += async (_, _) => await ViewSourceAsync(); flyout.Items.Add(sourceItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            if (_currentTab is WebView2Tab wv2Tab && wv2Tab.CoreWebView2 != null) { var inspectItem = new MenuFlyoutItem { Text = "检查元素 (F12)" }; inspectItem.Click += (_, _) => wv2Tab.CoreWebView2.OpenDevToolsWindow(); flyout.Items.Add(inspectItem); }
            var targetElement = _currentTab?.ViewElement;
            if (targetElement != null) flyout.ShowAt(targetElement, new Point(Math.Max(0, Math.Min(args.Location.X, targetElement.ActualWidth)), Math.Max(0, Math.Min(args.Location.Y, targetElement.ActualHeight))));
        }

        // HubPane 事件
        private void HubPane_NavigateRequested(string url)
        {
            _currentTab?.NavigateAsync(url);
            HubSplitView.IsPaneOpen = false;
        }

        // 设置面板关闭
        private void SettingsPane_CloseRequested(object sender, EventArgs e) => SettingsSplitView.IsPaneOpen = false;
    }
}