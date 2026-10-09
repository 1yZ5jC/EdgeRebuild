using System;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using EdgeRebuild.Services;

namespace EdgeRebuild.Core
{
    public class AppMenuManager
    {
        private readonly TabManager _tabManager;
        private readonly EngineType _defaultEngine;
        private readonly Func<EngineType> _getCurrentEngine;
        private readonly Func<Task> _onPrint;
        private readonly Func<Task> _onViewSource;
        private readonly Func<Task> _onWebCapture;
        private readonly Action _onShowHistory;
        private readonly Action _onShowSettings;
        private readonly Func<Task> _onClearData;
        private readonly Action _onBookmarkAll;
        private readonly Action _onAbout;
        private readonly Action _onNewWindow;
        private readonly Action _onNewInPrivateWindow;
        private readonly Action _onToggleInPrivate;
        private readonly Func<bool> _getIsInPrivate;
        private readonly Action _onOpenMetroWindow;
        private readonly Action<double> _onZoomAdjust;
        private readonly Action _onZoomReset;
        private readonly Action _onFind;
        private readonly Action _onDevTools;
        private readonly Action<EngineType> _onEngineSwitch;
        private readonly Func<Task> _onReopenTab;
        private readonly Func<Task> _onNewTab;
        private readonly Action _onClearClosedTabs;
        private readonly Func<bool> _hasClosedTabs;
        private readonly Action _onCopyCurrentUrl;
        private readonly Func<bool> _isReaderModeActive;
        private readonly Action _onToggleReaderMode;
        private readonly Func<string> _getReadAloudLabel;
        private readonly Action _onToggleReadAloud;
        private readonly Action _onOpenDemoWindow;

        public AppMenuManager(
            TabManager tabManager,
            EngineType defaultEngine,
            Func<EngineType> getCurrentEngine,
            Func<Task> onPrint,
            Func<Task> onViewSource,
            Func<Task> onWebCapture,
            Action onShowHistory,
            Action onShowSettings,
            Func<Task> onClearData,
            Action onBookmarkAll,
            Action onAbout,
            Action onNewWindow,
            Action onNewInPrivateWindow,
            Action onToggleInPrivate,
            Func<bool> getIsInPrivate,
            Action onOpenMetroWindow,
            Action<double> onZoomAdjust,
            Action onZoomReset,
            Action onFind,
            Action onDevTools,
            Action<EngineType> onEngineSwitch,
            Func<Task> onReopenTab,
            Func<Task> onNewTab,
            Action onClearClosedTabs,
            Func<bool> hasClosedTabs,
            Action onCopyCurrentUrl,
            Func<bool> isReaderModeActive,
            Action onToggleReaderMode,
            Func<string> getReadAloudLabel,
            Action onToggleReadAloud,
            Action onOpenDemoWindow = null)
        {
            _tabManager = tabManager;
            _defaultEngine = defaultEngine;
            _getCurrentEngine = getCurrentEngine;
            _onPrint = onPrint;
            _onViewSource = onViewSource;
            _onWebCapture = onWebCapture;
            _onShowHistory = onShowHistory;
            _onShowSettings = onShowSettings;
            _onClearData = onClearData;
            _onBookmarkAll = onBookmarkAll;
            _onAbout = onAbout;
            _onNewWindow = onNewWindow;
            _onNewInPrivateWindow = onNewInPrivateWindow;
            _onToggleInPrivate = onToggleInPrivate;
            _getIsInPrivate = getIsInPrivate;
            _onOpenMetroWindow = onOpenMetroWindow;
            _onZoomAdjust = onZoomAdjust;
            _onZoomReset = onZoomReset;
            _onFind = onFind;
            _onDevTools = onDevTools;
            _onEngineSwitch = onEngineSwitch;
            _onReopenTab = onReopenTab;
            _onNewTab = onNewTab;
            _onClearClosedTabs = onClearClosedTabs;
            _hasClosedTabs = hasClosedTabs;
            _onCopyCurrentUrl = onCopyCurrentUrl;
            _isReaderModeActive = isReaderModeActive;
            _onToggleReaderMode = onToggleReaderMode;
            _getReadAloudLabel = getReadAloudLabel;
            _onToggleReadAloud = onToggleReadAloud;
            _onOpenDemoWindow = onOpenDemoWindow;
        }

        public MenuFlyout BuildMenu()
        {
            var menu = new MenuFlyout();
            ApplyThemeToFlyout(menu);

            // 渲染引擎子菜单
            var engineSub = new MenuFlyoutSubItem { Text = "渲染引擎" };
            var edgeHtmlItem = new MenuFlyoutItem { Text = "EdgeHTML" };
            edgeHtmlItem.Click += (_, _) => _onEngineSwitch?.Invoke(EngineType.EdgeHtml);
            var webView2Item = new MenuFlyoutItem { Text = "WebView2" };
            webView2Item.Click += (_, _) => _onEngineSwitch?.Invoke(EngineType.WebView2);
            engineSub.Items.Add(edgeHtmlItem);
            engineSub.Items.Add(webView2Item);
            menu.Items.Add(engineSub);
            menu.Items.Add(new MenuFlyoutSeparator());

            // 新建窗口
            var newWindowItem = new MenuFlyoutItem { Text = "新建窗口" };
            newWindowItem.Click += (_, _) => _onNewWindow?.Invoke();
            menu.Items.Add(newWindowItem);

            // 新建 InPrivate 窗口（独立窗口：不写历史、不存会话）
            var inPrivateWindowItem = new MenuFlyoutItem { Text = "新建 InPrivate 窗口" };
            inPrivateWindowItem.Click += (_, _) => _onNewInPrivateWindow?.Invoke();
            menu.Items.Add(inPrivateWindowItem);

            // InPrivate 模式（当前窗口内切换，不依赖子窗口）
            var inPrivateItem = new MenuFlyoutItem
            {
                Text = (_getIsInPrivate?.Invoke() ?? false) ? "退出 InPrivate 模式" : "进入 InPrivate 模式"
            };
            inPrivateItem.Click += (_, _) => _onToggleInPrivate?.Invoke();
            menu.Items.Add(inPrivateItem);

            // Metro IE 窗口（无标签条 + 缩略图网格 + 磁贴主页）
            var metroWindowItem = new MenuFlyoutItem { Text = "打开 Metro IE 窗口" };
            metroWindowItem.Click += (_, _) => _onOpenMetroWindow?.Invoke();
            menu.Items.Add(metroWindowItem);

            // 多窗口测试页（Demo）：标签条 + 副窗口按钮（MainPageWinBtn）
            var demoWindowItem = new MenuFlyoutItem { Text = "打开多窗口测试页" };
            demoWindowItem.Click += (_, _) => _onOpenDemoWindow?.Invoke();
            menu.Items.Add(demoWindowItem);
            menu.Items.Add(new MenuFlyoutSeparator());

            // 缩放子菜单
            var zoomSub = new MenuFlyoutSubItem { Text = "缩放" };
            var zoomInItem = new MenuFlyoutItem { Text = "放大" };
            zoomInItem.Click += (_, _) => _onZoomAdjust?.Invoke(0.1);
            var zoomOutItem = new MenuFlyoutItem { Text = "缩小" };
            zoomOutItem.Click += (_, _) => _onZoomAdjust?.Invoke(-0.1);
            var zoomResetItem = new MenuFlyoutItem { Text = "重置" };
            zoomResetItem.Click += (_, _) => _onZoomReset?.Invoke();
            zoomSub.Items.Add(zoomInItem);
            zoomSub.Items.Add(zoomOutItem);
            zoomSub.Items.Add(zoomResetItem);
            menu.Items.Add(zoomSub);

            // 打印、查找、源代码
            var readerItem = new MenuFlyoutItem { Text = (_isReaderModeActive?.Invoke() ?? false) ? "退出阅读模式" : "阅读模式" };
            readerItem.Click += (_, _) => _onToggleReaderMode?.Invoke();
            menu.Items.Add(readerItem);

            var readAloudItem = new MenuFlyoutItem { Text = _getReadAloudLabel?.Invoke() ?? "大声朗读" };
            readAloudItem.Click += (_, _) => _onToggleReadAloud?.Invoke();
            menu.Items.Add(readAloudItem);

            var printItem = new MenuFlyoutItem { Text = "打印" };
            printItem.Click += async (_, _) => await _onPrint?.Invoke();
            menu.Items.Add(printItem);

            var findItem = new MenuFlyoutItem { Text = "查找" };
            findItem.Click += (_, _) => _onFind?.Invoke();
            menu.Items.Add(findItem);

            var sourceItem = new MenuFlyoutItem { Text = "查看源代码" };
            sourceItem.Click += async (_, _) => await _onViewSource?.Invoke();
            menu.Items.Add(sourceItem);

            var captureItem = new MenuFlyoutItem { Text = "网页捕获（截图批注）" };
            captureItem.Click += async (_, _) => { if (_onWebCapture != null) await _onWebCapture(); };
            menu.Items.Add(captureItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            // 更多工具
            var toolsSub = new MenuFlyoutSubItem { Text = "更多工具" };
            var devToolsItem = new MenuFlyoutItem { Text = "开发者工具" };
            devToolsItem.Click += (_, _) => _onDevTools?.Invoke();
            toolsSub.Items.Add(devToolsItem);
            menu.Items.Add(toolsSub);

            menu.Items.Add(new MenuFlyoutSeparator());

            // 复制当前网址
            var copyUrlItem = new MenuFlyoutItem { Text = "复制当前网址" };
            copyUrlItem.Click += (_, _) => _onCopyCurrentUrl?.Invoke();
            menu.Items.Add(copyUrlItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            // 重新打开关闭的标签
            var reopenSub = new MenuFlyoutSubItem { Text = "重新打开关闭的标签" };
            if (!_hasClosedTabs())
            {
                var emptyItem = new MenuFlyoutItem { Text = "没有最近关闭的标签", IsEnabled = false };
                reopenSub.Items.Add(emptyItem);
            }
            else
            {
                var reopenItem = new MenuFlyoutItem { Text = "恢复最近关闭的标签" };
                reopenItem.Click += async (_, _) => await _onReopenTab?.Invoke();
                reopenSub.Items.Add(reopenItem);

                var clearItem = new MenuFlyoutItem { Text = "清空列表" };
                clearItem.Click += (_, _) => _onClearClosedTabs?.Invoke();
                reopenSub.Items.Add(clearItem);
            }
            menu.Items.Add(reopenSub);

            // 收藏全部
            var bookmarkAllItem = new MenuFlyoutItem { Text = "将所有标签加入收藏" };
            bookmarkAllItem.Click += (_, _) => _onBookmarkAll?.Invoke();
            menu.Items.Add(bookmarkAllItem);

            // 历史记录
            var historyItem = new MenuFlyoutItem { Text = "历史记录" };
            historyItem.Click += (_, _) => _onShowHistory?.Invoke();
            menu.Items.Add(historyItem);

            // 清除浏览数据
            var clearDataItem = new MenuFlyoutItem { Text = "清除浏览数据" };
            clearDataItem.Click += async (_, _) => await _onClearData?.Invoke();
            menu.Items.Add(clearDataItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            // 设置
            var settingsItem = new MenuFlyoutItem { Text = "设置" };
            settingsItem.Click += (_, _) => _onShowSettings?.Invoke();
            menu.Items.Add(settingsItem);

            // 关于
            var aboutItem = new MenuFlyoutItem { Text = "关于 Edge Rebuild" };
            aboutItem.Click += (_, _) => _onAbout?.Invoke();
            menu.Items.Add(aboutItem);

            return menu;
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
    }
}