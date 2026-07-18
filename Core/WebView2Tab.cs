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
        private bool _isSuspended = false;
        private string _suspendedUrl = "";
        private bool _httpsFailed = false;

        public string Id { get; } = Guid.NewGuid().ToString();
        public FrameworkElement ViewElement => _webView;
        public bool CanGoBack => _webView.CoreWebView2?.CanGoBack ?? false;
        public bool CanGoForward => _webView.CoreWebView2?.CanGoForward ?? false;
        public string CurrentUrl => _isSuspended ? _suspendedUrl : (_webView.Source?.ToString() ?? "");
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
                _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
                CheckNavigationState();
            }
            finally { _initLock.Release(); }
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            NewWindowRequested?.Invoke(e.Uri);
        }

        public async Task SuspendAsync()
        {
            if (_isSuspended) return;
            if (_webView.CoreWebView2 == null) { _suspendedUrl = _webView.Source?.ToString() ?? ""; _isSuspended = true; return; }
            try
            {
                _suspendedUrl = _webView.Source?.ToString() ?? "";
                await _webView.CoreWebView2.TrySuspendAsync();
                _isSuspended = true;
            }
            catch { }
        }

        public async Task ResumeAsync()
        {
            if (!_isSuspended) return;
            _isSuspended = false;
            if (_webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.Resume();
                if (!string.IsNullOrEmpty(_suspendedUrl)) _webView.CoreWebView2.Navigate(_suspendedUrl);
            }
            else await NavigateAsync(_suspendedUrl);
        }

        private async void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            var op = e.DownloadOperation;
            e.Handled = true;
            string url = op.Uri ?? "";
            string fileName = Path.GetFileName(op.ResultFilePath);

            bool askBeforeDownload = false;
            try { askBeforeDownload = (await Services.SettingsManager.GetAsync("AskBeforeDownload") == "True"); } catch { }

            var existing = Services.DownloadManager.FindCompletedByFileNameSync(fileName, url);
            if (existing != null) { e.Cancel = true; try { await Windows.System.Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(existing.FullPath)); } catch { } return; }

            StorageFolder defaultFolder = null;
            try { defaultFolder = await Services.DownloadManager.GetDownloadFolderAsync(); } catch { }

            if (askBeforeDownload)
            {
                e.Cancel = true;
                var dialog = new Controls.DownloadDialog(fileName, url, defaultFolder);
                if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.SelectedFolder != null && !string.IsNullOrWhiteSpace(dialog.FileName))
                    DownloadRequested?.Invoke(url, dialog.SelectedFolder, dialog.FileName);
            }
            else
            {
                string filePath = Path.Combine(defaultFolder.Path, fileName);
                if (File.Exists(filePath)) { string ext = Path.GetExtension(fileName); string name = Path.GetFileNameWithoutExtension(fileName); int counter = 1; while (File.Exists(filePath = Path.Combine(defaultFolder.Path, $"{name} ({counter++}){ext}"))) ; }
                e.ResultFilePath = filePath;
                var item = await Services.DownloadManager.AddAsync(url, filePath, Path.GetFileName(filePath));
                item.WebViewOperation = op; item.TotalBytesToReceive = op.TotalBytesToReceive;
                Services.DownloadManager.StartDownloadOperation(item);
            }
        }

        private async void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_isSuspended) return;

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
                if (!_httpsFailed && currentUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    if (e.WebErrorStatus == CoreWebView2WebErrorStatus.ConnectionAborted ||
                        e.WebErrorStatus == CoreWebView2WebErrorStatus.CannotConnect ||
                        e.WebErrorStatus == CoreWebView2WebErrorStatus.HostNameNotResolved ||
                        e.WebErrorStatus == CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect ||
                        e.WebErrorStatus == CoreWebView2WebErrorStatus.CertificateExpired ||
                        e.WebErrorStatus == CoreWebView2WebErrorStatus.CertificateIsInvalid ||
                        e.WebErrorStatus == CoreWebView2WebErrorStatus.ServerUnreachable ||
                        e.WebErrorStatus == CoreWebView2WebErrorStatus.Timeout ||
                        e.WebErrorStatus == CoreWebView2WebErrorStatus.Unknown)
                    {
                        _httpsFailed = true;
                        string httpUrl = "http://" + currentUrl.Substring("https://".Length);
                        System.Diagnostics.Debug.WriteLine($"[HTTPS Fallback] Trying HTTP: {httpUrl}");
                        _webView.CoreWebView2.Navigate(httpUrl);
                        return;
                    }
                }
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
                if (target.Kind == CoreWebView2ContextMenuTargetKind.Image) { args.MenuType = ContextMenuType.Image; args.ImageUrl = target.SourceUri; }
                else if (!string.IsNullOrEmpty(target.LinkUri)) { args.MenuType = ContextMenuType.Link; args.LinkUrl = target.LinkUri; }
                args.HasSelection = target.HasSelection; args.SelectionText = target.SelectionText ?? ""; args.IsEditable = target.IsEditable;
            }
            ContextMenuRequested?.Invoke(args);
            e.Handled = true;
        }

        public async Task<string> ExecuteScriptAsync(string script) => (_webView?.CoreWebView2 != null) ? await _webView.CoreWebView2.ExecuteScriptAsync(script) : "";

        // 修复：先判断是否已是有效的绝对 URI，再决定是否补全协议
        public async Task NavigateAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                url = "about:blank";

            if (_webView == null) return;
            await EnsureInitializedAsync();
            if (_webView.CoreWebView2 == null) return;

            _httpsFailed = false;

            // 如果已经是有效的绝对 URI，直接使用（避免错误补全）
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                // 如果不是绝对 URI，尝试补全 "https://"
                string candidate = "https://" + url;
                if (!Uri.TryCreate(candidate, UriKind.Absolute, out uri))
                {
                    // 仍然无效，回退到 about:blank
                    uri = new Uri("about:blank");
                }
            }

            _webView.CoreWebView2.Navigate(uri.AbsoluteUri);
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
                _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
            }
            try { (_webView.Parent as Panel)?.Children.Remove(_webView); _webView.Close(); } catch { }
            _webView = null;

            TitleChanged = null;
            UrlChanged = null;
            CanGoBackChanged = null;
            CanGoForwardChanged = null;
            FaviconChanged = null;
            ContextMenuRequested = null;
            NewWindowRequested = null;
        }
    }
}