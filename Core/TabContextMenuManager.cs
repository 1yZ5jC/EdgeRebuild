using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using EdgeRebuild.Services;

namespace EdgeRebuild.Core
{
    public class TabContextMenuManager
    {
        private readonly TabManager _tabManager;
        private readonly EngineType _defaultEngine;
        private readonly Func<EngineType> _getCurrentEngine;
        private readonly Func<Task> _onPrintRequested;
        private readonly Func<Task> _onViewSourceRequested;

        public TabContextMenuManager(
            TabManager tabManager,
            EngineType defaultEngine,
            Func<EngineType> getCurrentEngine,
            Func<Task> onPrintRequested,
            Func<Task> onViewSourceRequested)
        {
            _tabManager = tabManager;
            _defaultEngine = defaultEngine;
            _getCurrentEngine = getCurrentEngine;
            _onPrintRequested = onPrintRequested;
            _onViewSourceRequested = onViewSourceRequested;
        }

        public void ShowTabRightClickMenu(Border tabBorder, TabViewItem viewItem)
        {
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

            menu.ShowAt(tabBorder, new Point(0, tabBorder.ActualHeight));
        }

        public void ShowWebContextMenu(TabContextMenuEventArgs args)
        {
            var tab = _tabManager.CurrentTab;
            if (tab is EdgeHtmlTab) return;

            var flyout = new MenuFlyout();
            ApplyThemeToFlyout(flyout);

            // 复制
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

            // 粘贴
            var pasteItem = new MenuFlyoutItem { Text = "粘贴", IsEnabled = args.IsEditable };
            pasteItem.Click += async (_, _) =>
                await new ContentDialog { Title = "粘贴", Content = "请使用键盘快捷键 Ctrl+V 进行粘贴。", CloseButtonText = "确定" }.ShowAsync();
            flyout.Items.Add(pasteItem);

            // 全选
            var selectAllItem = new MenuFlyoutItem { Text = "全选" };
            selectAllItem.Click += async (_, _) =>
            {
                if (_tabManager.CurrentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                    await wv2.CoreWebView2.ExecuteScriptAsync("document.execCommand('selectAll');");
            };
            flyout.Items.Add(selectAllItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // 后退/前进/刷新
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

            // 链接相关
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

            // 图片相关
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

            // 打印和查看源代码
            var printItem = new MenuFlyoutItem { Text = "打印" };
            printItem.Click += async (_, _) => await _onPrintRequested?.Invoke();
            flyout.Items.Add(printItem);

            var sourceItem = new MenuFlyoutItem { Text = "查看页面源代码" };
            sourceItem.Click += async (_, _) => await _onViewSourceRequested?.Invoke();
            flyout.Items.Add(sourceItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // 开发者工具
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