using System;
using System.Threading.Tasks;
using Windows.Data.Json;
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
            // 启用任意来源的脚本通知
            foreach (var uri in WebView.AnyScriptNotifyUri)
            {
                _webView.AllowedScriptNotifyUris.Add(uri);
            }

            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.DOMContentLoaded += OnDOMContentLoaded;
            _webView.NavigationFailed += OnNavigationFailed;
            _webView.ScriptNotify += OnScriptNotify;
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
            InjectContextMenuScript();
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
                var result = await _webView.InvokeScriptAsync("eval", new[] { script });
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

        private async void InjectContextMenuScript()
        {
            try
            {
                string script = @"
document.addEventListener('contextmenu', function(e) {
    e.preventDefault();
    var target = e.target;
    var info = {
        type: 'page',
        linkUrl: '',
        imageUrl: '',
        x: e.clientX,
        y: e.clientY,
        selectionText: window.getSelection().toString(),
        hasSelection: !window.getSelection().isCollapsed,
        isEditable: target.isContentEditable || target.tagName === 'INPUT' || target.tagName === 'TEXTAREA'
    };
    var link = target.closest('a');
    if (link && link.href && !link.href.startsWith('javascript:')) {
        info.type = 'link';
        info.linkUrl = link.href;
    }
    if (target.tagName === 'IMG' && target.src) {
        info.type = 'image';
        info.imageUrl = target.src;
    }
    window.external.notify(JSON.stringify(info));
}, true);
";
                await _webView.InvokeScriptAsync("eval", new[] { script });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InjectContextMenuScript error: {ex.Message}");
            }
        }

        private void OnScriptNotify(object sender, NotifyEventArgs e)
        {
            try
            {
                JsonObject info = JsonObject.Parse(e.Value);
                var args = new TabContextMenuEventArgs
                {
                    Location = new Windows.Foundation.Point(info.GetNamedNumber("x"), info.GetNamedNumber("y")),
                    CanGoBack = _webView.CanGoBack,
                    CanGoForward = _webView.CanGoForward,
                    HasSelection = info.GetNamedBoolean("hasSelection"),
                    SelectionText = info.GetNamedString("selectionText"),
                    IsEditable = info.GetNamedBoolean("isEditable")
                };

                string type = info.GetNamedString("type");
                if (type == "link")
                {
                    args.MenuType = ContextMenuType.Link;
                    args.LinkUrl = info.GetNamedString("linkUrl");
                }
                else if (type == "image")
                {
                    args.MenuType = ContextMenuType.Image;
                    args.ImageUrl = info.GetNamedString("imageUrl");
                }
                else
                {
                    args.MenuType = ContextMenuType.Page;
                }

                ContextMenuRequested?.Invoke(args);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnScriptNotify error: {ex.Message}");
            }
        }

        public async Task<string> ExecuteScriptAsync(string script)
        {
            if (_webView == null) return "";
            try
            {
                return await _webView.InvokeScriptAsync("eval", new[] { script });
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
            _webView.ScriptNotify -= OnScriptNotify;
            _webView = null;
        }
    }
}