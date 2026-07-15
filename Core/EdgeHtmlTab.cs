using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace EdgeRebuild.Core
{
    public class EdgeHtmlTab : IBrowserTab
    {
        private WebView _webView;

        public string Id { get; } = Guid.NewGuid().ToString();
        public FrameworkElement ViewElement => _webView;
        public bool CanGoBack => _webView.CanGoBack;
        public bool CanGoForward => _webView.CanGoForward;
        public string CurrentUrl => _webView.Source?.ToString() ?? "";

        public event Action<string> TitleChanged;
        public event Action<string> UrlChanged;
        public event Action<bool> CanGoBackChanged;
        public event Action<bool> CanGoForwardChanged;
        public EngineType Engine => EngineType.EdgeHtml;
        public string EngineIcon => "🌐";
        public string FaviconUri => _faviconUri;

        
        public event Action<string> FaviconChanged;

        public EdgeHtmlTab()
        {
            _webView = new WebView();
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.DOMContentLoaded += OnDOMContentLoaded;
        }

        private async void UpdateTitle()
        {
            try
            {
                var title = await _webView.InvokeScriptAsync("eval", new[] { "document.title" });
                TitleChanged?.Invoke(title ?? "");
            }
            catch { }
        }

        private void OnDOMContentLoaded(WebView sender, WebViewDOMContentLoadedEventArgs args) => UpdateTitle();

        private void OnNavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            UrlChanged?.Invoke(args.Uri?.ToString() ?? "");
            UpdateTitle();
            CheckNavigationState();
        }

        private void CheckNavigationState()
        {
            CanGoBackChanged?.Invoke(_webView.CanGoBack);
            CanGoForwardChanged?.Invoke(_webView.CanGoForward);
        }

        public async void Navigate(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return;
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    url = "https://" + url;
                _webView?.Navigate(new Uri(url));
            }
            catch (Exception ex)
            {
                // 记录日志或显示错误，但不崩溃
                System.Diagnostics.Debug.WriteLine($"EdgeHtmlTab Navigate error: {ex.Message}");
            }
        }

        public void GoBack() => _webView.GoBack();
        public void GoForward() => _webView.GoForward();
        public void Refresh() => _webView.Refresh();
        public void Stop() => _webView.Stop();

        public void Dispose()
        {
            _webView.NavigationCompleted -= OnNavigationCompleted;
            _webView.DOMContentLoaded -= OnDOMContentLoaded;
            _webView = null;
        }
    }
}