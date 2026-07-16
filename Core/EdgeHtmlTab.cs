using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Core
{
    public class EdgeHtmlTab : IBrowserTab
    {
        private WebView _webView;
        private string _faviconUri = "";
        private string _title = "";

        public string Id { get; } = Guid.NewGuid().ToString();
        public FrameworkElement ViewElement => _webView;
        public bool CanGoBack => _webView.CanGoBack;
        public bool CanGoForward => _webView.CanGoForward;
        public string CurrentUrl => _webView.Source?.ToString() ?? "";
        public EngineType Engine => EngineType.EdgeHtml;
        public string EngineIcon => "🌐";
        public string Title => _title;
        public string FaviconUri => _faviconUri;

        public event Action<string> TitleChanged;
        public event Action<string> UrlChanged;
        public event Action<bool> CanGoBackChanged;
        public event Action<bool> CanGoForwardChanged;
        public event Action<string> FaviconChanged;
        public event Action<TabContextMenuEventArgs> ContextMenuRequested;

        public EdgeHtmlTab()
        {
            _webView = new WebView();
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.DOMContentLoaded += OnDOMContentLoaded;
            _webView.NavigationFailed += OnNavigationFailed;
        }

        private void UpdateTitle()
        {
            try
            {
                string docTitle = _webView.DocumentTitle;
                if (string.IsNullOrWhiteSpace(docTitle))
                {
                    string host = _webView.Source?.Host;
                    _title = string.IsNullOrEmpty(host) ? "新标签页" : host;
                }
                else
                {
                    _title = docTitle;
                }
                TitleChanged?.Invoke(_title);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateTitle error: {ex.Message}");
            }
        }

        private void OnDOMContentLoaded(WebView sender, WebViewDOMContentLoadedEventArgs args)
        {
            UpdateTitle();
            TryExtractFavicon();
        }

        private void OnNavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            UrlChanged?.Invoke(args.Uri?.ToString() ?? "");
            UpdateTitle();
            CheckNavigationState();
            TryExtractFavicon();
        }

        private void OnNavigationFailed(object sender, WebViewNavigationFailedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation failed: {e.Uri} - {e.WebErrorStatus}");
        }

        private void CheckNavigationState()
        {
            CanGoBackChanged?.Invoke(_webView.CanGoBack);
            CanGoForwardChanged?.Invoke(_webView.CanGoForward);
        }

        private async void TryExtractFavicon()
        {
            if (_webView?.Source == null || _webView.Source.AbsoluteUri == "about:blank")
                return;

            string script = @"
(function() {
    var links = document.querySelectorAll('link[rel*=""icon""]');
    if (links.length > 0) {
        var href = links[0].href;
        if (href) return href;
    }
    return '';
})()";

            try
            {
                var result = await _webView.InvokeScriptAsync("eval", new string[] { script });
                result = result?.Trim('"', ' ');
                if (!string.IsNullOrEmpty(result) && result.StartsWith("http"))
                {
                    SetFaviconUri(result);
                    return;
                }
            }
            catch { }

            string defaultFavicon = $"{_webView.Source.Scheme}://{_webView.Source.Host}/favicon.ico";
            SetFaviconUri(defaultFavicon);
        }

        private void SetFaviconUri(string url)
        {
            if (url == _faviconUri) return;
            _faviconUri = url;
            FaviconChanged?.Invoke(url);
        }

        public async Task<string> ExecuteScriptAsync(string script)
        {
            if (_webView == null) return "";
            try
            {
                return await _webView.InvokeScriptAsync("eval", new string[] { script });
            }
            catch
            {
                return "";
            }
        }

        public Task NavigateAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return Task.CompletedTask;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("edge:", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains("://"))
            {
                url = "https://" + url;
            }
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                _webView.Navigate(uri);
            return Task.CompletedTask;
        }

        public Task GoBackAsync() { _webView.GoBack(); return Task.CompletedTask; }
        public Task GoForwardAsync() { _webView.GoForward(); return Task.CompletedTask; }
        public Task RefreshAsync() { _webView.Refresh(); return Task.CompletedTask; }
        public Task StopAsync() { _webView.Stop(); return Task.CompletedTask; }

        public void Dispose()
        {
            _webView.NavigationCompleted -= OnNavigationCompleted;
            _webView.DOMContentLoaded -= OnDOMContentLoaded;
            _webView.NavigationFailed -= OnNavigationFailed;
            _webView = null;
        }
    }
}