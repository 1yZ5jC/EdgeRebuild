using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
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
            ConfigureSuggestionsFlyout();
            UpdateAddressChrome();
            UpdateSiteInfo();
        }

        private string _currentUrl = "";
        private string _currentHost = "";
        private bool _currentSecure;
        private bool _currentLocal = true;
        private readonly List<SuggestionItem> _suggestions = new List<SuggestionItem>();

        public void UpdateNavButtons(bool canGoBack, bool canGoForward)
        {
            BackBtn.IsEnabled = canGoBack;
            ForwardBtn.IsEnabled = canGoForward;
            BackBtn.Foreground = new SolidColorBrush(canGoBack ? Colors.White : MetroColorPalette.MutedColor);
            ForwardBtn.Foreground = new SolidColorBrush(canGoForward ? Colors.White : MetroColorPalette.MutedColor);
        }

        /// <summary>设置页开关：关闭时去掉圆形按钮的外围描边，只保留图标。</summary>
        public void SetCircleButtonsVisible(bool visible)
        {
            _showCircleButtons = visible;
            ApplyCircleRing(BackBtn);
            ApplyCircleRing(ForwardBtn);
            ApplyCircleRing(RefreshBtn);
            ApplyCircleRing(FavoriteBtn);
            ApplyCircleRing(MoreBtn);
            ApplyCircleRing(GoBtn);
        }

        private bool _showCircleButtons = true;

        private void ApplyCircleRing(Button button)
        {
            var thickness = _showCircleButtons ? new Thickness(3) : new Thickness(0);
            var brush = new SolidColorBrush(_showCircleButtons
                ? Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)
                : Colors.Transparent);
            button.BorderThickness = thickness;
            button.BorderBrush = brush;

            // 1809 上 TemplateBinding 对运行时本地值更新不稳定：模板已展开时
            // 直接同步模板内 Border，保证开关立即生效。
            var bg = FindDescendantBorder(button, "Bg");
            if (bg != null)
            {
                bg.BorderThickness = thickness;
                bg.BorderBrush = brush;
            }
        }

        private static Border? FindDescendantBorder(DependencyObject root, string name)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Border border && border.Name == name) return border;
                var found = FindDescendantBorder(child, name);
                if (found != null) return found;
            }
            return null;
        }

        public void SetUrl(string url)
        {
            _currentUrl = url ?? "";
            UpdateSiteInfo();
            if (UrlBox.FocusState == FocusState.Unfocused)
            {
                UrlBox.Text = DecodeUrl(_currentUrl);
                UpdateAddressChrome();
            }
        }

        /// <summary>显示地址时把 Punycode 主机名还原成 Unicode（与主窗口地址栏一致）。</summary>
        private static string DecodeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            try
            {
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(url);
                    string host = uri.Host;
                    if (host.Contains("xn--"))
                    {
                        string unicodeHost = new IdnMapping().GetUnicode(host);
                        return uri.Scheme + "://" + unicodeHost + uri.PathAndQuery + uri.Fragment;
                    }
                }
            }
            catch { }
            return url;
        }

        /// <summary>当前页已收藏时星标显示实心；点击始终打开收藏夹面板。</summary>
        public void SetFavoriteState(bool isFavorite)
        {
            FavGlyph.Glyph = isFavorite ? "\uE735" : "\uE734";
            FavGlyph.Foreground = new SolidColorBrush(isFavorite ? MetroColorPalette.MetroBlue : Colors.White);
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

        private void SubmitUrl(string? url = null)
        {
            string input = (url ?? UrlBox.Text ?? "").Trim();
            if (input.Length == 0) return;
            string target = ResolveEngineShortcut(input) ?? input;
            CloseSuggestions();
            UrlSubmitted?.Invoke(target);
            ContentAreaFocusRequested?.Invoke();   // 焦点还给页面内容
        }

        /// <summary>主窗口同款引擎前缀：@g / @b / @ddg + 搜索词。</summary>
        private static string? ResolveEngineShortcut(string input)
        {
            if (!input.StartsWith("@", StringComparison.Ordinal)) return null;
            int space = input.IndexOf(' ');
            if (space <= 1) return null;
            string name = input.Substring(1, space - 1).ToLowerInvariant();
            string query = input.Substring(space + 1).Trim();
            if (query.Length == 0) return null;
            return name switch
            {
                "g" or "google" => $"https://www.google.com/search?q={Uri.EscapeDataString(query)}",
                "b" or "bing" => $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}",
                "ddg" or "duckduckgo" => $"https://duckduckgo.com/?q={Uri.EscapeDataString(query)}",
                _ => null
            };
        }

        private void UrlBox_GotFocus(object sender, RoutedEventArgs e)
        {
            UrlFrame.Background = new SolidColorBrush(Colors.White);
            UrlFrame.BorderBrush = new SolidColorBrush(MetroColorPalette.MetroBlue);
            UrlBox.Foreground = new SolidColorBrush(Colors.Black);
            SiteInfoGlyph.Foreground = new SolidColorBrush(Colors.Black);
            UpdateAddressChrome();
        }

        private void UrlBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UrlFrame.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F));
            UrlFrame.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            UrlBox.Foreground = new SolidColorBrush(Colors.White);
            SiteInfoGlyph.Foreground = new SolidColorBrush(Colors.White);
            UpdateAddressChrome();
        }

        private void UrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateAddressChrome();

            if (UrlBox.FocusState == FocusState.Unfocused || string.IsNullOrWhiteSpace(UrlBox.Text))
            {
                CloseSuggestions();
                return;
            }

            UpdateSuggestions();
            if (_suggestions.Count > 0) ShowSuggestions();
            else CloseSuggestions();
        }

        private void UpdateAddressChrome()
        {
            if (ClearBtn == null || GoBtn == null || UrlBox == null || FavoriteBtn == null) return;

            bool focused = UrlBox.FocusState != FocusState.Unfocused;
            bool hasText = !string.IsNullOrEmpty(UrlBox.Text);
            ClearBtn.Visibility = focused && hasText ? Visibility.Visible : Visibility.Collapsed;
            GoBtn.IsEnabled = !string.IsNullOrWhiteSpace(UrlBox.Text);
            if (PlaceholderOverlay != null)
                PlaceholderOverlay.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            UrlBox.Text = "";
            UrlBox.Focus(FocusState.Programmatic);
            CloseSuggestions();
            UpdateAddressChrome();
        }

        private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                if (SuggestionsFlyout.IsOpen && _suggestions.Count > 0)
                    SubmitUrl(_suggestions[0].Url);
                else
                    SubmitUrl();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Down)
            {
                UpdateSuggestions();
                if (_suggestions.Count > 0) ShowSuggestions();
                else CloseSuggestions();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Tab)
            {
                UpdateSuggestions();
                if (_suggestions.Count > 0) ShowSuggestions();
                else CloseSuggestions();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                if (SuggestionsFlyout.IsOpen)
                {
                    CloseSuggestions();
                }
                else
                {
                    // 恢复显示当前页地址（而非清空）
                    UrlBox.Text = DecodeUrl(_currentUrl);
                    UrlBox.SelectionStart = UrlBox.Text.Length;
                    ContentAreaFocusRequested?.Invoke();
                }
                UpdateAddressChrome();
                e.Handled = true;
            }
        }

        // ---- 站点信息（参考主窗口地址栏：左侧安全/本地图标 + 说明） ----
        private void UpdateSiteInfo()
        {
            string url = _currentUrl ?? "";
            _currentLocal = string.IsNullOrEmpty(url)
                || url == "about:blank"
                || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("edge://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

            _currentHost = "";
            _currentSecure = false;

            if (!_currentLocal && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                _currentHost = uri.Host ?? "";
                _currentSecure = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrEmpty(_currentHost)) _currentLocal = true;
            }

            if (_currentLocal)
            {
                SiteInfoGlyph.Glyph = "\uE80F";   // Home
                ToolTipService.SetToolTip(SiteInfoBtn, "本地界面");
            }
            else
            {
                SiteInfoGlyph.Glyph = _currentSecure ? "\uE72E" : "\uE774";
                ToolTipService.SetToolTip(SiteInfoBtn,
                    _currentSecure ? $"安全连接 - {_currentHost}" : $"普通连接 - {_currentHost}");
            }

            SiteInfoGlyph.Foreground = new SolidColorBrush(
                UrlBox.FocusState == FocusState.Unfocused ? Colors.White : Colors.Black);
        }

        private void SiteInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            var flyout = new MenuFlyout();
            string text = _currentLocal
                ? "你正在查看的是浏览器提供的本地界面"
                : _currentSecure
                    ? $"与 {_currentHost} 的连接是安全的"
                    : $"与 {_currentHost} 的连接未加密";
            flyout.Items.Add(new MenuFlyoutItem { Text = text, IsEnabled = false });
            flyout.ShowAt(SiteInfoBtn, new Point(0, SiteInfoBtn.ActualHeight));
        }

        // ---- 地址栏建议（收藏 + 历史；参考主窗口地址栏） ----
        private void ConfigureSuggestionsFlyout()
        {
            var style = new Style(typeof(FlyoutPresenter));
            style.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(0)));
            style.Setters.Add(new Setter(FlyoutPresenter.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F))));
            style.Setters.Add(new Setter(FlyoutPresenter.BorderBrushProperty,
                new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x33, 0x33))));
            style.Setters.Add(new Setter(FlyoutPresenter.BorderThicknessProperty, new Thickness(1)));
            SuggestionsFlyout.FlyoutPresenterStyle = style;
        }

        private void UpdateSuggestions()
        {
            _suggestions.Clear();
            _suggestions.AddRange(GetSuggestions(UrlBox.Text ?? ""));

            SuggestionsPanel.Children.Clear();
            if (_suggestions.Count == 0) return;

            SuggestionBorder.Width = Math.Max(UrlBox.ActualWidth, 300);
            for (int i = 0; i < _suggestions.Count; i++)
            {
                if (i > 0)
                {
                    SuggestionsPanel.Children.Add(new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x33, 0x33)),
                        Margin = new Thickness(8, 0, 8, 0)
                    });
                }
                SuggestionsPanel.Children.Add(CreateSuggestionItem(_suggestions[i]));
            }
        }

        private Border CreateSuggestionItem(SuggestionItem item)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                Height = 46,
                Padding = new Thickness(12, 0, 12, 0)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new FontIcon
            {
                Glyph = item.IsPopular ? "\uE721" : item.IsFavorite ? "\uE734" : "\uE81C",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = new SolidColorBrush(item.IsFavorite ? MetroColorPalette.MetroBlue : MetroColorPalette.MutedColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(icon, 0);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = item.DisplayTitle,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            text.Children.Add(new TextBlock
            {
                Text = item.Url,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Foreground = new SolidColorBrush(MetroColorPalette.MutedColor),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(text, 1);

            grid.Children.Add(icon);
            grid.Children.Add(text);
            border.Child = grid;

            border.Tapped += (s, e) =>
            {
                SubmitUrl(item.Url);
                e.Handled = true;
            };
            border.PointerEntered += (s, e) =>
                border.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            border.PointerExited += (s, e) =>
                border.Background = new SolidColorBrush(Colors.Transparent);
            AutomationProperties.SetName(border, "打开建议 " + item.DisplayTitle);

            return border;
        }

        private List<SuggestionItem> GetSuggestions(string query)
        {
            var results = new List<SuggestionItem>();
            string q = (query ?? "").Trim();

            // 空地址栏按 ↓/Tab：给常用搜索主页（与主窗口建议一致）
            if (q.Length == 0)
            {
                string engine = "Google";
                try { engine = SettingsManager.GetCached("SearchEngine") ?? "Google"; } catch { }
                var homes = new (string Name, string Url)[]
                {
                    ("Google", "https://www.google.com"),
                    ("Bing", "https://www.bing.com"),
                    ("DuckDuckGo", "https://duckduckgo.com")
                };
                int current = Array.FindIndex(homes,
                    h => h.Name.Equals(engine, StringComparison.OrdinalIgnoreCase));
                if (current > 0)
                {
                    var first = homes[0];
                    homes[0] = homes[current];
                    homes[current] = first;
                }
                foreach (var home in homes)
                {
                    results.Add(new SuggestionItem
                    {
                        DisplayTitle = home.Name + " 主页",
                        Url = home.Url,
                        IsPopular = true
                    });
                }
                return results;
            }

            string lq = q.ToLowerInvariant();

            try
            {
                foreach (var favorite in FavoritesManager.Instance.Favorites
                    .Where(f => (f.Title ?? "").ToLowerInvariant().Contains(lq)
                             || (f.Url ?? "").ToLowerInvariant().Contains(lq))
                    .Take(4))
                {
                    results.Add(new SuggestionItem
                    {
                        DisplayTitle = string.IsNullOrWhiteSpace(favorite.Title) ? favorite.Url : favorite.Title,
                        Url = favorite.Url,
                        IsFavorite = true
                    });
                }
            }
            catch { }

            try
            {
                foreach (var history in HistoryManager.History
                    .Where(h => (h.Title ?? "").ToLowerInvariant().Contains(lq)
                             || (h.Url ?? "").ToLowerInvariant().Contains(lq))
                    .Take(5))
                {
                    if (string.IsNullOrEmpty(history.Url) || history.Url == "about:blank") continue;
                    if (results.Any(r => string.Equals(r.Url, history.Url, StringComparison.OrdinalIgnoreCase))) continue;
                    results.Add(new SuggestionItem
                    {
                        DisplayTitle = string.IsNullOrWhiteSpace(history.Title) ? history.Url : history.Title,
                        Url = history.Url,
                        IsFavorite = false
                    });
                }
            }
            catch { }

            return results.Take(8).ToList();
        }

        private void ShowSuggestions()
        {
            if (_suggestions.Count == 0) return;
            SuggestionBorder.Width = Math.Max(UrlBox.ActualWidth, 300);
            if (!SuggestionsFlyout.IsOpen)
                SuggestionsFlyout.ShowAt(UrlBox);
        }

        private void CloseSuggestions()
        {
            if (SuggestionsFlyout.IsOpen)
                SuggestionsFlyout.Hide();
        }

        private void SuggestionsFlyout_Closed(object sender, object e)
        {
            // 让下一次输入可以重新打开；无需清空列表。
        }

        private sealed class SuggestionItem
        {
            public string DisplayTitle { get; set; } = "";
            public string Url { get; set; } = "";
            public bool IsFavorite { get; set; }
            public bool IsPopular { get; set; }
        }
    }
}
