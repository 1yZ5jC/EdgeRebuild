using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Core
{
    public class EdgeHtmlTab : IBrowserTab
    {
        private WebView _webView;
        private string _faviconUri = "";
        private string _title = "";
        private bool _disposed = false;
        private bool _metroHome = false;
        private bool _inPrivate = false;

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
        public event Action<string> NewWindowRequested;
        public event Action<bool> LoadingChanged;
        // 快捷键/标记/朗读/NTP 搜索/右键不走事件：EdgeHTML 无消息通道，
        // 由宿主轮询 PollPageKeyAsync / PollNtpSearchAsync / PollContextMenuAsync 等 marker 读取。

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
            + " if (c && s && k===77) { mark('hm'); e.preventDefault(); return; }"
            + " if (c && s && k===83) { mark('cs'); e.preventDefault(); return; }"
            + " if (c && !s && k===84) { mark('ct'); e.preventDefault(); return; }"
            + " if (c && !s && k===87) { mark('cw'); e.preventDefault(); return; }"
            + " if (c && !s && k===76) { mark('cl'); e.preventDefault(); return; }"
            + " if (!c && !s && k===122) { mark('f11'); e.preventDefault(); return; }"
            + " }, false); })();";

        private void InjectPageKeyBridge()
        {
            _ = ExecuteScriptAsync(PageKeyBridgeJs);
        }

        /// <summary>
        /// 页面右键桥（EdgeHTML 版）：UWP WebView 无控件级右键菜单事件，
        /// 且外部站点的 window.external.notify 受白名单限制，故沿用 marker 方案——
        /// 页面监听 contextmenu 阻止默认菜单，把位置/类型写入 window.__edgCtxMarker
        /// （纯文本管道分隔：kind|x|y|encodeURIComponent(值)，与 TTS marker 同机制），
        /// 宿主定时轮询后弹出自定义菜单（与 WebView2 菜单一致）。
        /// </summary>
        private const string CtxBridgeJs =
            "(function(){ if (window.__edgCtxBridge) return; window.__edgCtxBridge = true;"
            + " window.addEventListener('contextmenu', function(e){"
            + " var el = e.target;"
            + " var img = el.closest ? el.closest('img') : null;"
            + " var a = el.closest ? el.closest('a[href]') : null;"
            + " var kind = 'page', val = '';"
            + " if (img) { kind = 'image'; val = img.currentSrc || img.src || ''; }"
            + " else if (a) { kind = 'link'; val = a.href || ''; }"
            + " else if (window.getSelection && window.getSelection().toString()) { kind = 'selection'; val = window.getSelection().toString(); }"
            + " window.__edgCtxMarker = kind + '|' + Math.round(e.clientX) + '|' + Math.round(e.clientY) + '|' + encodeURIComponent(val);"
            + " e.preventDefault();"
            + " }, true); })();";

        private void InjectContextMenuBridge()
        {
            _ = ExecuteScriptAsync(CtxBridgeJs);
        }

        /// <summary>
        /// 读取并清空页面右键 marker（由 MainPage 定时轮询，仅对当前活动 EdgeHTML 标签）。
        /// 返回 "kind|x|y|encodeURIComponent(值)" 纯文本。
        /// </summary>
        public async Task<string> PollContextMenuAsync()
        {
            if (_disposed) return "";
            var raw = await ExecuteScriptAsync("(window.__edgCtxMarker)||''");
            if (string.IsNullOrEmpty(raw) || raw == "\"\"") return "";
            _ = ExecuteScriptAsync("window.__edgCtxMarker='';");
            return raw.Trim('"');
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

        /// <summary>
        /// 读取并清空页面朗读 marker（EPUB 在 EdgeHTML 引擎下无 postMessage，
        /// 页面把 "tts:" + 文本写入 window.__edgTtsMarker，由宿主轮询转发）。
        /// </summary>
        public async Task<string> PollTtsAsync()
        {
            if (_disposed) return "";
            var raw = await ExecuteScriptAsync("(window.__edgTtsMarker)||''");
            if (string.IsNullOrEmpty(raw) || raw == "\"\"") return "";
            _ = ExecuteScriptAsync("window.__edgTtsMarker='';");
            return raw.Trim('"');
        }

        /// <summary>
        /// 读取并清空页面 NTP 搜索 marker（NTP 页面无 postMessage 时把搜索词
        /// 写入 window.__edgNtpSearch，由宿主轮询转发，与 WebView2 行为统一）。
        /// </summary>
        public async Task<string> PollNtpSearchAsync()
        {
            if (_disposed) return "";
            var raw = await ExecuteScriptAsync("(window.__edgNtpSearch)||''");
            if (string.IsNullOrEmpty(raw) || raw == "\"\"") return "";
            _ = ExecuteScriptAsync("window.__edgNtpSearch='';");
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

            string[] exts = { ".exe", ".zip", ".rar", ".7z", ".pdf", ".epub", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".mp3", ".mp4", ".avi", ".mkv", ".apk", ".msi", ".tar", ".gz", ".bz2", ".dmg", ".iso" };
            string url = args.Uri?.ToString() ?? "";
            // 仅拦截网络地址：本地阅读器自身页面（ms-appx-web reader.html?file=...&src=xxx.epub）
            // 整串以 .epub 结尾，若一并拦截会被误当成下载，导致阅读器永远打不开。
            bool isNetworkUrl = args.Uri != null
                && (args.Uri.Scheme == Uri.UriSchemeHttp || args.Uri.Scheme == Uri.UriSchemeHttps);
            if (isNetworkUrl && exts.Any(ext => url.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
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
            InjectContextMenuBridge();

            if (args.IsSuccess)
            {
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
            ShowErrorPage(e);
        }

        // ---- 导航失败 → 本地错误页(参考规范 §5.5),EdgeHTML 直接走 ms-appx-web 资源 ----
        private void ShowErrorPage(WebViewNavigationFailedEventArgs e)
        {
            if (_disposed) return;
            string url = e.Uri?.ToString() ?? "";
            if (string.IsNullOrEmpty(url)) return;
            Uri uri;
            try { uri = new Uri(url); } catch { return; }
            if (uri.Scheme != "http" && uri.Scheme != "https") return;
            if (url.Contains("/ErrorPages/error.html")) return;

            try
            {
                if (_webView == null) return;
                string page = "ms-appx-web:///Assets/ErrorPages/error.html?kind=other#" + Uri.EscapeDataString(url);
                _webView.Navigate(new Uri(page));
            }
            catch (Exception ex)
            {
                EdgeRebuild.Services.Diagnostics.Log($"[错误页] EdgeHTML 导航失败: {ex.Message}");
            }
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
            InjectPageKeyBridge();
            InjectContextMenuBridge();
            // DOM 加载完成时再尝试提取 favicon
            TryExtractFavicon();
            _ = SeedTilesFromFavoritesIfConfiguredAsync();
        }

        // P1-7：磁贴主页数据源=收藏夹时，ntpmetro 页 DOM 就绪后注入收藏磁贴
        private async Task SeedTilesFromFavoritesIfConfiguredAsync()
        {
            try
            {
                string uri = _webView?.Source?.AbsoluteUri ?? "";
                if (!uri.Contains("/ntpmetro/")) return;
                if (!await NtpTileSeeder.ShouldSeedAsync()) return;
                await ExecuteScriptAsync(NtpTileSeeder.BuildSeedScript());
            }
            catch { }
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

        public void SetMetroHome() => _metroHome = true;

        public void SetInPrivate() => _inPrivate = true;

        public async Task NavigateAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) url = "about:blank";
            // 新标签页（about:blank）→ 本地 NTP 页面（设置里可改回真空白页；EdgeHTML 无
            // prefers-color-scheme 支持，主题经查询参数显式传入）
            if (url == "about:blank")
            {
                if (_inPrivate)
                {
                    // InPrivate：隐身专属新标签页（无磁贴、无历史，防泄漏）
                    string theme = Services.ThemeManager.IsDarkTheme ? "dark" : "light";
                    url = $"ms-appx-web:///Assets/Ntp/index.html?theme={theme}&inprivate=1";
                }
                else if (_metroHome)
                {
                    // Metro IE 窗口：磁贴风格主页，深色固定主题
                    url = "ms-appx-web:///Assets/NtpMetro/index.html?theme=dark";
                }
                else
                {
                    // 主页类型（设置→常规→新标签页主页类型）：Classic 经典主页 /
                    // Metro 磁贴主页 / Blank 空白页；旧键 NewTabShowNtp 作兼容回退
                    string ntpType = "Classic";
                    try
                    {
                        string v = await Services.SettingsManager.GetAsync("NewTabPageType");
                        if (!string.IsNullOrEmpty(v)) ntpType = v;
                        else if ((await Services.SettingsManager.GetAsync("NewTabShowNtp")) == "False") ntpType = "Blank";
                    }
                    catch { }
                    if (ntpType == "Metro")
                    {
                        url = "ms-appx-web:///Assets/NtpMetro/index.html?theme=dark";
                    }
                    else if (ntpType != "Blank")
                    {
                        string theme = Services.ThemeManager.IsDarkTheme ? "dark" : "light";
                        url = $"ms-appx-web:///Assets/Ntp/index.html?theme={theme}";
                    }
                }
            }
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains("://"))
                url = "https://" + url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) uri = new Uri("about:blank");
            _webView.Navigate(uri);
        }

        public Task NavigateToHtmlAsync(string html)
        {
            if (string.IsNullOrEmpty(html)) return Task.CompletedTask;
            try { _webView.NavigateToString(html); } catch { }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 用 ms-local-stream 打开本地 EPUB 阅读器。阅读器页面、脚本与 EPUB 同源，
        /// 规避 EdgeHTML 对 ms-appx-web → ms-appdata 的 XHR 封锁。
        /// </summary>
        public Task NavigateToEpubReaderAsync(string epubPath, string sourceUrl)
        {
            if (string.IsNullOrEmpty(epubPath) || !File.Exists(epubPath))
            {
                Services.Diagnostics.Log($"[EPUB 阅读器] 缓存文件不存在: {epubPath}");
                return Task.CompletedTask;
            }
            try
            {
                string assetDir = Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "EpubReader");
                string relative = "reader.html?file=" + ReaderStreamResolver.VirtualEpubName;
                if (!string.IsNullOrEmpty(sourceUrl))
                    relative += "&src=" + Uri.EscapeDataString(sourceUrl);
                var uri = _webView.BuildLocalStreamUri("epubreader", relative);
                Services.Diagnostics.Log($"[EPUB 阅读器] 本地流地址: {uri}");
                _webView.NavigateToLocalStreamUri(uri, new ReaderStreamResolver(assetDir, epubPath));
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Log($"[EPUB 阅读器] 本地流导航失败: {ex.Message}");
            }
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