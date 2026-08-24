using System;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Metro
{
    public sealed partial class MetroAddressBarControl : UserControl
    {
        public event Action BackRequested;
        public event Action ForwardRequested;
        public event Action RefreshRequested;
        public event Action HomeRequested;
        public event Action FullscreenRequested;
        public event Action<string> UrlSubmitted;
        public event Action ContentAreaFocusRequested;   // Esc/提交后把焦点还给页面内容

        public MetroAddressBarControl()
        {
            InitializeComponent();
            UpdateNavButtons(false, false);
        }

        private string _currentUrl = "";

        public void UpdateNavButtons(bool canGoBack, bool canGoForward)
        {
            BackBtn.IsEnabled = canGoBack;
            ForwardBtn.IsEnabled = canGoForward;
            BackBtn.Foreground = new SolidColorBrush(canGoBack ? Windows.UI.Colors.White : MetroColorPalette.MutedColor);
            ForwardBtn.Foreground = new SolidColorBrush(canGoForward ? Windows.UI.Colors.White : MetroColorPalette.MutedColor);
        }

        public void SetUrl(string url)
        {
            _currentUrl = url ?? "";
            if (UrlBox.FocusState == FocusState.Unfocused)
                UrlBox.Text = _currentUrl;
        }

        public void FocusUrl()
        {
            UrlBox.Focus(FocusState.Programmatic);
            UrlBox.SelectAll();
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();
        private void ForwardBtn_Click(object sender, RoutedEventArgs e) => ForwardRequested?.Invoke();
        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
        private void HomeBtn_Click(object sender, RoutedEventArgs e) => HomeRequested?.Invoke();
        private void FullscreenBtn_Click(object sender, RoutedEventArgs e) => FullscreenRequested?.Invoke();

        private void UrlBox_GotFocus(object sender, RoutedEventArgs e)
        {
            FocusLine.Visibility = Visibility.Visible;
        }

        private void UrlBox_LostFocus(object sender, RoutedEventArgs e)
        {
            FocusLine.Visibility = Visibility.Collapsed;
        }

        private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                UrlSubmitted?.Invoke(UrlBox.Text?.Trim() ?? "");
                ContentAreaFocusRequested?.Invoke();   // 焦点还给页面内容
            }
            else if (e.Key == VirtualKey.Escape)
            {
                // 恢复显示当前页地址（而非清空）
                UrlBox.Text = _currentUrl;
                UrlBox.SelectionStart = UrlBox.Text.Length;
                ContentAreaFocusRequested?.Invoke();
            }
        }
    }
}
