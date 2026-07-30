using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
                CheckNavigationState();
            }
            finally { _initLock.Release(); }
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            NewWindowRequested?.Invoke(e.Uri);
        }

        private async void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            var dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher;
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => LoadingChanged?.Invoke(true));
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

        private async void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
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

                    var result = await dialog.ShowAsync();
                    if (result != ContentDialogResult.Primary)
                    {
                        e.Cancel = true;
                        return;
                    }

                    if (!Controls.DownloadDialog.TryValidateFileName(dialog.FileName, out string safeName, out string error))
                    {
                        e.Cancel = true;
                        await new ContentDialog { Title = "文件名无效", Content = error, CloseButtonText = "确定" }.ShowAsync();
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

        private async void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_isSuspended || _disposed) return;
            var dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher;
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => LoadingChanged?.Invoke(false));

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
                if (target.Kind == CoreWebView2ContextMenuTargetKind.Image) { args.MenuType = ContextMenuType.Image; args.ImageUrl = target.SourceUri; }
                else if (!string.IsNullOrEmpty(target.LinkUri)) { args.MenuType = ContextMenuType.Link; args.LinkUrl = target.LinkUri; }
                args.HasSelection = target.HasSelection; args.SelectionText = target.SelectionText ?? ""; args.IsEditable = target.IsEditable;
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