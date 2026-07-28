using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Core
{
    public class EdgeHtmlTab : IBrowserTab
    {
        private WebView _webView;
        private string _faviconUri = "";
        private string _title = "";
        private bool _httpsFailed = false;

        public static event Action<string> DownloadRequested;

        public string Id { get; } = Guid.NewGuid().ToString();
        public FrameworkElement ViewElement => _webView;
        public bool CanGoBack => _webView.CanGoBack;
        public bool CanGoForward => _webView.CanGoForward;
        public string CurrentUrl => _webView.Source?.ToString() ?? "";
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
        public event Action<TabContextMenuEventArgs> ContextMenuRequested;
        public event Action<string> NewWindowRequested;
        public event Action<bool> LoadingChanged;

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

        private async void OnNavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            var dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher;
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => LoadingChanged?.Invoke(true));

            string[] exts = { ".exe", ".zip", ".rar", ".7z", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".mp3", ".mp4", ".avi", ".mkv", ".apk", ".msi", ".tar", ".gz", ".bz2", ".dmg", ".iso" };
            string url = args.Uri?.ToString() ?? "";
            if (exts.Any(ext => url.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                args.Cancel = true;
                DownloadRequested?.Invoke(url);
                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => LoadingChanged?.Invoke(false));
            }
        }

        private async void OnNavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            var dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher;
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => LoadingChanged?.Invoke(false));

            if (args.IsSuccess)
            {
                _httpsFailed = false;
                UrlChanged?.Invoke(args.Uri?.ToString() ?? "");
                UpdateTitle();
                CheckNavigationState();
                TryExtractFavicon();
            }
        }

        private async void OnNavigationFailed(object sender, WebViewNavigationFailedEventArgs e)
        {
            var dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher;
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => LoadingChanged?.Invoke(false));
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

        private void OnDOMContentLoaded(WebView sender, WebViewDOMContentLoadedEventArgs args) { UpdateTitle(); TryExtractFavicon(); }

        private void CheckNavigationState()
        {
            CanGoBackChanged?.Invoke(_webView.CanGoBack);
            CanGoForwardChanged?.Invoke(_webView.CanGoForward);
        }

        private async void TryExtractFavicon()
        {
            if (_webView?.Source == null || _webView.Source.AbsoluteUri == "about:blank") return;
            try
            {
                var result = await _webView.InvokeScriptAsync("eval", new[] { "document.querySelector('link[rel*=\"icon\"]')?.href || ''" });
                if (!string.IsNullOrEmpty(result)) SetFaviconUri(result.Trim('"'));
                else SetFaviconUri($"{_webView.Source.Scheme}://{_webView.Source.Host}/favicon.ico");
            }
            catch { }
        }

        private void SetFaviconUri(string url)
        {
            if (url != _faviconUri) { _faviconUri = url; FaviconChanged?.Invoke(url); }
        }

        public async Task<string> ExecuteScriptAsync(string script)
        {
            try { return await _webView.InvokeScriptAsync("eval", new[] { script }); }
            catch { return ""; }
        }

        public Task NavigateAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) url = "about:blank";
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains("://"))
                url = "https://" + url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) uri = new Uri("about:blank");
            _httpsFailed = false;
            _webView.Navigate(uri);
            return Task.CompletedTask;
        }

        public Task GoBackAsync() { _webView.GoBack(); return Task.CompletedTask; }
        public Task GoForwardAsync() { _webView.GoForward(); return Task.CompletedTask; }
        public Task RefreshAsync() { _webView.Refresh(); return Task.CompletedTask; }
        public Task StopAsync() { _webView.Stop(); return Task.CompletedTask; }

        public void Dispose()
        {
            _webView.NewWindowRequested -= OnNewWindowRequested;
            _webView.NavigationStarting -= OnNavigationStarting;
            _webView.NavigationCompleted -= OnNavigationCompleted;
            _webView.DOMContentLoaded -= OnDOMContentLoaded;
            _webView.NavigationFailed -= OnNavigationFailed;
            _webView = null;

            TitleChanged = null;
            UrlChanged = null;
            CanGoBackChanged = null;
            CanGoForwardChanged = null;
            FaviconChanged = null;
            ContextMenuRequested = null;
            NewWindowRequested = null;
            LoadingChanged = null;
        }
    }
}