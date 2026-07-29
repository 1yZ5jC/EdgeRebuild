using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Core
{
    public class TabManager
    {
        private readonly List<TabViewItem> _tabs = new List<TabViewItem>();
        private IBrowserTab _currentTab;
        private readonly Stack<(string url, string title, EngineType engine)> _closedTabs = new Stack<(string, string, EngineType)>();
        private const int MaxClosedTabs = 20;

        private readonly Dictionary<IBrowserTab, DispatcherTimer> _suspendTimers = new Dictionary<IBrowserTab, DispatcherTimer>();
        private const int SuspendDelaySeconds = 120;
        private bool _enableTabSuspend = true;

        // 事件
        public event Action<TabViewItem> TabCreated;        // 新标签创建完成，需要 MainPage 构建 UI
        public event Action<TabViewItem> TabChanged;        // 当前标签切换
        public event Action<int> TabCountChanged;           // 标签数量变化
        public event Action<TabViewItem> TabClosed;         // 标签关闭
        public event Action<IBrowserTab, bool> TabSuspendedChanged; // 挂起状态变化

        public IReadOnlyList<TabViewItem> Tabs => _tabs;
        public IBrowserTab CurrentTab => _currentTab;
        public int TabCount => _tabs.Count;
        public bool IsTabSuspendEnabled => _enableTabSuspend;

        public TabManager()
        {
            _ = LoadSettingsAsync();
        }

        private async Task LoadSettingsAsync()
        {
            var setting = await Services.SettingsManager.GetAsync("EnableTabSuspend") ?? "True";
            _enableTabSuspend = (setting == "True" || setting == "true");
        }

        public async Task<TabViewItem> CreateTabAsync(EngineType engine, string url = null)
        {
            // 创建浏览器引擎
            IBrowserTab tab = engine == EngineType.WebView2 ? new WebView2Tab() : new EdgeHtmlTab();

            // 处理 DoNotTrack
            if (tab is WebView2Tab wv2Tab)
            {
                string doNotTrack = await Services.SettingsManager.GetAsync("DoNotTrack") ?? "False";
                if (doNotTrack == "True" || doNotTrack == "true")
                {
                    await wv2Tab.EnsureInitializedAsync();
                    wv2Tab.CoreWebView2.WebResourceRequested += OnWebResourceRequestedForDNT;
                }
            }

            var viewItem = new TabViewItem
            {
                Tab = tab,
                IsSuspended = false
            };
            _tabs.Add(viewItem);

            // 触发事件，让 MainPage 构建 UI
            TabCreated?.Invoke(viewItem);

            // 导航到目标 URL
            if (!string.IsNullOrEmpty(url))
                await tab.NavigateAsync(url);
            else
                await tab.NavigateAsync("about:blank");

            TabCountChanged?.Invoke(_tabs.Count);

            // 如果当前没有活动标签，自动激活此标签
            if (_currentTab == null)
                await SwitchToTabAsync(viewItem);

            return viewItem;
        }

        public async Task CloseTabAsync(TabViewItem viewItem, bool addToClosed = true)
        {
            if (viewItem == null || !_tabs.Contains(viewItem)) return;

            string url = viewItem.Tab.CurrentUrl;
            if (addToClosed && !string.IsNullOrEmpty(url) && url != "about:blank")
            {
                _closedTabs.Push((url, viewItem.Tab.Title ?? url, viewItem.Tab.Engine));
                while (_closedTabs.Count > MaxClosedTabs)
                    _closedTabs.Pop();
            }

            _tabs.Remove(viewItem);

            // 如果当前激活的标签被关闭，切换到其他标签
            if (_currentTab == viewItem.Tab)
            {
                _currentTab = null;
                if (_tabs.Count > 0)
                    await SwitchToTabAsync(_tabs[0]);
            }

            // 清理资源
            viewItem.Tab.Dispose();
            CleanupSuspendTimer(viewItem.Tab);

            TabClosed?.Invoke(viewItem);
            TabCountChanged?.Invoke(_tabs.Count);
        }

        public async Task SwitchToTabAsync(TabViewItem viewItem)
        {
            if (viewItem == null || viewItem.Tab == _currentTab) return;

            var previousTab = _currentTab;
            _currentTab = viewItem.Tab;

            // 恢复当前标签
            if (_currentTab.IsSuspended)
                await _currentTab.ResumeAsync();

            // 挂起之前的标签
            if (_enableTabSuspend && previousTab != null && previousTab != _currentTab)
                await SuspendTabAsync(previousTab);

            TabChanged?.Invoke(viewItem);
        }

        public bool TryReopenClosedTab(out EngineType engine, out string url, out string title)
        {
            if (_closedTabs.Count > 0)
            {
                (url, title, engine) = _closedTabs.Pop();
                return true;
            }
            engine = EngineType.EdgeHtml;
            url = null;
            title = null;
            return false;
        }

        public void ClearClosedTabs() => _closedTabs.Clear();

        public async Task SuspendAllTabsAsync()
        {
            foreach (var item in _tabs)
            {
                if (item.Tab is WebView2Tab wv2 && !wv2.IsSuspended)
                    await wv2.SuspendAsync();
            }
        }

        public async Task ResumeAllTabsAsync()
        {
            foreach (var item in _tabs)
            {
                if (item.Tab.IsSuspended)
                    await item.Tab.ResumeAsync();
            }
        }

        private async Task SuspendTabAsync(IBrowserTab tab)
        {
            if (tab is WebView2Tab wv2 && !wv2.IsSuspended)
            {
                // 延迟挂起（由定时器触发）
                if (_suspendTimers.ContainsKey(tab))
                    _suspendTimers[tab].Stop();

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SuspendDelaySeconds) };
                timer.Tick += async (s, e) =>
                {
                    timer.Stop();
                    var target = tab;
                    if (target == _currentTab || target.IsSuspended)
                    {
                        _suspendTimers.Remove(target);
                        return;
                    }

                    // 检查是否有活动下载
                    bool hasActiveDownload = Services.DownloadManager.Downloads.Any(d =>
                        d.Status == Services.DownloadStatus.Downloading ||
                        d.Status == Services.DownloadStatus.Paused);
                    if (hasActiveDownload)
                    {
                        _suspendTimers.Remove(target);
                        return;
                    }

                    if (target is WebView2Tab wv2Target)
                    {
                        await wv2Target.SuspendAsync();
                        var vi = _tabs.FirstOrDefault(v => v.Tab == wv2Target);
                        if (vi != null)
                        {
                            vi.IsSuspended = true;
                            TabSuspendedChanged?.Invoke(target, true);
                        }
                    }
                    _suspendTimers.Remove(target);
                };
                _suspendTimers[tab] = timer;
                timer.Start();
            }
        }

        private void CleanupSuspendTimer(IBrowserTab tab)
        {
            if (_suspendTimers.ContainsKey(tab))
            {
                _suspendTimers[tab].Stop();
                _suspendTimers.Remove(tab);
            }
        }

        private void OnWebResourceRequestedForDNT(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestedEventArgs e)
        {
            e.Request.Headers.SetHeader("DNT", "1");
        }

        public void DisposeAll()
        {
            foreach (var item in _tabs)
                item.Tab.Dispose();
            _tabs.Clear();
            _currentTab = null;
            foreach (var timer in _suspendTimers.Values)
                timer.Stop();
            _suspendTimers.Clear();
        }
    }
}