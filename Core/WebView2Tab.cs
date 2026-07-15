using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace EdgeRebuild.Core
{
    public class WebView2Tab : IBrowserTab
    {
        private WebView2 _webView;
        private Task _initTask;
        private string _faviconUri = "";

        public string Id { get; } = Guid.NewGuid().ToString();
        public FrameworkElement ViewElement => _webView;
        public bool CanGoBack => _webView.CoreWebView2?.CanGoBack ?? false;
        public bool CanGoForward => _webView.CoreWebView2?.CanGoForward ?? false;
        public string CurrentUrl => _webView.Source?.ToString() ?? "";
        public EngineType Engine => EngineType.WebView2;
        public string EngineIcon => "🧬";
        public string FaviconUri => _faviconUri;

        public event Action<string> TitleChanged;
        public event Action<string> UrlChanged;
        public event Action<bool> CanGoBackChanged;
        public event Action<bool> CanGoForwardChanged;
        public event Action<string> FaviconChanged;

        public WebView2Tab()
        {
            _webView = new WebView2();
            _webView.NavigationCompleted += OnNavigationCompleted;
        }

        // 公开初始化方法，供外部调用
        public async Task EnsureInitializedAsync()
        {
            if (_webView == null || _webView.CoreWebView2 != null) return;

            try
            {
                if (_initTask == null)
                    _initTask = _webView.EnsureCoreWebView2Async().AsTask();
                await _initTask;

                _webView.CoreWebView2.DocumentTitleChanged += OnDocumentTitleChanged;
                _webView.CoreWebView2.HistoryChanged += OnHistoryChanged;
                CheckNavigationState();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2 init failed: {ex.Message}");
                // 初始化失败时，_initTask 应重置以便允许重试
                _initTask = null;
                throw; // 重新抛出，让 Navigate 的 catch 捕获
            }
        }

        private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            UrlChanged?.Invoke(sender.Source?.ToString() ?? "");
            TitleChanged?.Invoke(_webView.CoreWebView2?.DocumentTitle);
            CheckNavigationState();
            ExtractFavicon();
        }

        private void OnDocumentTitleChanged(object sender, object e) =>
            TitleChanged?.Invoke(_webView.CoreWebView2?.DocumentTitle);

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
            if (_webView.CoreWebView2 == null || _webView.Source?.AbsoluteUri == "about:blank")
                return;

            try
            {
                var script = @"
(function() {
    var links = document.querySelectorAll('link[rel*=""icon""]');
    if (links.length > 0) {
        var href = links[0].href;
        if (href.startsWith('http')) return href;
        var baseUrl = location.protocol + '//' + location.host;
        return baseUrl + (href.startsWith('/') ? href : '/' + href);
    }
    return location.protocol + '//' + location.host + '/favicon.ico';
})()";
                var result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                if (!string.IsNullOrEmpty(result))
                {
                    result = result.Trim('"');
                    ProcessFaviconUrl(result);
                }
            }
            catch { }
        }

        private void ProcessFaviconUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                if (url != _faviconUri)
                {
                    _faviconUri = url;
                    FaviconChanged?.Invoke(url);
                }
            }
        }

        public async void Navigate(string url)
        {
            try
            {
                if (_webView == null) return; // 标签已销毁

                await EnsureInitializedAsync();

                if (_webView.CoreWebView2 == null) return;

                if (string.IsNullOrWhiteSpace(url)) return;
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    url = "https://" + url;

                _webView.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                // 记录异常但不让应用崩溃
                System.Diagnostics.Debug.WriteLine($"WebView2Tab Navigate error: {ex.Message}");
            }
        }

        public async void GoBack()
        {
            await EnsureInitializedAsync();
            _webView.CoreWebView2?.GoBack();
        }

        public async void GoForward()
        {
            await EnsureInitializedAsync();
            _webView.CoreWebView2?.GoForward();
        }

        public void Refresh() => _webView.CoreWebView2?.Reload();
        public void Stop() => _webView.CoreWebView2?.Stop();

        public void Dispose()
        {
            _webView.NavigationCompleted -= OnNavigationCompleted;
            if (_webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.DocumentTitleChanged -= OnDocumentTitleChanged;
                _webView.CoreWebView2.HistoryChanged -= OnHistoryChanged;
            }
            _webView.Close();
            _webView = null;
        }
    }
}