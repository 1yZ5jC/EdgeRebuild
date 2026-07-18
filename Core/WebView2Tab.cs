using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
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

        public string Id { get; } = Guid.NewGuid().ToString();
        public FrameworkElement ViewElement => _webView;
        public bool CanGoBack => _webView.CoreWebView2?.CanGoBack ?? false;
        public bool CanGoForward => _webView.CoreWebView2?.CanGoForward ?? false;
        public string CurrentUrl => _webView.Source?.ToString() ?? "";
        public EngineType Engine => EngineType.WebView2;
        public string EngineIcon => "🧬";
        public string Title => _title;
        public string FaviconUri => _faviconUri;

        public CoreWebView2 CoreWebView2 => _webView?.CoreWebView2;

        public event Action<string> TitleChanged;
        public event Action<string> UrlChanged;
        public event Action<bool> CanGoBackChanged;
        public event Action<bool> CanGoForwardChanged;
        public event Action<string> FaviconChanged;
        public event Action<TabContextMenuEventArgs> ContextMenuRequested;

        // 静态事件，用于通知 MainPage 启动隐蔽下载（询问后自定义路径）
        public static event Action<string, StorageFolder, string> DownloadRequested;

        public WebView2Tab()
        {
            _webView = new WebView2();
            _webView.NavigationCompleted += OnNavigationCompleted;
        }

        public async Task EnsureInitializedAsync()
        {
            if (_webView.CoreWebView2 != null) return;
            await _initLock.WaitAsync();
            try
            {
                if (_webView.CoreWebView2 != null) return;
                await _webView.EnsureCoreWebView2Async();

                _webView.CoreWebView2.DocumentTitleChanged += OnDocumentTitleChanged;
                _webView.CoreWebView2.HistoryChanged += OnHistoryChanged;
                _webView.CoreWebView2.ContextMenuRequested += OnContextMenuRequested;
                _webView.CoreWebView2.DownloadStarting += OnDownloadStarting;

                CheckNavigationState();
            }
            finally { _initLock.Release(); }
        }

        // ========== 下载拦截 ==========
        private async void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            var op = e.DownloadOperation;
            e.Handled = true;               // 阻止默认下载 UI

            string url = op.Uri ?? "";
            string fileName = Path.GetFileName(op.ResultFilePath);

            bool askBeforeDownload = false;
            try
            {
                string askSetting = await Services.SettingsManager.GetAsync("AskBeforeDownload");
                askBeforeDownload = (askSetting == "True" || askSetting == "true");
            }
            catch { }

            // 去重：已存在的文件直接打开
            var existing = Services.DownloadManager.FindCompletedByFileNameSync(fileName, url);
            if (existing != null)
            {
                e.Cancel = true;
                try { await Windows.System.Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(existing.FullPath)); } catch { }
                return;
            }

            StorageFolder defaultFolder = null;
            try { defaultFolder = await Services.DownloadManager.GetDownloadFolderAsync(); } catch { }

            if (askBeforeDownload)
            {
                e.Cancel = true; // 取消本次下载，让用户通过对话框重新选择
                var dialog = new Controls.DownloadDialog(fileName, url, defaultFolder);
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    if (dialog.SelectedFolder != null && !string.IsNullOrWhiteSpace(dialog.FileName))
                    {
                        // 通知 MainPage 使用隐蔽下载器以自定义路径重新下载
                        DownloadRequested?.Invoke(url, dialog.SelectedFolder, dialog.FileName);
                    }
                }
            }
            else
            {
                // 直接设置下载路径
                string filePath = Path.Combine(defaultFolder.Path, fileName);
                if (File.Exists(filePath))
                {
                    string ext = Path.GetExtension(fileName);
                    string name = Path.GetFileNameWithoutExtension(fileName);
                    int counter = 1;
                    while (File.Exists(filePath = Path.Combine(defaultFolder.Path, $"{name} ({counter++}){ext}"))) ;
                }
                e.ResultFilePath = filePath;            // 在事件参数中设置路径
                var item = await Services.DownloadManager.AddAsync(url, filePath, Path.GetFileName(filePath));
                item.WebViewOperation = op;
                item.TotalBytesToReceive = op.TotalBytesToReceive;
                Services.DownloadManager.StartDownloadOperation(item);
            }
        }

        // ========== 其余原有方法 ==========
        private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            UrlChanged?.Invoke(sender.Source?.ToString() ?? "");
            UpdateTitleFromDocument();
            CheckNavigationState();
            ExtractFavicon();
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
                var script = @"(function() { var links = document.querySelectorAll('link[rel*=""icon""]'); if (links.length > 0) { var href = links[0].href; if (href.startsWith('http')) return href; var baseUrl = location.protocol + '//' + location.host; return baseUrl + (href.startsWith('/') ? href : '/' + href); } return location.protocol + '//' + location.host + '/favicon.ico'; })()";
                var result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                if (!string.IsNullOrEmpty(result)) { result = result.Trim('"'); ProcessFaviconUrl(result); }
            }
            catch { }
        }

        private void ProcessFaviconUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https") && url != _faviconUri)
            { _faviconUri = url; FaviconChanged?.Invoke(url); }
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
                    if (target.Kind == CoreWebView2ContextMenuTargetKind.Image) { args.MenuType = ContextMenuType.Image; args.ImageUrl = target.SourceUri; }
                    else if (!string.IsNullOrEmpty(target.LinkUri)) { args.MenuType = ContextMenuType.Link; args.LinkUrl = target.LinkUri; }
                    args.HasSelection = target.HasSelection;
                    args.SelectionText = target.SelectionText ?? "";
                    args.IsEditable = target.IsEditable;
                }
                catch { }
            }
            ContextMenuRequested?.Invoke(args);
            e.Handled = true;
        }

        public async Task<string> ExecuteScriptAsync(string script) { if (_webView?.CoreWebView2 == null) return ""; try { return await _webView.CoreWebView2.ExecuteScriptAsync(script); } catch { return ""; } }

        public async Task NavigateAsync(string url)
        {
            if (_webView == null) return;
            await EnsureInitializedAsync();
            if (_webView.CoreWebView2 == null) return;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.Contains("://"))
                url = "https://" + url;
            if (Uri.TryCreate(url, UriKind.Absolute, out _)) _webView.CoreWebView2.Navigate(url);
        }

        public async Task GoBackAsync() { await EnsureInitializedAsync(); _webView.CoreWebView2?.GoBack(); }
        public async Task GoForwardAsync() { await EnsureInitializedAsync(); _webView.CoreWebView2?.GoForward(); }
        public Task RefreshAsync() { _webView.CoreWebView2?.Reload(); return Task.CompletedTask; }
        public Task StopAsync() { _webView.CoreWebView2?.Stop(); return Task.CompletedTask; }

        public void Dispose()
        {
            _initLock?.Dispose();
            if (_webView == null) return;
            _webView.NavigationCompleted -= OnNavigationCompleted;
            if (_webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.DocumentTitleChanged -= OnDocumentTitleChanged;
                _webView.CoreWebView2.HistoryChanged -= OnHistoryChanged;
                _webView.CoreWebView2.ContextMenuRequested -= OnContextMenuRequested;
                _webView.CoreWebView2.DownloadStarting -= OnDownloadStarting;
            }
            try { (_webView.Parent as Panel)?.Children.Remove(_webView); _webView.Close(); } catch { }
            _webView = null;

            TitleChanged = null;
            UrlChanged = null;
            CanGoBackChanged = null;
            CanGoForwardChanged = null;
            FaviconChanged = null;
            ContextMenuRequested = null;
        }
    }
}