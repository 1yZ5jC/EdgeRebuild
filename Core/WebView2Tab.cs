using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using EdgeRebuild.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Core
{
    public class WebView2Tab : IBrowserTab
    {
        private WebView2 _webView;
        private SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
        private string _faviconUri = "";
        private string _title = "";
        private bool _isSuspended = false;
        private string _suspendedUrl = "";
        private bool _httpsFailed = false;
        private bool _disposed = false;

        public string Id { get; } = Guid.NewGuid().ToString();
        public FrameworkElement ViewElement => _webView;
        public bool CanGoBack => _webView?.CoreWebView2?.CanGoBack ?? false;  // 空检查
        public bool CanGoForward => _webView?.CoreWebView2?.CanGoForward ?? false;
        public string CurrentUrl => _isSuspended ? _suspendedUrl : (_webView?.Source?.ToString() ?? "");  // 空检查
        public EngineType Engine => EngineType.WebView2;
        public string EngineIcon => "🧬";
        public string Title => _title;
        public string FaviconUri => _faviconUri;
        public bool IsSuspended => _isSuspended;

        public CoreWebView2 CoreWebView2 => _webView?.CoreWebView2;

        public event Action<string> TitleChanged;
        public event Action<string> UrlChanged;
        public event Action<bool> CanGoBackChanged;
        public event Action<bool> CanGoForwardChanged;
        public event Action<string> FaviconChanged;
        public event Action<TabContextMenuEventArgs> ContextMenuRequested;
        public event Action<string> NewWindowRequested;
        public event Action<string, StorageFolder, string> DownloadRequested;
        public event Action<bool> LoadingChanged;
        public event Action<PageShortcutKey> PageShortcutKeyPressed;
        public event Action<string> PageMarkActionRequested;   // 页面标记动作回传（如 "remove:3"）

        // 页面内快捷键桥：焦点在网页内容内时键击被引擎吞掉、宿主收不到，
        // 注入监听脚本经 window.chrome.webview.postMessage 回传（ES5，双引擎通用）。
        private const string PageKeyBridgeJs =
            "(function(){ if (window.__edgPageKeyBridge) return; window.__edgPageKeyBridge = true;"
            + " function send(s){ try { window.chrome.webview.postMessage(s); } catch(e){ try { window.external.notify(s); } catch(e2){} } }"
            + " window.addEventListener('keydown', function(e){"
            + " var c=e.ctrlKey, s=e.shiftKey, k=e.keyCode;"
            + " if (c && !s && k===70) { send('cf'); e.preventDefault(); e.stopPropagation(); return; }"
            + " if (c && s && k===71) { send('scg'); e.preventDefault(); return; }"
            + " if (c && !s && k===71) { send('cg'); e.preventDefault(); return; }"
            + " if (!c && s && k===114) { send('sf3'); e.preventDefault(); return; }"
            + " if (!c && !s && k===114) { send('f3'); e.preventDefault(); return; }"
            + " if (!c && !s && k===27) { send('esc'); return; }"
            + " if (c && s && k===77) { send('hm'); e.preventDefault(); return; }"
            + " if (c && s && k===83) { send('cs'); e.preventDefault(); return; }"
            + " }, false); })();";

        private static PageShortcutKey? ParsePageKey(string msg)
        {
            switch (msg)
            {
                case "cf": return PageShortcutKey.Find;
                case "f3":
                case "cg": return PageShortcutKey.FindNext;
                case "sf3":
                case "scg": return PageShortcutKey.FindPrevious;
                case "esc": return PageShortcutKey.CloseFind;
                default: return null;
            }
        }

        public WebView2Tab()
        {
            _webView = new WebView2();
            _webView.NavigationCompleted += OnNavigationCompleted;
        }

        public async Task EnsureInitializedAsync()
        {
            if (_webView.CoreWebView2 != null || _disposed) return;
            await _initLock.WaitAsync();
            try
            {
                if (_webView.CoreWebView2 != null || _disposed) return;
                await _webView.EnsureCoreWebView2Async();
                if (_disposed) return;
                _webView.CoreWebView2.DocumentTitleChanged += OnDocumentTitleChanged;
                _webView.CoreWebView2.HistoryChanged += OnHistoryChanged;
                _webView.CoreWebView2.ContextMenuRequested += OnContextMenuRequested;
                _webView.CoreWebView2.DownloadStarting += OnDownloadStarting;
                _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
                _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                _ = _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(PageKeyBridgeJs);
                // UWP 版 WebView2 无 IsFindEnabled（WinUI3 专有），
                // 禁用浏览器加速键以免 Ctrl+F 弹出原生查找条；
                // Ctrl+F/Ctrl+P/Ctrl+R 由应用层快捷键接管，F5 已在 ShortcutManager 注册。
                _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                SetupLocalViewerHosts();
                CheckNavigationState();
            }
            finally { _initLock.Release(); }
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            NewWindowRequested?.Invoke(e.Uri);
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            LoadingChanged?.Invoke(true);

            // PDF/EPUB 导航：Chromium 内置 PDF 插件会直接接管（不触发 DownloadStarting），
            // 必须在此拦截，经本地 pdf.js 查看器 / epubjs 阅读器渲染。
            if (IsViewerDocUrl(e.Uri))
            {
                e.Cancel = true;
                _ = OpenLocalViewerAsync(e.Uri.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "pdf" : "epub", e.Uri);
            }
        }

        private static bool IsViewerDocUrl(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return false;
            try
            {
                // 本地查看器域（虚拟主机）不拦截：viewer.html 的 file 参数里编码了
                // contentcache.local 的 .pdf/.epub 路径，URL 整体以 .pdf 结尾会误判。
                var host = new Uri(uri).Host;
                if (host.EndsWith("pdfviewer.local", StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith("epubreader.local", StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith("contentcache.local", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            catch { return false; }
            return uri.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                || uri.EndsWith(".epub", StringComparison.OrdinalIgnoreCase);
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string msg = e.TryGetWebMessageAsString() ?? "";
            if (msg.StartsWith("edgmark:", StringComparison.Ordinal))
            {
                PageMarkActionRequested?.Invoke(msg.Substring("edgmark:".Length));
                return;
            }
            var key = ParsePageKey(msg);
            if (key.HasValue)
                PageShortcutKeyPressed?.Invoke(key.Value);
        }

        public async Task SuspendAsync()
        {
            if (_isSuspended || _disposed) return;
            if (_webView.CoreWebView2 == null)
            {
                _suspendedUrl = _webView.Source?.ToString() ?? "";
                _isSuspended = true;
                return;
            }
            try
            {
                _suspendedUrl = _webView.Source?.ToString() ?? "";
                var result = await _webView.CoreWebView2.TrySuspendAsync();
                _isSuspended = result;
            }
            catch { _isSuspended = false; }
        }

        public async Task ResumeAsync()
        {
            if (!_isSuspended || _disposed) return;
            _isSuspended = false;
            if (_webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.Resume();
            }
            else if (!string.IsNullOrEmpty(_suspendedUrl))
            {
                await NavigateAsync(_suspendedUrl);
            }
        }

        /// <summary>
        /// 本地静态资源托管：pdf.js 查看器 / epubjs 阅读器 / 文档缓存（同源加载免 CORS）。
        /// </summary>
        private void SetupLocalViewerHosts()
        {
            try
            {
                var basePath = Package.Current.InstalledLocation.Path;
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "pdfviewer.local",
                    Path.Combine(basePath, "Assets", "PdfViewer"),
                    CoreWebView2HostResourceAccessKind.Allow);
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "epubreader.local",
                    Path.Combine(basePath, "Assets", "EpubReader"),
                    CoreWebView2HostResourceAccessKind.Allow);
                var cacheDir = Path.Combine(ApplicationData.Current.LocalFolder.Path, "contentcache");
                Directory.CreateDirectory(cacheDir);
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "contentcache.local",
                    cacheDir,
                    CoreWebView2HostResourceAccessKind.Allow);
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[本地查看器] 虚拟主机映射失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 下载文档到本地缓存，再经本地查看器/阅读器打开。
        /// 用 Windows.Web.Http（UWP 原生网络栈，与 WebView2/系统代理一致），
        /// 比 System.Net.Http 在 UWP 沙箱中可靠。
        /// </summary>
        private async Task OpenLocalViewerAsync(string kind, string url)
        {
            try
            {
                using var client = new Windows.Web.Http.HttpClient();
                var response = await client.GetAsync(new Uri(url));
                response.EnsureSuccessStatusCode();
                var buffer = await response.Content.ReadAsBufferAsync();
                byte[] bytes = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);
                string ext = kind == "pdf" ? ".pdf" : ".epub";
                var filePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "contentcache",
                    $"doc_{DateTime.Now:yyyyMMdd_HHmmss_fff}{ext}");
                await File.WriteAllBytesAsync(filePath, bytes);
                string page = kind == "pdf" ? "viewer.html" : "reader.html";
                string viewerHost = kind == "pdf" ? "pdfviewer.local" : "epubreader.local";
                _webView.CoreWebView2?.Navigate(
                    $"https://{viewerHost}/{page}?file=https://contentcache.local/{Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[本地查看器] 打开 {kind} 失败: {ex}");
                try { NotificationService.ShowToast("打开失败", ex.Message); } catch { }
            }
        }

        private static bool IsPdfLike(CoreWebView2DownloadOperation op)
        {
            return (op.MimeType?.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) == true)
                || (op.Uri?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true);
        }

        private static bool IsEpubLike(CoreWebView2DownloadOperation op)
        {
            return (op.MimeType?.Equals("application/epub+zip", StringComparison.OrdinalIgnoreCase) == true)
                || (op.Uri?.EndsWith(".epub", StringComparison.OrdinalIgnoreCase) == true);
        }

        private static bool IsLocalCacheUrl(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return false;
            try
            {
                var host = new Uri(uri).Host;
                return host.EndsWith("pdfviewer.local", StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith("epubreader.local", StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith("contentcache.local", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Content-Disposition 判断：无头/inline 视为"在浏览器内查看"，attachment 视为"必须下载"。
        /// </summary>
        private static bool IsAttachmentDisposition(string disposition)
        {
            if (string.IsNullOrWhiteSpace(disposition)) return false;
            return disposition.IndexOf("inline", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private async void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            // PDF（inline）与 EPUB 下载：取消下载，经本地查看器/阅读器渲染。
            // 本地查看器域（contentcache 等）的下载不拦截，走正常下载流程（如查看器内"保存"）。
            if (!IsLocalCacheUrl(e.DownloadOperation.Uri)
                && !e.DownloadOperation.Uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
                && ((IsPdfLike(e.DownloadOperation) && !IsAttachmentDisposition(e.DownloadOperation.ContentDisposition))
                    || IsEpubLike(e.DownloadOperation)))
            {
                e.Cancel = true;
                _ = OpenLocalViewerAsync(
                    e.DownloadOperation.MimeType?.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase) == true
                        || e.DownloadOperation.Uri?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true
                        ? "pdf" : "epub",
                    e.DownloadOperation.Uri);
                return;
            }

            var deferral = e.GetDeferral();
            try
            {
                var op = e.DownloadOperation;
                e.Handled = true;

                var existing = Services.DownloadManager.FindCompletedByFileNameSync(Path.GetFileName(op.ResultFilePath), op.Uri);
                if (existing != null)
                {
                    e.Cancel = true;
                    try
                    {
                        var file = await StorageFile.GetFileFromPathAsync(existing.FullPath);
                        await Windows.System.Launcher.LaunchFileAsync(file);
                    }
                    catch { }
                    return;
                }

                bool askBeforeDownload = false;
                try { askBeforeDownload = (await Services.SettingsManager.GetAsync("AskBeforeDownload") == "True"); } catch { }

                if (askBeforeDownload)
                {
                    StorageFolder defaultFolder = null;
                    try { defaultFolder = await Services.DownloadManager.GetDownloadFolderAsync(); } catch { }

                    var dialog = new Controls.DownloadDialog(
                        Path.GetFileName(op.ResultFilePath),
                        op.Uri,
                        defaultFolder,
                        op.TotalBytesToReceive > 0 ? (long?)op.TotalBytesToReceive : null);
                    dialog.XamlRoot = ViewElement?.XamlRoot;

                    var result = await dialog.ShowAsync();
                    if (result != ContentDialogResult.Primary)
                    {
                        e.Cancel = true;
                        return;
                    }

                    if (!Controls.DownloadDialog.TryValidateFileName(dialog.FileName, out string safeName, out string error))
                    {
                        e.Cancel = true;
                        await new ContentDialog { Title = "文件名无效", Content = error, CloseButtonText = "确定", XamlRoot = ViewElement?.XamlRoot }.ShowAsync();
                        return;
                    }

                    string folderPath = dialog.SelectedFolder?.Path ?? defaultFolder?.Path ?? "";
                    string fullPath = Path.Combine(folderPath, safeName);
                    if (File.Exists(fullPath))
                    {
                        string ext = Path.GetExtension(safeName);
                        string name = Path.GetFileNameWithoutExtension(safeName);
                        int counter = 1;
                        while (File.Exists(fullPath = Path.Combine(folderPath, $"{name} ({counter++}){ext}"))) ;
                    }
                    e.ResultFilePath = fullPath;
                    var item = await Services.DownloadManager.AddAsync(op.Uri, fullPath, Path.GetFileName(fullPath));
                    item.WebViewOperation = op;
                    item.TotalBytesToReceive = op.TotalBytesToReceive;
                    Services.DownloadManager.StartDownloadOperation(item);
                }
                else
                {
                    string fileName = Path.GetFileName(op.ResultFilePath);
                    StorageFolder folder = await Services.DownloadManager.GetDownloadFolderAsync();
                    string finalPath = Path.Combine(folder.Path, fileName);
                    if (File.Exists(finalPath))
                    {
                        string ext = Path.GetExtension(fileName);
                        string name = Path.GetFileNameWithoutExtension(fileName);
                        int counter = 1;
                        while (File.Exists(finalPath = Path.Combine(folder.Path, $"{name} ({counter++}){ext}"))) ;
                    }
                    e.ResultFilePath = finalPath;
                    var item = await Services.DownloadManager.AddAsync(op.Uri, finalPath, Path.GetFileName(finalPath));
                    item.WebViewOperation = op;
                    item.TotalBytesToReceive = op.TotalBytesToReceive;
                    Services.DownloadManager.StartDownloadOperation(item);
                }
            }
            catch (Exception ex)
            {
                e.Cancel = true;
                System.Diagnostics.Debug.WriteLine($"下载处理失败: {ex.Message}");
            }
            finally { deferral.Complete(); }
        }

        private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_isSuspended || _disposed) return;
            LoadingChanged?.Invoke(false);

            if (e.IsSuccess)
            {
                _httpsFailed = false;
                UrlChanged?.Invoke(sender.Source?.ToString() ?? "");
                UpdateTitleFromDocument();
                CheckNavigationState();
                ExtractFavicon();
            }
            else
            {
                string currentUrl = sender.Source?.ToString() ?? "";
                UrlChanged?.Invoke(currentUrl);
            }
        }

        private void OnDocumentTitleChanged(object sender, object e) => UpdateTitleFromDocument();
        private void UpdateTitleFromDocument()
        {
            string newTitle = _webView.CoreWebView2?.DocumentTitle ?? "";
            if (string.IsNullOrWhiteSpace(newTitle)) newTitle = _webView.Source?.Host ?? "新标签页";
            if (newTitle != _title) { _title = newTitle; TitleChanged?.Invoke(_title); }
        }
        private void OnHistoryChanged(object sender, object e) => CheckNavigationState();
        private void CheckNavigationState()
        {
            if (_webView.CoreWebView2 != null)
            {
                CanGoBackChanged?.Invoke(_webView.CoreWebView2.CanGoBack);
                CanGoForwardChanged?.Invoke(_webView.CoreWebView2.CanGoForward);
            }
        }

        private async void ExtractFavicon()
        {
            if (_webView.CoreWebView2 == null || _webView.Source?.AbsoluteUri == "about:blank") return;
            try
            {
                var result = await _webView.CoreWebView2.ExecuteScriptAsync("(function(){ var l=document.querySelector('link[rel*=\"icon\"]'); return l?l.href:''; })()");
                if (!string.IsNullOrEmpty(result)) ProcessFaviconUrl(result.Trim('"'));
            }
            catch { }
        }

        private void ProcessFaviconUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out _) || url == _faviconUri) return;
            _faviconUri = url; FaviconChanged?.Invoke(url);
        }

        private void OnContextMenuRequested(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            var args = new TabContextMenuEventArgs
            {
                CanGoBack = _webView.CoreWebView2.CanGoBack,
                CanGoForward = _webView.CoreWebView2.CanGoForward,
                Location = new Windows.Foundation.Point(e.Location.X, e.Location.Y),
                MenuType = ContextMenuType.Page
            };
            var target = e.ContextMenuTarget;
            if (target != null)
            {
                try
                {
                    // 已知 WebView2 问题：ContextMenuTarget 的属性（Kind/LinkUri/
                    // SelectionText 等）在页面导航/目标失效时会抛
                    // InvalidOperationException (0x8000000E)，整体防护，失败降级为普通菜单
                    if (target.Kind == CoreWebView2ContextMenuTargetKind.Image) { args.MenuType = ContextMenuType.Image; args.ImageUrl = target.SourceUri; }
                    else if (!string.IsNullOrEmpty(target.LinkUri)) { args.MenuType = ContextMenuType.Link; args.LinkUrl = target.LinkUri; }
                    args.HasSelection = target.HasSelection;
                    try { args.SelectionText = target.SelectionText ?? ""; } catch { args.SelectionText = ""; }
                    args.IsEditable = target.IsEditable;
                }
                catch
                {
                    args.HasSelection = false;
                    args.SelectionText = "";
                    args.IsEditable = false;
                }
            }
            ContextMenuRequested?.Invoke(args);
            e.Handled = true;
        }

        public async Task<string> ExecuteScriptAsync(string script) => (_webView?.CoreWebView2 != null) ? await _webView.CoreWebView2.ExecuteScriptAsync(script) : "";

        public async Task NavigateAsync(string url)
        {
            if (_disposed) return;
            if (string.IsNullOrWhiteSpace(url)) url = "about:blank";

            try
            {
                await EnsureInitializedAsync();
                if (_webView.CoreWebView2 == null)
                {
                    LoadingChanged?.Invoke(false);
                    return;
                }

                _httpsFailed = false;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    string candidate = "https://" + url;
                    if (!Uri.TryCreate(candidate, UriKind.Absolute, out uri))
                        uri = new Uri("about:blank");
                }
                _webView.CoreWebView2.Navigate(uri.AbsoluteUri);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2 导航异常: {ex.Message}");
                LoadingChanged?.Invoke(false);
                UrlChanged?.Invoke($"error://{url}?error={Uri.EscapeDataString(ex.Message)}");
            }
        }

        public async Task NavigateToHtmlAsync(string html)
        {
            if (_disposed || string.IsNullOrEmpty(html)) return;
            try
            {
                await EnsureInitializedAsync();
                _webView.CoreWebView2?.NavigateToString(html);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2 NavigateToString 异常: {ex.Message}");
            }
        }

        public async Task GoBackAsync() { await EnsureInitializedAsync(); _webView.CoreWebView2?.GoBack(); }
        public async Task GoForwardAsync() { await EnsureInitializedAsync(); _webView.CoreWebView2?.GoForward(); }
        public Task RefreshAsync() { _webView.CoreWebView2?.Reload(); return Task.CompletedTask; }
        public Task StopAsync() { _webView.CoreWebView2?.Stop(); return Task.CompletedTask; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _initLock?.Dispose();
            if (_webView == null) return;

            // 解除事件
            _webView.NavigationCompleted -= OnNavigationCompleted;
            if (_webView.CoreWebView2 != null)
            {
                try
                {
                    _webView.CoreWebView2.DocumentTitleChanged -= OnDocumentTitleChanged;
                    _webView.CoreWebView2.HistoryChanged -= OnHistoryChanged;
                    _webView.CoreWebView2.ContextMenuRequested -= OnContextMenuRequested;
                    _webView.CoreWebView2.DownloadStarting -= OnDownloadStarting;
                    _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
                    _webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                }
                catch { /* 忽略 */ }
            }

            try
            {
                (_webView.Parent as Panel)?.Children.Remove(_webView);
                _webView.Close();
            }
            catch { /* 忽略 */ }
            _webView = null;

            TitleChanged = null;
            UrlChanged = null;
            CanGoBackChanged = null;
            CanGoForwardChanged = null;
            FaviconChanged = null;
            ContextMenuRequested = null;
            NewWindowRequested = null;
            DownloadRequested = null;
            LoadingChanged = null;
        }
    }
}