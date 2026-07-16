using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Core
{
    public class WebView2Tab : IBrowserTab
    {
        private WebView2 _webView;
        private Task _initTask;
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

        public WebView2Tab()
        {
            _webView = new WebView2();
            _webView.NavigationCompleted += OnNavigationCompleted;
        }

        public async Task EnsureInitializedAsync()
        {
            if (_webView.CoreWebView2 != null) return;
            if (_initTask == null)
                _initTask = _webView.EnsureCoreWebView2Async().AsTask();
            await _initTask;

            _webView.CoreWebView2.DocumentTitleChanged += OnDocumentTitleChanged;
            _webView.CoreWebView2.HistoryChanged += OnHistoryChanged;
            CheckNavigationState();
        }

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
            if (string.IsNullOrWhiteSpace(newTitle))
                newTitle = _webView.Source?.Host ?? "新标签页";
            if (newTitle != _title)
            {
                _title = newTitle;
                TitleChanged?.Invoke(_title);
            }
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

        public async Task NavigateAsync(string url)
        {
            if (_webView == null) return;
            await EnsureInitializedAsync();
            if (_webView.CoreWebView2 == null) return;
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("edge:", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains("://"))
            {
                url = "https://" + url;
            }
            if (Uri.TryCreate(url, UriKind.Absolute, out _))
                _webView.CoreWebView2.Navigate(url);
        }

        public async Task GoBackAsync()
        {
            await EnsureInitializedAsync();
            _webView.CoreWebView2?.GoBack();
        }

        public async Task GoForwardAsync()
        {
            await EnsureInitializedAsync();
            _webView.CoreWebView2?.GoForward();
        }

        public Task RefreshAsync()
        {
            _webView.CoreWebView2?.Reload();
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            _webView.CoreWebView2?.Stop();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_webView == null) return;
            _webView.NavigationCompleted -= OnNavigationCompleted;
            if (_webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.DocumentTitleChanged -= OnDocumentTitleChanged;
                _webView.CoreWebView2.HistoryChanged -= OnHistoryChanged;
            }
            try
            {
                var parent = _webView.Parent as Panel;
                parent?.Children.Remove(_webView);
                _webView.Close();
            }
            catch { }
            _webView = null;
        }
    }
}