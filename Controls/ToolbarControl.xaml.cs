using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Controls
{
    public sealed partial class ToolbarControl : UserControl
    {
        public event Action<string> UrlSubmitted;
        public event Action BackRequested;
        public event Action ForwardRequested;
        public event Action RefreshRequested;
        public event Action AddFavoriteClicked;
        public event Action HubClicked;
        public event Action MenuClicked;
        public event Action<EngineType> EngineSwitched;

        private DispatcherTimer _suggestionTimer;
        private bool _isSuggestionsVisible;
        private List<SuggestionItem> _currentSuggestions = new List<SuggestionItem>();

        private const string SearchEngineUrl = "https://www.google.com/search?q={0}";
        private static readonly IdnMapping _idn = new IdnMapping();

        private Brush _foregroundBrush;
        private Brush _mutedForegroundBrush;
        private bool _isDarkTheme;

        public ToolbarControl()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _suggestionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _suggestionTimer.Tick += OnSuggestionTimerTick;
            SuggestionsFlyout.Closed += (_, _) => _isSuggestionsVisible = false;
        }

        public void FocusAddressBarAndClear()
        {
            UrlBox.Text = string.Empty;
            UrlBox.Focus(FocusState.Programmatic);
        }

        public void ApplySkin(SkinManager.SkinColors colors, bool isDark)
        {
            RootBorder.Background = colors.ToolbarBackground;
            RootBorder.BorderBrush = colors.SeparatorBrush;
            UrlBox.Background = colors.AddressBarBackground;
            UrlBox.BorderBrush = colors.AddressBarBorder;
            UrlBox.Foreground = colors.ForegroundBrush;
            EngineLabel.Foreground = colors.ForegroundBrush;
            EngineCombo.Foreground = colors.ForegroundBrush;
            BackBtn.Foreground = colors.MutedForegroundBrush;
            ForwardBtn.Foreground = colors.MutedForegroundBrush;
            RefreshBtn.Foreground = colors.MutedForegroundBrush;
            AddFavBtn.Foreground = colors.MutedForegroundBrush;
            HubBtn.Foreground = colors.MutedForegroundBrush;
            MenuBtn.Foreground = colors.MutedForegroundBrush;

            _foregroundBrush = colors.ForegroundBrush;
            _mutedForegroundBrush = colors.MutedForegroundBrush;
            _isDarkTheme = isDark;

            SuggestionsFlyout.FlyoutPresenterStyle = null;
        }

        public void UpdateNavState(bool canGoBack, bool canGoForward, string currentUrl)
        {
            BackBtn.IsEnabled = canGoBack;
            ForwardBtn.IsEnabled = canGoForward;
            if (currentUrl == "about:blank") UrlBox.Text = "";
            else UrlBox.Text = DecodeUrl(currentUrl);
        }

        public void SetEngine(EngineType engine)
        {
            EngineLabel.Text = engine == EngineType.EdgeHtml ? "E" : "W";
            EngineLabel.Foreground = engine == EngineType.EdgeHtml
                ? new SolidColorBrush(Colors.DodgerBlue)
                : new SolidColorBrush(Colors.MediumSeaGreen);
            EngineCombo.SelectedIndex = engine == EngineType.EdgeHtml ? 0 : 1;
        }

        public void UpdateFavoriteButton(bool isFavorite)
        {
            AddFavBtn.Content = isFavorite ? "\xE735" : "\xE734";
            AddFavBtn.Foreground = isFavorite
                ? new SolidColorBrush(Colors.Gold)
                : new SolidColorBrush(Colors.Gray);
        }

        public void ShowMenu(MenuFlyout menu) => menu.ShowAt(MenuBtn, new Point(0, MenuBtn.ActualHeight));

        private void BackBtn_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();
        private void ForwardBtn_Click(object sender, RoutedEventArgs e) => ForwardRequested?.Invoke();
        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
        private void AddFavBtn_Click(object sender, RoutedEventArgs e) => AddFavoriteClicked?.Invoke();
        private void HubBtn_Click(object sender, RoutedEventArgs e) => HubClicked?.Invoke();
        private void MenuBtn_Click(object sender, RoutedEventArgs e) => MenuClicked?.Invoke();

        private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (_isSuggestionsVisible && _currentSuggestions.Count > 0)
                {
                    SubmitUrl(_currentSuggestions[0].Url);
                    e.Handled = true;
                    return;
                }
                string input = UrlBox.Text?.Trim();
                if (!string.IsNullOrEmpty(input))
                {
                    string url = ResolveInputToUrl(input);
                    UrlSubmitted?.Invoke(url);
                }
                CloseSuggestions();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down)
            {
                ShowSuggestionsManually();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CloseSuggestions();
                e.Handled = true;
            }
        }

        private void EnsureFlyoutPresenterStyle(double width)
        {
            var style = new Style(typeof(FlyoutPresenter));
            style.Setters.Add(new Setter(FlyoutPresenter.MinWidthProperty, width));
            style.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(0)));
            SuggestionsFlyout.FlyoutPresenterStyle = style;
        }

        private string ResolveInputToUrl(string raw)
        {
            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("edge:", StringComparison.OrdinalIgnoreCase))
                return raw;

            raw = raw.Replace('\u3002', '.').Replace('\uFF0E', '.');
            if (raw.Contains(" ")) return string.Format(SearchEngineUrl, Uri.EscapeDataString(raw));

            bool hasDot = raw.Contains('.');
            bool hasNonAscii = raw.Any(c => c > 127);
            if (!hasDot && !hasNonAscii)
            {
                if (raw.Length >= 3 && raw.All(c => char.IsLetterOrDigit(c) || c == '-'))
                    return "https://" + raw;
                return string.Format(SearchEngineUrl, Uri.EscapeDataString(raw));
            }
            if (hasDot)
            {
                string converted = ConvertToPunycode(raw);
                if (!converted.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !converted.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    converted = "https://" + converted;
                if (Uri.TryCreate(converted, UriKind.Absolute, out _)) return converted;
                return string.Format(SearchEngineUrl, Uri.EscapeDataString(raw));
            }
            if (!hasDot && hasNonAscii)
            {
                string converted = TryConvertDomain(raw);
                if (converted != null)
                {
                    string url = "https://" + converted;
                    if (Uri.TryCreate(url, UriKind.Absolute, out _)) return url;
                }
                return string.Format(SearchEngineUrl, Uri.EscapeDataString(raw));
            }
            return string.Format(SearchEngineUrl, Uri.EscapeDataString(raw));
        }

        private string ConvertToPunycode(string url) { /* 实现与之前相同 */ return url; }
        private string TryConvertDomain(string domain) { /* 实现与之前相同 */ return null; }
        private string DecodeUrl(string url) { /* 实现与之前相同 */ return url; }

        private void UrlBox_GotFocus(object sender, RoutedEventArgs e) { /* 与之前相同 */ }
        private void UrlBox_LostFocus(object sender, RoutedEventArgs e) { /* 与之前相同 */ }

        private void UrlBox_TextChanged(object sender, TextChangedEventArgs e) { /* 与之前相同 */ }
        private void OnSuggestionTimerTick(object sender, object e) { /* 与之前相同 */ }
        private void ShowSuggestionsManually() { /* 与之前相同 */ }
        private void UpdateSuggestions() { /* 与之前相同 */ }

        private Border CreateSuggestionItem(SuggestionItem item, double maxWidth) { /* 与之前相同 */ return null; }
        private List<SuggestionItem> GetSuggestions(string query) { /* 与之前相同 */ return new List<SuggestionItem>(); }

        private void CloseSuggestions() { SuggestionsFlyout.Hide(); _isSuggestionsVisible = false; }
        private void SubmitUrl(string url)
        {
            CloseSuggestions();
            UrlBox.Text = DecodeUrl(url);
            UrlSubmitted?.Invoke(url);
        }

        private void EngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EngineCombo.SelectedIndex >= 0)
            {
                EngineType engine = EngineCombo.SelectedIndex == 1 ? EngineType.WebView2 : EngineType.EdgeHtml;
                EngineSwitched?.Invoke(engine);
            }
        }

        private class SuggestionItem
        {
            public string DisplayTitle { get; set; }
            public string Url { get; set; }
            public bool IsFavorite { get; set; }
        }
    }
}