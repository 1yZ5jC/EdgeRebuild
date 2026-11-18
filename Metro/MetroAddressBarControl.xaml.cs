using System;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Metro
{
    public sealed partial class MetroAddressBarControl : UserControl
    {
        public event Action? BackRequested;
        public event Action? ForwardRequested;
        public event Action? RefreshRequested;
        public event Action? FavoriteRequested;
        public event Action? MoreRequested;
        public event Action<string>? UrlSubmitted;
        public event Action? ContentAreaFocusRequested;   // Esc/提交后把焦点还给页面内容

        public MetroAddressBarControl()
        {
            InitializeComponent();
            UpdateNavButtons(false, false);
            UpdateAddressChrome();
        }

        private string _currentUrl = "";

        public void UpdateNavButtons(bool canGoBack, bool canGoForward)
        {
            BackBtn.IsEnabled = canGoBack;
            ForwardBtn.IsEnabled = canGoForward;
            BackBtn.Foreground = new SolidColorBrush(canGoBack ? Colors.White : MetroColorPalette.MutedColor);
            ForwardBtn.Foreground = new SolidColorBrush(canGoForward ? Colors.White : MetroColorPalette.MutedColor);
        }

        public void SetUrl(string url)
        {
            _currentUrl = url ?? "";
            if (UrlBox.FocusState == FocusState.Unfocused)
            {
                UrlBox.Text = _currentUrl;
                UpdateAddressChrome();
            }
        }

        /// <summary>当前页已收藏时星标显示实心；点击始终打开收藏夹面板。</summary>
        public void SetFavoriteState(bool isFavorite)
        {
            FavoriteBtn.Content = isFavorite ? "\uE735" : "\uE734";
            ToolTipService.SetToolTip(FavoriteBtn, isFavorite ? "收藏夹（已收藏）" : "收藏夹");
        }

        public void FocusUrl()
        {
            UrlBox.Focus(FocusState.Programmatic);
            UrlBox.SelectAll();
            UpdateAddressChrome();
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();
        private void ForwardBtn_Click(object sender, RoutedEventArgs e) => ForwardRequested?.Invoke();
        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
        private void FavoriteBtn_Click(object sender, RoutedEventArgs e) => FavoriteRequested?.Invoke();
        private void MoreBtn_Click(object sender, RoutedEventArgs e) => MoreRequested?.Invoke();
        private void GoBtn_Click(object sender, RoutedEventArgs e) => SubmitUrl();

        private void SubmitUrl()
        {
            string input = (UrlBox.Text ?? "").Trim();
            if (input.Length == 0) return;
            UrlSubmitted?.Invoke(input);
            ContentAreaFocusRequested?.Invoke();   // 焦点还给页面内容
        }

        private void UrlBox_GotFocus(object sender, RoutedEventArgs e)
        {
            UrlFrame.Background = new SolidColorBrush(Colors.White);
            UrlFrame.BorderBrush = new SolidColorBrush(MetroColorPalette.MetroBlue);
            UrlBox.Foreground = new SolidColorBrush(Colors.Black);
            UpdateAddressChrome();
        }

        private void UrlBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UrlFrame.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F));
            UrlFrame.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
            UrlBox.Foreground = new SolidColorBrush(Colors.White);
            UpdateAddressChrome();
        }

        private void UrlBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateAddressChrome();

        private void UpdateAddressChrome()
        {
            if (ClearBtn == null || GoBtn == null || UrlBox == null) return;

            bool focused = UrlBox.FocusState != FocusState.Unfocused;
            bool hasText = !string.IsNullOrEmpty(UrlBox.Text);
            ClearBtn.Visibility = focused && hasText ? Visibility.Visible : Visibility.Collapsed;
            GoBtn.IsEnabled = !string.IsNullOrWhiteSpace(UrlBox.Text);
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            UrlBox.Text = "";
            UrlBox.Focus(FocusState.Programmatic);
            UpdateAddressChrome();
        }

        private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                SubmitUrl();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                // 恢复显示当前页地址（而非清空）
                UrlBox.Text = _currentUrl;
                UrlBox.SelectionStart = UrlBox.Text.Length;
                ContentAreaFocusRequested?.Invoke();
                UpdateAddressChrome();
                e.Handled = true;
            }
        }
    }
}
