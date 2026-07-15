using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Core
{
    public class EdgeHtmlTab : IBrowserTab
    {
        private WebView _webView;
        private string _faviconUri = "";

        public string Id { get; } = Guid.NewGuid().ToString();
        public FrameworkElement ViewElement => _webView;
        public bool CanGoBack => _webView.CanGoBack;
        public bool CanGoForward => _webView.CanGoForward;
        public string CurrentUrl => _webView.Source?.ToString() ?? "";
        public EngineType Engine => EngineType.EdgeHtml;
        public string EngineIcon => "🌐";
        public string FaviconUri => _faviconUri;

        public event Action<string> TitleChanged;
        public event Action<string> UrlChanged;
        public event Action<bool> CanGoBackChanged;
        public event Action<bool> CanGoForwardChanged;
        public event Action<string> FaviconChanged;

        public EdgeHtmlTab()
        {
            _webView = new WebView();
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.DOMContentLoaded += OnDOMContentLoaded;
            _webView.NavigationFailed += OnNavigationFailed;
        }

        // 使用 WebView 自带的 DocumentTitle 获取标题，更可靠
        private void UpdateTitle()
        {
            try
            {
                string title = _webView.DocumentTitle ?? "";
                if (string.IsNullOrWhiteSpace(title))
                    title = _webView.Source?.Host ?? "空白页";
                System.Diagnostics.Debug.WriteLine($"EdgeHtmlTab Title: '{title}'");
                TitleChanged?.Invoke(title);
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

            // 方法1：尝试读取 <link> 标签
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
                var result = await _webView.InvokeScriptAsync("eval", new[] { script });
                result = result?.Trim('"', ' ');
                if (!string.IsNullOrEmpty(result) && result.StartsWith("http"))
                {
                    SetFaviconUri(result);
                    return;
                }
            }
            catch { }

            // 方法2：默认使用 /favicon.ico
            string defaultFavicon = $"{_webView.Source.Scheme}://{_webView.Source.Host}/favicon.ico";
            SetFaviconUri(defaultFavicon);
        }

        private void SetFaviconUri(string url)
        {
            if (url == _faviconUri) return;
            _faviconUri = url;
            System.Diagnostics.Debug.WriteLine($"Favicon: {url}");
            FaviconChanged?.Invoke(url);
        }

        public void Navigate(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
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
        }

        public void GoBack() => _webView.GoBack();
        public void GoForward() => _webView.GoForward();
        public void Refresh() => _webView.Refresh();
        public void Stop() => _webView.Stop();

        public void Dispose()
        {
            _webView.NavigationCompleted -= OnNavigationCompleted;
            _webView.DOMContentLoaded -= OnDOMContentLoaded;
            _webView.NavigationFailed -= OnNavigationFailed;
            _webView = null;
        }
    }
}