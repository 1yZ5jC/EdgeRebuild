using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        public ToolbarControl()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _suggestionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _suggestionTimer.Tick += OnSuggestionTimerTick;

            SuggestionsFlyout.Closed += (_, _) =>
            {
                _isSuggestionsVisible = false;
            };
        }

        // ---- 公开方法 ----

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
        }

        public void UpdateNavState(bool canGoBack, bool canGoForward, string currentUrl)
        {
            BackBtn.IsEnabled = canGoBack;
            ForwardBtn.IsEnabled = canGoForward;
            UrlBox.Text = currentUrl;
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

        public void ShowMenu(MenuFlyout menu)
        {
            menu.ShowAt(MenuBtn, new Point(0, MenuBtn.ActualHeight));
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();
        private void ForwardBtn_Click(object sender, RoutedEventArgs e) => ForwardRequested?.Invoke();
        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
        private void AddFavBtn_Click(object sender, RoutedEventArgs e) => AddFavoriteClicked?.Invoke();
        private void HubBtn_Click(object sender, RoutedEventArgs e) => HubClicked?.Invoke();
        private void MenuBtn_Click(object sender, RoutedEventArgs e) => MenuClicked?.Invoke();

        // ---- 键盘事件 ----

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
                    if (!input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                        !input.StartsWith("about:", StringComparison.OrdinalIgnoreCase) &&
                        !input.StartsWith("edge:", StringComparison.OrdinalIgnoreCase) &&
                        !input.Contains("://"))
                        input = "https://" + input;
                    UrlSubmitted?.Invoke(input);
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

        // ---- 焦点 ----
        private void UrlBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var accentColor = (Color)Application.Current.Resources["SystemAccentColor"];
            UrlBox.BorderBrush = new SolidColorBrush(accentColor);
        }

        private void UrlBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UrlBox.BorderBrush = new SolidColorBrush(Colors.LightGray);
        }

        // ---- 输入文字变化 ----
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
                if (_currentSuggestions.Count == 0)
                {
                    CloseSuggestions();
                }
            }
        }

        // ---- 手动打开建议 ----
        private void ShowSuggestionsManually()
        {
            if (string.IsNullOrWhiteSpace(UrlBox.Text))
            {
                CloseSuggestions();
                return;
            }

            UpdateSuggestions();

            if (_currentSuggestions.Count > 0)
            {
                if (!_isSuggestionsVisible)
                {
                    SuggestionBorder.MinWidth = UrlBox.ActualWidth;
                    FlyoutBase.ShowAttachedFlyout(UrlBox);
                    _isSuggestionsVisible = true;
                }
            }
            else
            {
                CloseSuggestions();
            }
        }

        private void UpdateSuggestions()
        {
            string query = UrlBox.Text?.Trim() ?? "";
            _currentSuggestions = GetSuggestions(query);
            SuggestionsPanel.Children.Clear();

            double maxWidth = Math.Max(UrlBox.ActualWidth - 48, 200); // 预留图标和边距

            foreach (var suggestion in _currentSuggestions)
            {
                if (SuggestionsPanel.Children.Count > 0)
                {
                    SuggestionsPanel.Children.Add(new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush(Colors.LightGray),
                        Margin = new Thickness(8, 0, 8, 0)
                    });
                }
                SuggestionsPanel.Children.Add(CreateSuggestionItem(suggestion, maxWidth));
            }
        }

        private Border CreateSuggestionItem(SuggestionItem item, double maxWidth)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                Height = 38,
                Padding = new Thickness(12, 0, 12, 0)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 图标：收藏用星标，历史用时钟
            var icon = new FontIcon
            {
                Glyph = item.IsFavorite ? "\uE734" : "\uE81C",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x66, 0x66, 0x66)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(icon, 0);

            // 文本：标题  —  地址
            var textBlock = new TextBlock
            {
                Text = $"{item.DisplayTitle}  —  {item.Url}",
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.Black),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = maxWidth
            };
            Grid.SetColumn(textBlock, 1);

            grid.Children.Add(icon);
            grid.Children.Add(textBlock);
            border.Child = grid;

            // 点击事件
            border.Tapped += (s, e) =>
            {
                SubmitUrl(item.Url);
                e.Handled = true;
            };

            // 悬停效果
            border.PointerEntered += (s, e) => border.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3));
            border.PointerExited += (s, e) => border.Background = new SolidColorBrush(Colors.Transparent);

            return border;
        }

        private List<SuggestionItem> GetSuggestions(string query)
        {
            var results = new List<SuggestionItem>();
            if (string.IsNullOrWhiteSpace(query))
                return results;

            string lowerQuery = query.ToLower();

            // 优先展示收藏夹（最多5条）
            var favoriteMatches = FavoritesManager.Instance.Favorites
                .Where(f => f.Title.ToLower().Contains(lowerQuery) || f.Url.ToLower().Contains(lowerQuery))
                .Take(5)
                .Select(f => new SuggestionItem { DisplayTitle = f.Title, Url = f.Url, IsFavorite = true });

            // 历史记录（最多5条）
            var historyMatches = HistoryManager.History
                .Where(h => h.Title.ToLower().Contains(lowerQuery) || h.Url.ToLower().Contains(lowerQuery))
                .Take(5)
                .Select(h => new SuggestionItem { DisplayTitle = h.Title, Url = h.Url, IsFavorite = false });

            // 合并，去重（收藏优先）
            results.AddRange(favoriteMatches);
            foreach (var h in historyMatches)
            {
                if (!results.Any(r => r.Url == h.Url))
                    results.Add(h);
            }

            return results.Take(8).ToList();
        }

        private void CloseSuggestions()
        {
            SuggestionsFlyout.Hide();
            _isSuggestionsVisible = false;
        }

        private void SubmitUrl(string url)
        {
            CloseSuggestions();
            UrlBox.Text = url;
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