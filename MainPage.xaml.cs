using Microsoft.UI.Xaml.Controls;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace EdgeRebuild
{
    public sealed partial class MainPage : Page
    {
        // EdgeHTML WebView (UWP 原生)
        private Windows.UI.Xaml.Controls.WebView _edgeHtmlView;
        // WebView2 (WinUI 2)
        private WebView2 _webView2;

        public MainPage()
        {
            this.InitializeComponent();
            // 默认加载 EdgeHTML
            ShowEdgeHtml();
        }

        private void ShowEdgeHtml()
        {
            // 清除容器
            WebViewContainer.Child = null;

            // 如果还未创建则创建 EdgeHTML WebView
            if (_edgeHtmlView == null)
            {
                _edgeHtmlView = new Windows.UI.Xaml.Controls.WebView();
                _edgeHtmlView.NavigationCompleted += (s, e) =>
                {
                    UrlTextBox.Text = e.Uri?.ToString();
                };
            }

            WebViewContainer.Child = _edgeHtmlView;
        }

        private async void ShowWebView2()
        {
            WebViewContainer.Child = null;

            if (_webView2 == null)
            {
                _webView2 = new WebView2();
                await _webView2.EnsureCoreWebView2Async();
                _webView2.NavigationCompleted += (s, e) =>
                {
                    UrlTextBox.Text = _webView2.Source?.ToString();
                };
            }

            WebViewContainer.Child = _webView2;
        }

        private void BtnEdgeHtml_Click(object sender, RoutedEventArgs e)
        {
            ShowEdgeHtml();
        }

        private void BtnWebView2_Click(object sender, RoutedEventArgs e)
        {
            ShowWebView2();
        }

        private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                string url = UrlTextBox.Text;
                if (!url.StartsWith("http"))
                    url = "https://" + url;

                if (WebViewContainer.Child is Windows.UI.Xaml.Controls.WebView edgeView)
                {
                    edgeView.Navigate(new Uri(url));
                }
                else if (WebViewContainer.Child is WebView2 wv2)
                {
                    wv2.CoreWebView2.Navigate(url);
                }
            }
        }
    }
}
