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
        private readonly Action<TabViewItem> _onTogglePin;
        private readonly Func<Task> _onWebCapture;

        public TabContextMenuManager(
            TabManager tabManager,
            EngineType defaultEngine,
            Func<EngineType> getCurrentEngine,
            Func<Task> onPrintRequested,
            Func<Task> onViewSourceRequested,
            Action<TabViewItem> onTogglePin = null,
            Func<Task> onWebCapture = null)
        {
            _tabManager = tabManager;
            _defaultEngine = defaultEngine;
            _getCurrentEngine = getCurrentEngine;
            _onPrintRequested = onPrintRequested;
            _onViewSourceRequested = onViewSourceRequested;
            _onTogglePin = onTogglePin;
            _onWebCapture = onWebCapture;
        }

        // 搜索项文案带当前引擎名；引擎名异步读取，菜单弹出后原地更新
        private static async Task LoadSearchItemLabelAsync(MenuFlyoutItem item)
        {
            try
            {
                string engineName = await Services.SettingsManager.GetAsync("SearchEngine") ?? "Google";
                item.Text = $"用 {engineName} 搜索";
            }
            catch { }
        }

        public void ShowTabRightClickMenu(Border tabBorder, TabViewItem viewItem)        {
            if (viewItem == null) return;

            var menu = new MenuFlyout();
            ApplyThemeToFlyout(menu);

            // ---- 新建标签页 ----
            var newTabItem = new MenuFlyoutItem { Text = "新建标签页" };
            newTabItem.Click += async (_, _) => await _tabManager.CreateTabAsync(_defaultEngine, "about:blank");
            menu.Items.Add(newTabItem);

            // ---- 重新加载 ----
            var reloadItem = new MenuFlyoutItem { Text = "重新加载" };
            reloadItem.Click += (_, _) => viewItem.Tab?.RefreshAsync();
            menu.Items.Add(reloadItem);

            // ---- 关闭标签页 ----
            var closeItem = new MenuFlyoutItem { Text = "关闭标签页" };
            closeItem.Click += (_, _) => _ = _tabManager.CloseTabAsync(viewItem);
            menu.Items.Add(closeItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            // ---- 固定/取消固定 ----
            if (_onTogglePin != null)
            {
                var pinItem = new MenuFlyoutItem
                {
                    Text = viewItem.IsPinned ? "取消固定" : "固定标签"
                };
                pinItem.Click += (_, _) => _onTogglePin?.Invoke(viewItem);
                menu.Items.Add(pinItem);
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            // ---- 关闭其他标签页 ----
            var closeOthersItem = new MenuFlyoutItem { Text = "关闭其他标签页" };
            closeOthersItem.Click += (_, _) => CloseOtherTabs(viewItem);
            menu.Items.Add(closeOthersItem);

            // ---- 关闭右侧标签页 ----
            var closeRightItem = new MenuFlyoutItem { Text = "关闭右侧标签页" };
            closeRightItem.Click += (_, _) => CloseTabsToRight(viewItem);
            menu.Items.Add(closeRightItem);

            menu.ShowAt(tabBorder, new Point(0, tabBorder.ActualHeight));
        }

        public void ShowWebContextMenu(TabContextMenuEventArgs args)
        {
            var tab = _tabManager.CurrentTab;

            var flyout = new MenuFlyout();
            ApplyThemeToFlyout(flyout);

            // ---- 复制 ----
            // 启用规则：链接右键场景用"复制链接"（此处分组禁用）；图片/选区/页面场景
            // 均可复制。PDF 查看器等场景下 HasSelection 不可靠（常为 false），
            // 因此点击时实时取选区，取不到时给出提示。
            var copyItem = new MenuFlyoutItem
            {
                Text = args.MenuType == ContextMenuType.Image ? "复制图片" : "复制",
                IsEnabled = args.MenuType != ContextMenuType.Link
            };
            copyItem.Click += async (_, _) =>
            {
                // 图片右键：复制图片本身到剪贴板
                if (args.MenuType == ContextMenuType.Image && !string.IsNullOrEmpty(args.ImageUrl))
                {
                    if (await CopyImageToClipboardAsync(args.ImageUrl))
                        NotificationService.ShowToast("已复制", "图片已复制到剪贴板");
                    else
                        NotificationService.ShowToast("复制失败", "无法获取图片");
                    return;
                }

                string selectedText = args.HasSelection ? (args.SelectionText ?? "") : "";
                if (string.IsNullOrEmpty(selectedText))
                {
                    if (_tabManager.CurrentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                    {
                        string json = await wv2.CoreWebView2.ExecuteScriptAsync(
                            "window.getSelection() ? window.getSelection().toString() : ''");
                        try { selectedText = System.Text.Json.JsonDocument.Parse(json).RootElement.GetString() ?? ""; }
                        catch { }
                    }
                    else if (_tabManager.CurrentTab is EdgeHtmlTab edge)
                    {
                        string json = await edge.ExecuteScriptAsync(
                            "window.getSelection() ? window.getSelection().toString() : ''");
                        try { selectedText = System.Text.Json.JsonDocument.Parse(json).RootElement.GetString() ?? ""; }
                        catch { }
                    }
                }
                if (string.IsNullOrEmpty(selectedText))
                {
                    NotificationService.ShowToast("无法复制", "页面没有可复制的文本");
                    return;
                }
                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(selectedText);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            };
            flyout.Items.Add(copyItem);

            // ---- 用搜索引擎搜索选中内容（旧版 Edge 交互：选区右键直达搜索，新标签打开） ----
            if (args.MenuType == ContextMenuType.Selection && !string.IsNullOrEmpty(args.SelectionText))
            {
                string searchText = args.SelectionText;
                var searchItem = new MenuFlyoutItem { Text = "搜索选中内容" };
                searchItem.Click += async (_, _) =>
                {
                    string engineName = "Google";
                    try { engineName = await Services.SettingsManager.GetAsync("SearchEngine") ?? "Google"; } catch { }
                    await _tabManager.CreateTabAsync(
                        _defaultEngine,
                        Services.UrlInputResolver.BuildSearchUrl(searchText, engineName));
                };
                _ = LoadSearchItemLabelAsync(searchItem);
                flyout.Items.Add(searchItem);
            }

            // ---- 粘贴 ----
            var pasteItem = new MenuFlyoutItem { Text = "粘贴", IsEnabled = args.IsEditable };
            pasteItem.Click += async (_, _) =>
                await new ContentDialog { Title = "粘贴", Content = "请使用键盘快捷键 Ctrl+V 进行粘贴。", CloseButtonText = "确定" }.ShowAsync();
            flyout.Items.Add(pasteItem);

            // ---- 全选 ----
            var selectAllItem = new MenuFlyoutItem { Text = "全选" };
            selectAllItem.Click += async (_, _) =>
            {
                if (_tabManager.CurrentTab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                    await wv2.CoreWebView2.ExecuteScriptAsync("document.execCommand('selectAll');");
                else if (_tabManager.CurrentTab is EdgeHtmlTab edge)
                    await edge.ExecuteScriptAsync("document.execCommand('selectAll');");
            };
            flyout.Items.Add(selectAllItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // ---- 标记高亮（选中文本时） ----
            if (args.HasSelection)
            {
                var markSubMenu = new MenuFlyoutSubItem { Text = "标记高亮" };
                foreach (var (name, color) in PageMarkManager.MarkColors)
                {
                    string c = color;
                    var colorItem = new MenuFlyoutItem { Text = name };
                    colorItem.Click += async (_, _) =>
                    {
                        var mark = await PageMarkManager.MarkSelectionAsync(_tabManager.CurrentTab, c);
                        if (mark == null)
                            NotificationService.ShowToast("标记高亮", "请先选中网页中的文字");
                    };
                    markSubMenu.Items.Add(colorItem);
                }
                flyout.Items.Add(markSubMenu);
            }

            // ---- 清除本页标记 ----
            var clearMarksItem = new MenuFlyoutItem { Text = "清除本页标记" };
            clearMarksItem.Click += async (_, _) => await PageMarkManager.ClearPageAsync(_tabManager.CurrentTab);
            flyout.Items.Add(clearMarksItem);

            // ---- 稍后读（旧版 Edge 阅读列表：一次性阅读队列） ----
            var readLaterItem = new MenuFlyoutItem { Text = "添加到稍后读" };
            readLaterItem.Click += async (_, _) =>
            {
                var tab = _tabManager.CurrentTab;
                if (tab == null || string.IsNullOrEmpty(tab.CurrentUrl) || tab.CurrentUrl == "about:blank")
                {
                    NotificationService.ShowToast("稍后读", "当前页面不可添加");
                    return;
                }
                await ReadingListManager.Instance.AddAsync(tab.Title ?? tab.CurrentUrl, tab.CurrentUrl);
                NotificationService.ShowToast("已加入稍后读", tab.Title ?? tab.CurrentUrl);
            };
            flyout.Items.Add(readLaterItem);

            // ---- 网页捕获（截图批注） ----
            var captureItem = new MenuFlyoutItem { Text = "网页捕获" };
            captureItem.Click += async (_, _) => { if (_onWebCapture != null) await _onWebCapture(); };
            flyout.Items.Add(captureItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // ---- 后退 ----
            var backItem = new MenuFlyoutItem { Text = "后退", IsEnabled = args.CanGoBack };
            backItem.Click += (_, _) => _tabManager.CurrentTab?.GoBackAsync();
            flyout.Items.Add(backItem);

            // ---- 前进 ----
            var forwardItem = new MenuFlyoutItem { Text = "前进", IsEnabled = args.CanGoForward };
            forwardItem.Click += (_, _) => _tabManager.CurrentTab?.GoForwardAsync();
            flyout.Items.Add(forwardItem);

            // ---- 刷新 ----
            var refreshItem = new MenuFlyoutItem { Text = "刷新" };
            refreshItem.Click += (_, _) => _tabManager.CurrentTab?.RefreshAsync();
            flyout.Items.Add(refreshItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // ---- 链接相关 ----
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

            // ---- 图片相关 ----
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

            // ---- 打印 ----
            // ---- 打印（EdgeHTML 无打印 API，仅 WebView2 提供） ----
            if (_tabManager.CurrentTab is WebView2Tab)
            {
                var printItem = new MenuFlyoutItem { Text = "打印" };
                printItem.Click += async (_, _) => await _onPrintRequested?.Invoke();
                flyout.Items.Add(printItem);
            }

            // ---- 查看源代码 ----
            var sourceItem = new MenuFlyoutItem { Text = "查看页面源代码" };
            sourceItem.Click += async (_, _) => await _onViewSourceRequested?.Invoke();
            flyout.Items.Add(sourceItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // ---- 开发者工具 ----
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

        /// <summary>
        /// 下载图片字节并写入剪贴板（WebView2/EdgeHTML 均无直接的"复制图片"API，
        /// 统一走下载 + SetBitmap）。
        /// </summary>
        private static async Task<bool> CopyImageToClipboardAsync(string url)
        {
            try
            {
                using var client = new Windows.Web.Http.HttpClient();
                var response = await client.GetAsync(new Uri(url));
                if (!response.IsSuccessStatusCode) return false;
                var stream = await response.Content.ReadAsInputStreamAsync();
                var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await Windows.Storage.Streams.RandomAccessStream.CopyAsync(stream, ms);
                ms.Seek(0);
                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(ms));
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                return true;
            }
            catch { return false; }
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