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
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

using TabViewItem = EdgeRebuild.Core.TabViewItem;
using WebView2 = Microsoft.UI.Xaml.Controls.WebView2;

namespace EdgeRebuild
{
    public sealed partial class MainPage : Page
    {
        private readonly List<TabViewItem> _tabViews = new List<TabViewItem>();
        private IBrowserTab _currentTab;
        private bool _isLoaded;
        private string _pendingUrl;
        private string _currentSkin = "Spartan";

        private double _zoomFactor = 1.0;
        private readonly Stack<string> _closedTabUrls = new Stack<string>();

        private readonly SolidColorBrush _selectedBrush = new SolidColorBrush(Colors.White);
        private readonly SolidColorBrush _unselectedBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
        private readonly SolidColorBrush _hoverBrush = new SolidColorBrush(Colors.Silver);
        private readonly SolidColorBrush _edgeBlueBrush = new SolidColorBrush(Colors.DodgerBlue);
        private readonly SolidColorBrush _webGreenBrush = new SolidColorBrush(Colors.MediumSeaGreen);

        private SolidColorBrush _foregroundBrush;
        private SolidColorBrush _mutedForegroundBrush;

        private const int MinTabWidth = 100;
        private const int MaxTabWidth = 160;
        private const int AdditionalMargin = 30;
        private const int MinDragWidth = 20;
        private const int ButtonBaseOffset = 50;
        private double _rightReserved;

        // 拖拽相关字段
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

        private WebView2 _stealthDownloader;
        private bool _askBeforeDownload;
        private EngineType _defaultEngine = EngineType.EdgeHtml;

        private readonly Dictionary<IBrowserTab, DispatcherTimer> _suspendTimers = new Dictionary<IBrowserTab, DispatcherTimer>();
        private const int SuspendDelaySeconds = 120;
        private bool _enableTabSuspend = true;

        private UISettings _uiSettings;

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
            if (_uiSettings != null)
            {
                _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
                _uiSettings = null;
            }
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
                UpdateTitleBarButtonColors(); // 动态设置按钮前景色

                SetSafeZonePadding();
                UpdateTabLayout();
                Window.Current.SizeChanged += OnWindowSizeChanged;

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

                _uiSettings = new UISettings();
                _uiSettings.ColorValuesChanged += OnColorValuesChanged;

                if (_tabViews.Count == 0)
                {
                    if (!string.IsNullOrEmpty(_pendingUrl))
                    {
                        await CreateNewTabAsync(_defaultEngine, _pendingUrl);
                        _pendingUrl = null;
                    }
                    else
                    {
                        await CreateNewTabAsync(_defaultEngine, "about:blank");
                    }
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

        private async void OnColorValuesChanged(UISettings sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                UpdateTitleBarButtonColors(); // 确保主题变化时按钮颜色跟随
                if (_currentSkin == SkinManager.SkinSpartan)
                {
                    ApplySkinColors(_currentSkin);
                }
            });
        }

        private void UpdateTitleBarButtonColors()
        {
            var titleBar = ApplicationView.GetForCurrentView().TitleBar;
            titleBar.ButtonForegroundColor = Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? Colors.White
                : Colors.Black;
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

        private void ApplySkinColors(string skinName)
        {
            var colors = SkinManager.GetSkinColors(skinName);
            TabBarBorder.Background = colors.ToolbarBackground;
            toolbarControl.ApplySkin(colors);
            HubSplitView.PaneBackground = colors.ToolbarBackground;
            SettingsSplitView.PaneBackground = colors.ToolbarBackground;

            _selectedBrush.Color = colors.TabActiveBackground.Color;
            _unselectedBrush.Color = colors.TabInactiveBackground.Color;
            _hoverBrush.Color = colors.TabHoverBackground.Color;
            _foregroundBrush = colors.ForegroundBrush;
            _mutedForegroundBrush = colors.MutedForegroundBrush;

            // 传递颜色给子控件
            bool isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
            hubPane.ApplySkinColors(colors.ForegroundBrush as SolidColorBrush, colors.MutedForegroundBrush as SolidColorBrush, isDark);
            SettingsPaneControl.ApplySkinColors(colors.ToolbarBackground);

            foreach (var item in _tabViews)
            {
                item.Container.Background = (item.Tab == _currentTab) ? _selectedBrush : _unselectedBrush;
                item.TitleText.Foreground = _foregroundBrush;
                item.CloseButton.Foreground = _mutedForegroundBrush;
            }
        }

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
            var viewItem = _tabViews.FirstOrDefault(v => v.Tab == _currentTab);
            if (viewItem == null) return;
            string currentUrl = _currentTab.CurrentUrl;
            _currentTab.Dispose();
            IBrowserTab newTab = newEngine == EngineType.WebView2 ? new WebView2Tab() : new EdgeHtmlTab();
            viewItem.Tab = newTab;
            await RebindTabAndSwitch(viewItem, newTab, currentUrl);
        }

        private void BindTabEvents(TabViewItem viewItem, IBrowserTab tab)
        {
            tab.TitleChanged += (title) => _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                string displayTitle = string.IsNullOrEmpty(title) ? "新标签页" : title;
                if (tab.IsSuspended)
                    viewItem.TitleText.Text = "[已挂起] " + displayTitle;
                else
                    viewItem.TitleText.Text = displayTitle;
            });
            tab.UrlChanged += (u) => { if (_currentTab == tab) { toolbarControl.UpdateNavState(tab.CanGoBack, tab.CanGoForward, u); UpdateStarButton(); } if (!string.IsNullOrEmpty(u) && u != "about:blank") HistoryManager.Add(tab.Title ?? u, u); };
            tab.CanGoBackChanged += (can) => { if (_currentTab == tab) toolbarControl.UpdateNavState(can, tab.CanGoForward, tab.CurrentUrl); };
            tab.CanGoForwardChanged += (can) => { if (_currentTab == tab) toolbarControl.UpdateNavState(tab.CanGoBack, can, tab.CurrentUrl); };
            tab.FaviconChanged += (faviconUrl) => UpdateFavicon(viewItem, faviconUrl);
            tab.ContextMenuRequested += OnTabContextMenuRequested;

            tab.NewWindowRequested += (url) =>
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    await CreateNewTabAsync(tab.Engine, url);
                });
            };
        }

        private async Task RebindTabAndSwitch(TabViewItem viewItem, IBrowserTab newTab, string urlToNavigate = null)
        {
            BindTabEvents(viewItem, newTab);
            viewItem.EngineMark.Text = newTab.Engine == EngineType.EdgeHtml ? "E" : "W";
            viewItem.EngineMark.Foreground = newTab.Engine == EngineType.EdgeHtml ? _edgeBlueBrush : _webGreenBrush;
            _currentTab = null;
            await SwitchToTabAsync(viewItem);
            if (!string.IsNullOrEmpty(urlToNavigate)) await newTab.NavigateAsync(urlToNavigate);
            else await newTab.NavigateAsync("about:blank");
        }

        private void UpdateTabSuspendedVisual(TabViewItem viewItem, bool suspended)
        {
            viewItem.IsSuspended = suspended;
            if (viewItem.SuspendedIndicator != null)
                viewItem.SuspendedIndicator.Visibility = suspended ? Visibility.Visible : Visibility.Collapsed;
            string originalTitle = viewItem.Tab.Title ?? "新标签页";
            viewItem.TitleText.Text = suspended ? "[已挂起] " + originalTitle : originalTitle;
        }

        private bool HasActiveDownload(IBrowserTab tab)
        {
            return DownloadManager.Downloads.Any(d => d.Status == "下载中" || d.Status == "已暂停");
        }

        private async Task SwitchToTabAsync(TabViewItem viewItem)
        {
            if (!_isLoaded || _currentTab == viewItem.Tab) return;

            if (_enableTabSuspend && _currentTab != null && _suspendTimers.ContainsKey(_currentTab))
            {
                _suspendTimers[_currentTab].Stop();
                _suspendTimers.Remove(_currentTab);
            }

            ContentContainer.Child = null;
            var previousTab = _currentTab;
            _currentTab = viewItem.Tab;

            if (_currentTab is WebView2Tab wv2 && wv2.IsSuspended)
            {
                await wv2.ResumeAsync();
                UpdateTabSuspendedVisual(viewItem, false);
            }
            else if (_currentTab is EdgeHtmlTab edgeTab && edgeTab.IsSuspended)
            {
                await edgeTab.ResumeAsync();
                UpdateTabSuspendedVisual(viewItem, false);
            }

            ContentContainer.Child = _currentTab.ViewElement;
            if (_currentTab is WebView2Tab wv2Tab) await wv2Tab.EnsureInitializedAsync();

            toolbarControl.SetEngine(_currentTab.Engine);
            toolbarControl.UpdateNavState(_currentTab.CanGoBack, _currentTab.CanGoForward, _currentTab.CurrentUrl);
            UpdateStarButton();

            foreach (var t in _tabViews) t.Container.Background = t == viewItem ? _selectedBrush : _unselectedBrush;

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
                        var vi = _tabViews.FirstOrDefault(v => v.Tab == wv2s);
                        if (vi != null) UpdateTabSuspendedVisual(vi, true);
                    }
                    else if (tab is EdgeHtmlTab edgeS)
                    {
                        await edgeS.SuspendAsync();
                        var vi = _tabViews.FirstOrDefault(v => v.Tab == edgeS);
                        if (vi != null) UpdateTabSuspendedVisual(vi, true);
                    }
                    _suspendTimers.Remove(tab);
                };
                _suspendTimers[previousTab] = timer;
                timer.Start();
            }
        }

        private void UpdateStarButton() { if (_currentTab != null) toolbarControl.UpdateFavoriteButton(FavoritesManager.Instance.ContainsUrl(_currentTab.CurrentUrl)); }

        private async Task CreateNewTabAsync(EngineType engine, string url = null)
        {
            if (!_isLoaded) return;
            IBrowserTab tab = engine == EngineType.WebView2 ? new WebView2Tab() : new EdgeHtmlTab();
            var tabBorder = new Border { Height = 32, Background = _unselectedBrush, BorderBrush = new SolidColorBrush(Colors.LightGray), BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Stretch, Width = MaxTabWidth };
            var tabPanel = new Grid { VerticalAlignment = VerticalAlignment.Center };
            tabPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tabPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var engineMark = new TextBlock { Text = engine == EngineType.EdgeHtml ? "E" : "W", Foreground = engine == EngineType.EdgeHtml ? _edgeBlueBrush : _webGreenBrush, FontSize = 11, FontWeight = Windows.UI.Text.FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            var faviconPlaceholder = new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\xE774", FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray), Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            var faviconImage = new Image { Width = 14, Height = 14, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            var titleText = new TextBlock { Text = "新标签页", TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 10, Foreground = _foregroundBrush, VerticalAlignment = VerticalAlignment.Center };
            var suspendedIndicator = new TextBlock { Text = "💤", FontSize = 10, Foreground = new SolidColorBrush(Colors.Gray), Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            infoPanel.Children.Add(engineMark);
            infoPanel.Children.Add(faviconPlaceholder);
            infoPanel.Children.Add(faviconImage);
            infoPanel.Children.Add(suspendedIndicator);
            infoPanel.Children.Add(titleText);
            Grid.SetColumn(infoPanel, 0); tabPanel.Children.Add(infoPanel);
            var closeBtn = new Button { Content = "\xE711", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10, Foreground = _mutedForegroundBrush, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0), Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0) };
            Grid.SetColumn(closeBtn, 1); tabPanel.Children.Add(closeBtn);
            tabBorder.Child = tabPanel; TabBarPanel.Children.Add(tabBorder);
            var viewItem = new TabViewItem
            {
                Tab = tab,
                TitleText = titleText,
                CloseButton = closeBtn,
                Container = tabBorder,
                FaviconImage = faviconImage,
                FaviconPlaceholder = faviconPlaceholder,
                EngineMark = engineMark,
                SuspendedIndicator = suspendedIndicator,
                IsSuspended = false
            };
            _tabViews.Add(viewItem);
            closeBtn.Click += (_, _) => _ = CloseTab(viewItem);
            tabBorder.PointerPressed += OnTabPointerPressed; tabBorder.PointerMoved += OnTabPointerMoved; tabBorder.PointerReleased += OnTabPointerReleased; tabBorder.PointerCanceled += OnTabPointerReleased; tabBorder.RightTapped += OnTabRightTapped;
            tabBorder.PointerEntered += (_, _) => { if (_currentTab != tab && !_isDragging) tabBorder.Background = _hoverBrush; };
            tabBorder.PointerExited += (_, _) => { if (_currentTab != tab && !_isDragging) tabBorder.Background = _unselectedBrush; };
            BindTabEvents(viewItem, tab);
            await SwitchToTabAsync(viewItem);
            if (!string.IsNullOrEmpty(url)) await tab.NavigateAsync(url);
            else await tab.NavigateAsync("about:blank");
            UpdateTabLayout();
            toolbarControl.FocusAddressBarAndClear();
        }

        private async Task CloseTab(TabViewItem viewItem)
        {
            if (!_isLoaded) return;
            int index = _tabViews.IndexOf(viewItem);
            if (index < 0) return;

            string url = viewItem.Tab.CurrentUrl;
            if (!string.IsNullOrEmpty(url) && url != "about:blank")
                _closedTabUrls.Push(url);

            var border = viewItem.Container;
            border.IsHitTestVisible = false;

            var storyboard = new Storyboard();
            var widthAnim = new DoubleAnimation
            {
                From = border.Width,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(180),
                EnableDependentAnimation = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(widthAnim, border);
            Storyboard.SetTargetProperty(widthAnim, "Width");
            storyboard.Children.Add(widthAnim);

            var opacityAnim = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(120)
            };
            Storyboard.SetTarget(opacityAnim, border);
            Storyboard.SetTargetProperty(opacityAnim, "Opacity");
            storyboard.Children.Add(opacityAnim);

            var tcs = new TaskCompletionSource<bool>();
            storyboard.Completed += (s, e) =>
            {
                (border.Parent as Panel)?.Children.Remove(border);
                tcs.TrySetResult(true);
            };
            storyboard.Begin();
            await tcs.Task;

            TabViewItem nextTab = null;
            if (_tabViews.Count > 1)
                nextTab = (index > 0) ? _tabViews[index - 1] : _tabViews[1];
            _tabViews.RemoveAt(index);

            if (_currentTab == viewItem.Tab)
            {
                if (nextTab != null)
                    await SwitchToTabAsync(nextTab);
                else
                    _currentTab = null;
            }

            viewItem.Tab.Dispose();

            if (_suspendTimers.ContainsKey(viewItem.Tab))
            {
                _suspendTimers[viewItem.Tab].Stop();
                _suspendTimers.Remove(viewItem.Tab);
            }

            if (_tabViews.Count == 0)
                await CreateNewTabAsync(_defaultEngine, "about:blank");
            else
                UpdateTabLayout();
        }

        // ========= 拖拽逻辑 =========
        private void OnTabPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var properties = e.GetCurrentPoint((UIElement)sender).Properties;
            if (properties.IsRightButtonPressed) return;
            ResetDragState();
            if (_tabViews.Count <= 1) return;
            var border = sender as Border;
            if (border == null) return;
            var viewItem = _tabViews.FirstOrDefault(v => v.Container == border);
            if (viewItem == null) return;

            _dragItem = viewItem;
            _dragOffset = e.GetCurrentPoint(viewItem.Container).Position;
            _hasMoved = false; _isDragging = false; _totalDx = 0; _isDetaching = false;
            _dragStartCanvasPoint = e.GetCurrentPoint(DragCanvas).Position;
            _dragStartScreenPoint = CoreWindow.GetForCurrentThread().PointerPosition;

            _dragGhost = new Border
            {
                Width = _dragItem.Container.ActualWidth,
                Height = 32,
                Background = _selectedBrush,
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = _dragItem.Tab.Title ?? "新标签页",
                    FontSize = 12,
                    Foreground = _foregroundBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0)
                }
            };
            var initialPos = viewItem.Container.TransformToVisual(DragCanvas).TransformPoint(new Point(0, 0));
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

            border.CapturePointer(e.Pointer);
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

                var screenPos = CoreWindow.GetForCurrentThread().PointerPosition;
                var windowBounds = Window.Current.Bounds;
                double threshold = 20;
                bool outside = screenPos.Y < -threshold || screenPos.Y > windowBounds.Height + threshold ||
                               screenPos.X < -threshold || screenPos.X > windowBounds.Width + threshold;
                if (!_isDetaching && outside)
                {
                    _isDetaching = true;
                    if (_dragGhost != null) { DragCanvas.Children.Remove(_dragGhost); _dragGhost = null; }
                    if (_dragItem != null && _dragItem.Container.Tag is UIElement originalChild)
                    {
                        _dragItem.Container.Child = originalChild;
                        _dragItem.Container.Tag = null;
                        _dragItem.Container.Opacity = 1.0;
                    }
                    MoveTabToNewWindowAsync(_dragItem);
                    _dragItem = null;
                    _isDragging = false;
                    _hasMoved = false;
                    _totalDx = 0;
                }
            }
            e.Handled = true;
        }

        private void UpdateDragSort()
        {
            if (_dragGhost == null) return;
            if ((DateTime.Now - _lastReorderTime).TotalMilliseconds < 200) return;

            double ghostCenterInCanvas = Canvas.GetLeft(_dragGhost) + _dragGhost.Width / 2;
            double ghostLocalX = ghostCenterInCanvas - _tabBarPanelOffset.X;

            int dragIndex = _tabViews.IndexOf(_dragItem);
            double accumulatedWidth = 0;
            int targetIndex = -1;

            for (int i = 0; i < _tabViews.Count; i++)
            {
                if (i == dragIndex) continue;
                var item = _tabViews[i];
                double itemWidth = item.Container.ActualWidth;
                double containerCenter = accumulatedWidth + itemWidth / 2;
                if (ghostLocalX < containerCenter)
                {
                    targetIndex = i;
                    break;
                }
                accumulatedWidth += itemWidth;
            }
            if (targetIndex == -1) targetIndex = _tabViews.Count - 1;
            if (targetIndex == dragIndex) return;

            _tabViews.RemoveAt(dragIndex);
            _tabViews.Insert(targetIndex, _dragItem);
            _lastReorderTime = DateTime.Now;
        }

        private void SyncTabPanelOrder()
        {
            TabBarPanel.Children.Clear();
            foreach (var item in _tabViews)
                TabBarPanel.Children.Add(item.Container);
        }

        private void OnTabPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_dragItem == null) return;
            try
            {
                if (_isDetaching) return;
                if (!_hasMoved)
                {
                    SwitchToTab(_dragItem);
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

        private async void MoveTabToNewWindowAsync(TabViewItem item)
        {
            string url = item.Tab.CurrentUrl;
            int index = _tabViews.IndexOf(item);
            if (index < 0) return;

            if (item.Container.Parent is Panel panel)
                panel.Children.Remove(item.Container);
            _tabViews.RemoveAt(index);

            if (_currentTab == item.Tab)
            {
                if (_tabViews.Count > 0)
                {
                    var next = _tabViews[Math.Min(index, _tabViews.Count - 1)];
                    await SwitchToTabAsync(next);
                }
                else
                {
                    _currentTab = null;
                    ContentContainer.Child = null;
                    await CreateNewTabAsync(_defaultEngine, "about:blank");
                }
            }
            else if (_tabViews.Count == 0)
            {
                await CreateNewTabAsync(_defaultEngine, "about:blank");
            }
            else
            {
                UpdateTabLayout();
            }

            item.Tab.Dispose();

            try
            {
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
            catch (Exception ex)
            {
                Debug.WriteLine($"MoveTabToNewWindow failed: {ex.Message}");
            }
        }

        private void OnTabPointerCanceled(object sender, PointerRoutedEventArgs e) { ResetDragState(); }
        private void SwitchToTab(TabViewItem viewItem) => _ = SwitchToTabAsync(viewItem);

        private void OnTabRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var border = sender as Border;
            var viewItem = _tabViews.FirstOrDefault(v => v.Container == border);
            if (viewItem == null) return;
            var menu = new MenuFlyout();
            var newTabItem = new MenuFlyoutItem { Text = "新建标签页" };
            newTabItem.Click += async (_, _) => await CreateNewTabAsync(_defaultEngine, "about:blank");
            menu.Items.Add(newTabItem);
            var reloadItem = new MenuFlyoutItem { Text = "重新加载" }; reloadItem.Click += (_, _) => viewItem.Tab?.RefreshAsync(); menu.Items.Add(reloadItem);
            var closeItem = new MenuFlyoutItem { Text = "关闭标签页" }; closeItem.Click += (_, _) => _ = CloseTab(viewItem); menu.Items.Add(closeItem);
            menu.Items.Add(new MenuFlyoutSeparator());
            var closeOthersItem = new MenuFlyoutItem { Text = "关闭其他标签页" }; closeOthersItem.Click += (_, _) => CloseOtherTabs(viewItem); menu.Items.Add(closeOthersItem);
            var closeRightItem = new MenuFlyoutItem { Text = "关闭右侧标签页" }; closeRightItem.Click += (_, _) => CloseTabsToRight(viewItem); menu.Items.Add(closeRightItem);
            menu.Items.Add(new MenuFlyoutSeparator());
            var moveItem = new MenuFlyoutItem { Text = "移动到新窗口" }; moveItem.Click += (_, _) => { MoveTabToNewWindowAsync(viewItem); }; menu.Items.Add(moveItem);
            menu.ShowAt(border, e.GetPosition(border));
        }

        private void CloseOtherTabs(TabViewItem keepItem) { foreach (var item in _tabViews.Where(t => t != keepItem).ToList()) _ = CloseTab(item); }
        private void CloseTabsToRight(TabViewItem startItem) { int index = _tabViews.IndexOf(startItem); if (index < 0) return; foreach (var item in _tabViews.Skip(index + 1).ToList()) _ = CloseTab(item); }

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

        private async Task SwitchCurrentTabEngine(EngineType newEngine)
        {
            if (_currentTab == null || _currentTab.Engine == newEngine) return;
            var viewItem = _tabViews.FirstOrDefault(v => v.Tab == _currentTab);
            if (viewItem == null) return;
            string currentUrl = _currentTab.CurrentUrl;
            _currentTab.Dispose();
            IBrowserTab newTab = newEngine == EngineType.WebView2 ? new WebView2Tab() : new EdgeHtmlTab();
            viewItem.Tab = newTab;
            await RebindTabAndSwitch(viewItem, newTab, currentUrl);
        }

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
            foreach (var item in _tabViews)
            {
                string url = item.Tab.CurrentUrl;
                if (!string.IsNullOrEmpty(url) && url != "about:blank" && !FavoritesManager.Instance.ContainsUrl(url))
                { FavoritesManager.Instance.Add(string.IsNullOrEmpty(item.Tab.Title) ? url : item.Tab.Title, url); count++; }
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

        private void HubPane_NavigateRequested(string url) { _currentTab?.NavigateAsync(url); HubSplitView.IsPaneOpen = false; }

        private void NewTabBtn_Click(object sender, RoutedEventArgs e) => _ = CreateNewTabAsync(_defaultEngine, "about:blank");
        private void ScrollLeftBtn_Click(object sender, RoutedEventArgs e) => TabScrollViewer.ChangeView(TabScrollViewer.HorizontalOffset - 100, null, null);
        private void ScrollRightBtn_Click(object sender, RoutedEventArgs e) => TabScrollViewer.ChangeView(TabScrollViewer.HorizontalOffset + 100, null, null);
        private void TabScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) { }
        private void SettingsPane_CloseRequested(object sender, EventArgs e) => SettingsSplitView.IsPaneOpen = false;

        private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs e) { SetSafeZonePadding(); if (!_isDragging) UpdateTabLayout(); }
        private void SetSafeZonePadding()
        {
            double systemOverlay = 100;
            try { var bounds = ApplicationView.GetForCurrentView().VisibleBounds; var windowBounds = Window.Current.Bounds; systemOverlay = windowBounds.Width - bounds.Width; } catch { }
            if (systemOverlay <= 0) systemOverlay = 100;
            _rightReserved = systemOverlay + AdditionalMargin;
            TabBarBorder.Padding = new Thickness(0, 0, _rightReserved, 0);
        }
        private void TabBarBorder_SizeChanged(object sender, SizeChangedEventArgs e) { SetSafeZonePadding(); if (!_isDragging) UpdateTabLayout(); }
        private void UpdateRightPanelMargin()
        {
            RightSidePanel.UpdateLayout();
            double rightFixed = RightSidePanel.ActualWidth + RightSidePanel.Margin.Left + RightSidePanel.Margin.Right;
            double leftFixed = ScrollLeftBtn.Visibility == Visibility.Visible ? ScrollLeftBtn.ActualWidth + ScrollLeftBtn.Margin.Left + ScrollLeftBtn.Margin.Right : 0;
            double contentWidth = TabBarBorder.ActualWidth - _rightReserved;
            double requiredForDrag = rightFixed + MinDragWidth;
            double extraOffset = Math.Max(0, requiredForDrag - (contentWidth - leftFixed));
            RightSidePanel.Margin = new Thickness(0, 0, ButtonBaseOffset + extraOffset, 0);
            RightSidePanel.UpdateLayout();
        }
        private void UpdateTabLayout()
        {
            if (!_isLoaded || _tabViews.Count == 0 || _isDragging) return;
            TabBarBorder.UpdateLayout();
            RightSidePanel.Margin = new Thickness(0, 0, ButtonBaseOffset, 0);
            RightSidePanel.UpdateLayout();
            UpdateRightPanelMargin();
            double leftFixed = ScrollLeftBtn.Visibility == Visibility.Visible ? ScrollLeftBtn.ActualWidth + ScrollLeftBtn.Margin.Left + ScrollLeftBtn.Margin.Right : 0;
            double rightFixed = RightSidePanel.ActualWidth + RightSidePanel.Margin.Left + RightSidePanel.Margin.Right;
            double contentWidth = TabBarBorder.ActualWidth - _rightReserved;
            double availableWidth = Math.Max(0, contentWidth - leftFixed - rightFixed - MinDragWidth);
            double idealTotal = _tabViews.Count * MaxTabWidth;
            double minTotal = _tabViews.Count * MinTabWidth;
            bool needScroll = false;
            double targetTabWidth = MaxTabWidth;
            if (idealTotal <= availableWidth) targetTabWidth = MaxTabWidth;
            else if (minTotal <= availableWidth) targetTabWidth = availableWidth / _tabViews.Count;
            else { targetTabWidth = MinTabWidth; needScroll = true; }
            foreach (var item in _tabViews) item.Container.Width = targetTabWidth;
            TabScrollViewer.MaxWidth = availableWidth;
            if (!needScroll) { TabScrollViewer.HorizontalScrollMode = ScrollMode.Disabled; ScrollLeftBtn.Visibility = Visibility.Collapsed; ScrollRightBtn.Visibility = Visibility.Collapsed; }
            else { TabScrollViewer.HorizontalScrollMode = ScrollMode.Enabled; ScrollLeftBtn.Visibility = Visibility.Visible; ScrollRightBtn.Visibility = Visibility.Visible; }
            UpdateRightPanelMargin();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string url) _pendingUrl = url;
        }

        private async void UpdateFavicon(TabViewItem viewItem, string faviconUrl)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (string.IsNullOrEmpty(faviconUrl) || !Uri.TryCreate(faviconUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                { viewItem.FaviconPlaceholder.Visibility = Visibility.Visible; viewItem.FaviconImage.Visibility = Visibility.Collapsed; viewItem.FaviconImage.Source = null; return; }
                try { var bitmap = new BitmapImage { UriSource = uri }; viewItem.FaviconImage.Source = bitmap; viewItem.FaviconImage.Visibility = Visibility.Visible; viewItem.FaviconPlaceholder.Visibility = Visibility.Collapsed; }
                catch { viewItem.FaviconImage.Visibility = Visibility.Collapsed; viewItem.FaviconPlaceholder.Visibility = Visibility.Visible; }
            });
        }
    }
}