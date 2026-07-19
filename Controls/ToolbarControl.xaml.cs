using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
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

        public void ApplySkin(SkinManager.SkinColors colors)
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
            _isDarkTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark;

            SuggestionsFlyout.FlyoutPresenterStyle = null;
        }

        public void UpdateNavState(bool canGoBack, bool canGoForward, string currentUrl)
        {
            BackBtn.IsEnabled = canGoBack;
            ForwardBtn.IsEnabled = canGoForward;
            if (currentUrl == "about:blank")
            {
                UrlBox.Text = "";
            }
            else
            {
                UrlBox.Text = DecodeUrl(currentUrl);
            }
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
            {
                return raw;
            }

            raw = raw.Replace('\u3002', '.').Replace('\uFF0E', '.');

            if (raw.Contains(" "))
                return string.Format(SearchEngineUrl, Uri.EscapeDataString(raw));

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
                if (Uri.TryCreate(converted, UriKind.Absolute, out _))
                    return converted;
                return string.Format(SearchEngineUrl, Uri.EscapeDataString(raw));
            }

            if (!hasDot && hasNonAscii)
            {
                string converted = TryConvertDomain(raw);
                if (converted != null)
                {
                    string url = "https://" + converted;
                    if (Uri.TryCreate(url, UriKind.Absolute, out _))
                        return url;
                }
                return string.Format(SearchEngineUrl, Uri.EscapeDataString(raw));
            }

            return string.Format(SearchEngineUrl, Uri.EscapeDataString(raw));
        }

        private string ConvertToPunycode(string url)
        {
            try
            {
                string protocol = "";
                int protoIdx = url.IndexOf("://");
                if (protoIdx >= 0)
                {
                    protocol = url.Substring(0, protoIdx + 3);
                    url = url.Substring(protoIdx + 3);
                }
                int slashIdx = url.IndexOf('/');
                string domain = slashIdx >= 0 ? url.Substring(0, slashIdx) : url;
                string path = slashIdx >= 0 ? url.Substring(slashIdx) : "";
                if (domain.Any(c => c > 127))
                    domain = _idn.GetAscii(domain);
                return protocol + domain + path;
            }
            catch { return url; }
        }

        private string TryConvertDomain(string domain)
        {
            try
            {
                if (domain.Any(c => c > 127))
                {
                    string ascii = _idn.GetAscii(domain);
                    if (!string.IsNullOrEmpty(ascii)) return ascii;
                }
                return null;
            }
            catch { return null; }
        }

        private string DecodeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            try
            {
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    Uri uri = new Uri(url);
                    string host = uri.Host;
                    if (host.Contains("xn--"))
                    {
                        IdnMapping idn = new IdnMapping();
                        string unicodeHost = idn.GetUnicode(host);
                        return uri.Scheme + "://" + unicodeHost + uri.PathAndQuery + uri.Fragment;
                    }
                }
            }
            catch { }
            return url;
        }

        private void UrlBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var accentColor = (Color)Application.Current.Resources["SystemAccentColor"];
            UrlBox.BorderBrush = new SolidColorBrush(accentColor);
        }
        private void UrlBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UrlBox.BorderBrush = new SolidColorBrush(Colors.LightGray);
        }

        private void UrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSuggestionsVisible)
            {
                _suggestionTimer.Stop();
                _suggestionTimer.Start();
            }
        }

        private void OnSuggestionTimerTick(object sender, object e)
        {
            _suggestionTimer.Stop();
            if (_isSuggestionsVisible)
            {
                UpdateSuggestions();
                if (_currentSuggestions.Count == 0) CloseSuggestions();
            }
        }

        private void ShowSuggestionsManually()
        {
            if (string.IsNullOrWhiteSpace(UrlBox.Text)) { CloseSuggestions(); return; }
            UpdateSuggestions();
            if (_currentSuggestions.Count > 0)
            {
                if (!_isSuggestionsVisible)
                {
                    EnsureFlyoutPresenterStyle(UrlBox.ActualWidth);
                    FlyoutBase.ShowAttachedFlyout(UrlBox);
                    _isSuggestionsVisible = true;
                }
            }
            else CloseSuggestions();
        }

        private void UpdateSuggestions()
        {
            string query = UrlBox.Text?.Trim() ?? "";
            _currentSuggestions = GetSuggestions(query);
            SuggestionsPanel.Children.Clear();
            double maxWidth = Math.Max(UrlBox.ActualWidth - 48, 200);
            foreach (var s in _currentSuggestions)
            {
                if (SuggestionsPanel.Children.Count > 0)
                    SuggestionsPanel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Colors.LightGray), Margin = new Thickness(8, 0, 8, 0) });
                SuggestionsPanel.Children.Add(CreateSuggestionItem(s, maxWidth));
            }
        }

        private Border CreateSuggestionItem(SuggestionItem item, double maxWidth)
        {
            var border = new Border { Background = new SolidColorBrush(Colors.Transparent), Height = 38, Padding = new Thickness(12, 0, 12, 0) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new FontIcon
            {
                Glyph = item.IsFavorite ? "\uE734" : "\uE81C",
                FontSize = 14,
                Foreground = _mutedForegroundBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(icon, 0);

            var text = new TextBlock
            {
                Text = $"{item.DisplayTitle}  —  {item.Url}",
                FontSize = 13,
                Foreground = _foregroundBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = maxWidth
            };
            Grid.SetColumn(text, 1);

            grid.Children.Add(icon);
            grid.Children.Add(text);
            border.Child = grid;

            border.Tapped += (s, e) => { SubmitUrl(item.Url); e.Handled = true; };

            Color hoverColor = _isDarkTheme ? Color.FromArgb(0xFF, 0x1A, 0x3A, 0x5C) : Color.FromArgb(0xFF, 0xD9, 0xEA, 0xF7);
            Color normalColor = Colors.Transparent;
            border.PointerEntered += (s, e) => border.Background = new SolidColorBrush(hoverColor);
            border.PointerExited += (s, e) => border.Background = new SolidColorBrush(normalColor);

            return border;
        }

        private List<SuggestionItem> GetSuggestions(string query)
        {
            var results = new List<SuggestionItem>();
            if (string.IsNullOrWhiteSpace(query)) return results;
            string lq = query.ToLower();
            var favs = FavoritesManager.Instance.Favorites
                .Where(f => f.Title.ToLower().Contains(lq) || f.Url.ToLower().Contains(lq))
                .Take(5)
                .Select(f => new SuggestionItem { DisplayTitle = f.Title, Url = f.Url, IsFavorite = true });
            var hists = HistoryManager.History
                .Where(h => h.Title.ToLower().Contains(lq) || h.Url.ToLower().Contains(lq))
                .Take(5)
                .Select(h => new SuggestionItem { DisplayTitle = h.Title, Url = h.Url, IsFavorite = false });
            results.AddRange(favs);
            foreach (var h in hists)
                if (!results.Any(r => r.Url == h.Url)) results.Add(h);
            return results.Take(8).ToList();
        }

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