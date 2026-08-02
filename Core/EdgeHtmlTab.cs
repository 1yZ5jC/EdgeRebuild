using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Core
{
    public class EdgeHtmlTab : IBrowserTab
    {
        private WebView _webView;
        private string _faviconUri = "";
        private string _title = "";
        private bool _httpsFailed = false;
        private bool _disposed = false;

        public static event Action<string> DownloadRequested;

        public string Id { get; } = Guid.NewGuid().ToString();
        public FrameworkElement ViewElement => _webView;
        public bool CanGoBack => _webView?.CanGoBack ?? false;
        public bool CanGoForward => _webView?.CanGoForward ?? false;
        public string CurrentUrl => _webView?.Source?.ToString() ?? "";
        public EngineType Engine => EngineType.EdgeHtml;
        public string EngineIcon => "🌐";
        public string Title => _title;
        public string FaviconUri => _faviconUri;
        public bool IsSuspended => false;

        public event Action<string> TitleChanged;
        public event Action<string> UrlChanged;
        public event Action<bool> CanGoBackChanged;
        public event Action<bool> CanGoForwardChanged;
        public event Action<string> FaviconChanged;
        public event Action<TabContextMenuEventArgs> ContextMenuRequested;
        public event Action<string> NewWindowRequested;
        public event Action<bool> LoadingChanged;
        public event Action<PageShortcutKey> PageShortcutKeyPressed;

        // 页面内快捷键桥（EdgeHTML 版）：外部网站的 window.external.notify 会被
        // ApplicationContentUriRules 白名单拦截（且不允许域通配），AddWebAllowedObject
        // 又要求独立 WinRT 组件程序集；故改用「写入全局 marker 变量 + 宿主定时轮询」：
        // 脚本只写 window.__edgKeyMarker，宿主用 InvokeScriptAsync 读取并清空。
        private const string PageKeyBridgeJs =
            "(function(){ if (window.__edgPageKeyBridge) return; window.__edgPageKeyBridge = true;"
            + " function mark(s){ window.__edgKeyMarker = s; }"
            + " window.addEventListener('keydown', function(e){"
            + " var c=e.ctrlKey, s=e.shiftKey, k=e.keyCode;"
            + " if (c && !s && k===70) { mark('cf'); e.preventDefault(); e.stopPropagation(); return; }"
            + " if (c && s && k===71) { mark('scg'); e.preventDefault(); return; }"
            + " if (c && !s && k===71) { mark('cg'); e.preventDefault(); return; }"
            + " if (!c && s && k===114) { mark('sf3'); e.preventDefault(); return; }"
            + " if (!c && !s && k===114) { mark('f3'); e.preventDefault(); return; }"
            + " if (!c && !s && k===27) { mark('esc'); return; }"
            + " }, false); })();";

        private void InjectPageKeyBridge()
        {
            _ = ExecuteScriptAsync(PageKeyBridgeJs);
        }

        /// <summary>
        /// 读取并清空页面内快捷键 marker（由 MainPage 定时轮询，仅对当前活动 EdgeHTML 标签）。
        /// </summary>
        public async Task<string> PollPageKeyAsync()
        {
            if (_disposed) return "";
            var raw = await ExecuteScriptAsync("(window.__edgKeyMarker)||''");
            if (string.IsNullOrEmpty(raw) || raw == "\"\"") return "";
            _ = ExecuteScriptAsync("window.__edgKeyMarker='';");
            return raw.Trim('"');
        }

        public Task SuspendAsync() => Task.CompletedTask;
        public Task ResumeAsync() => Task.CompletedTask;

        public EdgeHtmlTab()
        {
            _webView = new WebView();
            _webView.NavigationStarting += OnNavigationStarting;
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.DOMContentLoaded += OnDOMContentLoaded;
            _webView.NavigationFailed += OnNavigationFailed;
            _webView.NewWindowRequested += OnNewWindowRequested;
        }

        private void OnNewWindowRequested(WebView sender, WebViewNewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            NewWindowRequested?.Invoke(args.Uri?.ToString() ?? "");
        }

        private void OnNavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            LoadingChanged?.Invoke(true);

            string[] exts = { ".exe", ".zip", ".rar", ".7z", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".mp3", ".mp4", ".avi", ".mkv", ".apk", ".msi", ".tar", ".gz", ".bz2", ".dmg", ".iso" };
            string url = args.Uri?.ToString() ?? "";
            if (exts.Any(ext => url.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                args.Cancel = true;
                DownloadRequested?.Invoke(url);
                LoadingChanged?.Invoke(false);
            }
        }

        private void OnNavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            LoadingChanged?.Invoke(false);
            InjectPageKeyBridge();

            if (args.IsSuccess)
            {
                _httpsFailed = false;
                UrlChanged?.Invoke(args.Uri?.ToString() ?? "");
                UpdateTitle();
                CheckNavigationState();
                // favicon 提取由 OnDOMContentLoaded 和此处共同完成
                TryExtractFavicon();
            }
            else
            {
                // 失败时也尝试获取 favicon（可能页面有错误但仍能提取到某些图标）
                TryExtractFavicon();
            }
        }

        private void OnNavigationFailed(object sender, WebViewNavigationFailedEventArgs e)
        {
            LoadingChanged?.Invoke(false);
            // 失败时也尝试获取 favicon
            TryExtractFavicon();
        }

        private void UpdateTitle()
        {
            try
            {
                _title = _webView.DocumentTitle;
                if (string.IsNullOrWhiteSpace(_title)) _title = _webView.Source?.Host ?? "新标签页";
                TitleChanged?.Invoke(_title);
            }
            catch { }
        }

        private void OnDOMContentLoaded(WebView sender, WebViewDOMContentLoadedEventArgs args)
        {
            UpdateTitle();
            // DOM 加载完成时再尝试提取 favicon
            TryExtractFavicon();
        }

        private void CheckNavigationState()
        {
            CanGoBackChanged?.Invoke(_webView.CanGoBack);
            CanGoForwardChanged?.Invoke(_webView.CanGoForward);
        }

        private async void TryExtractFavicon()
        {
            if (_webView?.Source == null || _webView.Source.AbsoluteUri == "about:blank")
            {
                // 空白页不设置 favicon
                return;
            }

            try
            {
                // 尝试获取页面的 favicon 链接（EdgeHTML 不支持可选链，用 ES5 写法）
                var result = await _webView.InvokeScriptAsync("eval", new List<string> {
                    "(function(){ var l=document.querySelector('link[rel*=\"icon\"]'); return l?l.href:''; })()"
                });

                if (!string.IsNullOrEmpty(result) && Uri.TryCreate(result, UriKind.Absolute, out _))
                {
                    SetFaviconUri(result.Trim('"'));
                    return;
                }

                // 如果未找到，尝试使用默认路径 /favicon.ico
                string defaultFavicon = $"{_webView.Source.Scheme}://{_webView.Source.Host}/favicon.ico";
                SetFaviconUri(defaultFavicon);
            }
            catch
            {
                // 异常时忽略
            }
        }

        private void SetFaviconUri(string url)
        {
            if (url != _faviconUri && !string.IsNullOrEmpty(url))
            {
                _faviconUri = url;
                FaviconChanged?.Invoke(url);
            }
        }

        public async Task<string> ExecuteScriptAsync(string script)
        {
            // AOT 下 string[] 无法创建 CCW（InvalidCastException），需用 List<string> 传参
            try { return await _webView.InvokeScriptAsync("eval", new List<string> { script }); }
            catch (Exception ex) { Services.Diagnostics.Log($"[EdgeHTML] ExecuteScriptAsync 异常: {ex.GetType().Name}: {ex.Message}"); return ""; }
        }

        public Task NavigateAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) url = "about:blank";
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains("://"))
                url = "https://" + url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) uri = new Uri("about:blank");
            _httpsFailed = false;
            _webView.Navigate(uri);
            return Task.CompletedTask;
        }

        public Task NavigateToHtmlAsync(string html)
        {
            if (string.IsNullOrEmpty(html)) return Task.CompletedTask;
            try { _webView.NavigateToString(html); } catch { }
            return Task.CompletedTask;
        }

        public Task GoBackAsync() { _webView.GoBack(); return Task.CompletedTask; }
        public Task GoForwardAsync() { _webView.GoForward(); return Task.CompletedTask; }
        public Task RefreshAsync() { _webView.Refresh(); return Task.CompletedTask; }
        public Task StopAsync() { _webView.Stop(); return Task.CompletedTask; }

        public void Dispose()
        {
            _disposed = true;
            TitleChanged = null;
            UrlChanged = null;
            CanGoBackChanged = null;
            CanGoForwardChanged = null;
            FaviconChanged = null;
            ContextMenuRequested = null;
            NewWindowRequested = null;
            LoadingChanged = null;

            if (_webView == null) return;

            try { _webView.NewWindowRequested -= OnNewWindowRequested; } catch { }
            try { _webView.NavigationStarting -= OnNavigationStarting; } catch { }
            try { _webView.NavigationCompleted -= OnNavigationCompleted; } catch { }
            try { _webView.DOMContentLoaded -= OnDOMContentLoaded; } catch { }
            try { _webView.NavigationFailed -= OnNavigationFailed; } catch { }

            try
            {
                (_webView.Parent as Panel)?.Children.Remove(_webView);
                _webView = null;
            }
            catch { }
        }
    }
}