using System;
using System.Threading.Tasks;
using EdgeRebuild.Core;
using EdgeRebuild.Services;
using Windows.System;
using Windows.UI.ViewManagement;
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
    /// Metro IE 风格窗口：内容区之上无工具栏，地址栏 + 标签条 + AppBar 全部在底部，
    /// 全屏缩略图标签网格，磁贴风格新标签主页。色板固定（MetroColorPalette），
    /// 与主窗口皮肤完全隔离；键盘快捷键自处理，不经过全局 ShortcutManager。
    /// </summary>
    public sealed partial class MetroPage : Page
    {
        private AppWindow _appWindow;
        private EngineType _defaultEngine;
        private TabManager _tabManager;
        private bool _gridOpen;
        private bool _isFullscreen;

        public MetroPage()
        {
            InitializeComponent();
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
            _tabManager.TabCountChanged += count =>
            {
                AppBar.UpdateTabCount(count);
                TabStrip.Refresh(_tabManager.Tabs, FindViewItem(_tabManager.CurrentTab));
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
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Log($"[Metro] 全屏切换失败: {ex.Message}");
                NotificationService.ShowToast("Metro IE", "全屏切换失败");
            }
        }

        // ---- 地址栏 ----
        private void AddressBar_BackRequested() => _ = _tabManager.CurrentTab?.GoBackAsync();
        private void AddressBar_ForwardRequested() => _ = _tabManager.CurrentTab?.GoForwardAsync();
        private void AddressBar_RefreshRequested() => _ = _tabManager.CurrentTab?.RefreshAsync();
        private void AddressBar_HomeRequested() => _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank", false, true);
        private void AddressBar_FullscreenRequested() => ToggleFullscreen();
        private void AddressBar_UrlSubmitted(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;
            string url = input.Trim();
            if (!url.Contains("://") && !url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;
            _ = _tabManager.CreateTabAsync(_defaultEngine, url, false, true);
        }

        // ---- 底部标签条 ----
        private void TabStrip_TabSelected(TabViewItem viewItem) => _ = _tabManager.SwitchToTabAsync(viewItem);
        private void TabStrip_NewTabRequested() => _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank", false, true);

        // ---- AppBar ----
        private void AppBar_TabsClicked() => ToggleGrid();
        private void AppBar_NewTabClicked() => _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank", false, true);
        private void AppBar_SettingsClicked()
        {
            NotificationService.ShowToast("Metro IE", "设置面板（开发中）");
        }

        // ---- 缩略图网格（淡入缩放动画） ----
        private void ToggleGrid()
        {
            if (_gridOpen) CloseGrid();
            else OpenGrid();
        }

        private void OpenGrid()
        {
            _gridOpen = true;
            TabGridOverlay.Refresh(_tabManager.Tabs, FindViewItem(_tabManager.CurrentTab));
            TabGridOverlay.Visibility = Visibility.Visible;
            TabGridOverlay.Opacity = 0;
            var sb = new Storyboard();
            var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(160) };
            Storyboard.SetTarget(fade, TabGridOverlay);
            Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(fade);
            sb.Begin();
        }

        private void CloseGrid()
        {
            _gridOpen = false;
            var sb = new Storyboard();
            var fade = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(120) };
            Storyboard.SetTarget(fade, TabGridOverlay);
            Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(fade);
            sb.Completed += (s, e) => TabGridOverlay.Visibility = Visibility.Collapsed;
            sb.Begin();
        }

        private void RefreshGridIfOpen()
        {
            if (_gridOpen)
                TabGridOverlay.Refresh(_tabManager.Tabs, FindViewItem(_tabManager.CurrentTab));
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
            CloseGrid();
            _ = _tabManager.SwitchToTabAsync(viewItem);
        }

        private void TabGrid_TabCloseRequested(TabViewItem viewItem)
        {
            _ = _tabManager.CloseTabAsync(viewItem);
            RefreshGridIfOpen();
        }

        private void TabGrid_NewTabRequested()
        {
            CloseGrid();
            _ = _tabManager.CreateTabAsync(_defaultEngine, "about:blank", false, true);
        }

        private void TabGrid_DismissRequested() => CloseGrid();

        // ---- 键盘（Metro 窗口独立快捷键，不经过全局 ShortcutManager） ----
        private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrl = Windows.UI.Core.CoreWindow.GetForCurrentThread()?.GetKeyState(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) ?? false;

            if (_gridOpen && e.Key == VirtualKey.Escape) { CloseGrid(); e.Handled = true; return; }

            if (ctrl)
            {
                if (TryHandleCtrlNumber(e.Key)) { e.Handled = true; return; }
                switch (e.Key)
                {
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
                }
            }
            else if (e.Key == VirtualKey.F11)
            {
                e.Handled = true;
                ToggleFullscreen();
            }
            else if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
            }
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
