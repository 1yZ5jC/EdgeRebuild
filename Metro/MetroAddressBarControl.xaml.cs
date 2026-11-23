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
        public event Action? ReaderRequested;
        public event Action? FavoriteRequested;
        public event Action? MoreRequested;
        public event Action<string>? UrlSubmitted;
        public event Action? ContentAreaFocusRequested;   // Esc/提交后把焦点还给页面内容

        public MetroAddressBarControl()
        {
            InitializeComponent();

            // 三列建议面板的列顺序：常用 / 搜索 / 网页（键盘 ←→ 按此顺序移动）
            _wideColumns = new[] { _widePopular, _wideSearches, _widePages };

            // 圆圈颜色跟随按钮可用状态：禁用时灰圈，启用时白圈。
            // 仅靠模板 Disabled 视觉状态会被 SetCircleButtonsVisible 的本地值覆盖。
            foreach (var button in new[] { BackBtn, ForwardBtn, RefreshBtn, ReaderBtn, FavoriteBtn, MoreBtn, GoBtn })
                button.IsEnabledChanged += (s, e) => ApplyCircleRing((Button)s);

            UpdateNavButtons(false, false);
            ConfigureSuggestionsFlyout();
            UpdateAddressChrome();
            UpdateSiteInfo();
        }

        private string _currentUrl = "";
        private string _currentHost = "";
        private bool _currentSecure;
        private bool _currentLocal = true;
        private bool _compactSuggestions;   // 调试开关；默认 false = 三列建议面板
        private readonly List<SuggestionItem> _suggestions = new List<SuggestionItem>();
        private readonly List<SuggestionItem> _widePopular = new List<SuggestionItem>();
        private readonly List<SuggestionItem> _wideSearches = new List<SuggestionItem>();
        private readonly List<SuggestionItem> _widePages = new List<SuggestionItem>();
        private readonly List<SuggestionItem>[] _wideColumns;
        private readonly Dictionary<SuggestionItem, Border> _wideBorders = new Dictionary<SuggestionItem, Border>();
        private Border? _selectedWideBorder;
        private int _wideColumn = -1;
        private int _wideRow = -1;

        // 顺序即“常用”列里引擎主页的先后；当前搜索引擎会被提到最前。
        private static readonly (string Name, string Home)[] SearchEngines =
        {
            ("Google", "https://www.google.com"),
            ("Bing", "https://www.bing.com"),
            ("DuckDuckGo", "https://duckduckgo.com")
        };

        /// <summary>调试开关：true = 旧版单列 Flyout；false（默认）= 底部全宽三列建议面板。</summary>
        public void SetCompactSuggestionsEnabled(bool enabled)
        {
            if (_compactSuggestions == enabled) return;
            _compactSuggestions = enabled;
            if (SuggestionsFlyout.IsOpen) CloseSuggestions();
        }

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
            ApplyCircleRing(ReaderBtn);
            ApplyCircleRing(FavoriteBtn);
            ApplyCircleRing(MoreBtn);
            ApplyCircleRing(GoBtn);
        }

        private bool _showCircleButtons = true;

        private static readonly Color RingEnabledColor = Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF);
        private static readonly Color RingDisabledColor = Color.FromArgb(0xFF, 0x55, 0x55, 0x55);

        private void ApplyCircleRing(Button button)
        {
            var thickness = _showCircleButtons ? new Thickness(3) : new Thickness(0);
            var brush = new SolidColorBrush(!_showCircleButtons
                ? Colors.Transparent
                : (button.IsEnabled ? RingEnabledColor : RingDisabledColor));
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

        /// <summary>阅读按钮：文章页可用或正在阅读时显示；阅读中图标高亮。</summary>
        public void SetReaderState(bool available, bool active)
        {
            bool show = available || active;
            ReaderBtn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            ReaderGlyph.Foreground = new SolidColorBrush(active ? MetroColorPalette.MetroBlue : Colors.White);
            string tip = active ? "退出阅读模式" : "阅读模式";
            ToolTipService.SetToolTip(ReaderBtn, tip);
            AutomationProperties.SetName(ReaderBtn, tip);
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
        private void ReaderBtn_Click(object sender, RoutedEventArgs e) => ReaderRequested?.Invoke();
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
            if (HasAnySuggestions()) ShowSuggestions();
            else CloseSuggestions();
        }

        private bool HasAnySuggestions()
        {
            return _compactSuggestions ? _suggestions.Count > 0 : HasAnyWideItems();
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
                // 三列模式有选中项时打开选中项；没有选中项时按输入内容解析，
                // 避免把 github.com 这类网址当成搜索词。
                string? selectedUrl = GetSelectedWideUrl();
                if (selectedUrl != null)
                    SubmitUrl(selectedUrl);
                else if (_compactSuggestions && SuggestionsFlyout.IsOpen && _suggestions.Count > 0)
                    SubmitUrl(_suggestions[0].Url);
                else
                    SubmitUrl();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Down)
            {
                if (_compactSuggestions)
                {
                    UpdateSuggestions();
                    if (_suggestions.Count > 0) ShowSuggestions();
                    else CloseSuggestions();
                }
                else
                {
                    HandleWideDownKey();
                }
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Tab)
            {
                if (_compactSuggestions)
                {
                    UpdateSuggestions();
                    if (_suggestions.Count > 0) ShowSuggestions();
                    else CloseSuggestions();
                }
                else
                {
                    HandleWideDownKey();
                }
                e.Handled = true;
            }
            else if (!_compactSuggestions && e.Key == VirtualKey.Up)
            {
                if (HasWideSelection)
                {
                    MoveWideSelection(0, -1);
                    e.Handled = true;
                }
            }
            else if (!_compactSuggestions && e.Key == VirtualKey.Left)
            {
                // 只在已用 ↑↓ 选中建议时拦截；平时保留地址框内的光标移动。
                if (HasWideSelection)
                {
                    MoveWideSelection(-1, 0);
                    e.Handled = true;
                }
            }
            else if (!_compactSuggestions && e.Key == VirtualKey.Right)
            {
                if (HasWideSelection)
                {
                    MoveWideSelection(1, 0);
                    e.Handled = true;
                }
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
                new SolidColorBrush(Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A))));
            style.Setters.Add(new Setter(FlyoutPresenter.BorderThicknessProperty, new Thickness(1)));
            // FlyoutPresenter 的主题样式可能带 MaxWidth/MaxHeight 限制；三列模式需要横跨窗口，
            // 这里显式放开，尺寸由内容/地址栏宽度控制。
            style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 10000d));
            style.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, 10000d));
            SuggestionsFlyout.FlyoutPresenterStyle = style;
        }

        private void UpdateSuggestions()
        {
            _suggestions.Clear();

            if (!_compactSuggestions)
            {
                UpdateWideSuggestions();
                return;
            }

            _suggestions.AddRange(GetSuggestions(UrlBox.Text ?? ""));

            SuggestionsPanel.Children.Clear();
            if (_suggestions.Count == 0) return;

            SuggestionBorder.Width = Math.Max(UrlBox.ActualWidth, 360);
            for (int i = 0; i < _suggestions.Count; i++)
            {
                if (i > 0)
                {
                    SuggestionsPanel.Children.Add(new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A)),
                        Margin = new Thickness(12, 0, 12, 0)
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
                Height = 54,
                Padding = new Thickness(16, 0, 16, 0)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new FontIcon
            {
                Glyph = item.IsSearch ? "\uE721"
                    : item.IsPopular ? "\uE721"
                    : item.IsFavorite ? "\uE734" : "\uE81C",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = new SolidColorBrush(
                    item.IsFavorite || item.IsSearch ? MetroColorPalette.MetroBlue : MetroColorPalette.MutedColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(icon, 0);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = item.DisplayTitle,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 15,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (item.ShowSubtitle)
            {
                text.Children.Add(new TextBlock
                {
                    Text = item.Url,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(MetroColorPalette.MutedColor),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }
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
                border.Background = ReferenceEquals(border, _selectedWideBorder)
                    ? new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x78, 0xD7))
                    : new SolidColorBrush(Colors.Transparent);
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

        // ---- 底部全宽三列建议（可选模式；只使用本地收藏/历史，不接在线联想） ----
        private void UpdateWideSuggestions()
        {
            string q = (UrlBox.Text ?? "").Trim();
            ClearWideSelection();
            _wideBorders.Clear();
            _widePopular.Clear();
            _wideSearches.Clear();
            _widePages.Clear();

            var favorites = GetFavoriteMatches(q);
            var history = GetHistoryMatches(q);
            var engines = GetOrderedEngines(GetSearchEngineName());

            if (q.Length == 0)
            {
                // 常用：收藏；还没有收藏时显示空状态。
                foreach (var favorite in favorites.Take(6))
                    _widePopular.Add(CreateFavoriteSuggestion(favorite));

                // 搜索：三家引擎主页，当前引擎排最前。
                foreach (var engine in engines)
                {
                    _wideSearches.Add(new SuggestionItem
                    {
                        DisplayTitle = engine.Name + " 主页",
                        Url = engine.Home,
                        IsPopular = true
                    });
                }
            }
            else
            {
                foreach (var favorite in favorites.Take(6))
                    _widePopular.Add(CreateFavoriteSuggestion(favorite));

                foreach (var engine in engines)
                {
                    _wideSearches.Add(new SuggestionItem
                    {
                        DisplayTitle = string.Format("用 {0} 搜索“{1}”", engine.Name, q),
                        Url = BuildSearchUrl(engine.Name, q),
                        IsSearch = true,
                        ShowSubtitle = false
                    });
                }
            }

            // 网页：历史匹配 + 剩余收藏匹配；按 URL 去重，并排除“常用”列已出现的项。
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in _widePopular)
                seen.Add(UrlKey(item.Url));

            if (q.Length > 0)
            {
                foreach (var favorite in favorites.Skip(6))
                {
                    if (_widePages.Count >= 12) break;
                    AddWidePage(CreateFavoriteSuggestion(favorite), seen);
                }
            }

            foreach (var entry in history)
            {
                if (_widePages.Count >= 12) break;
                AddWidePage(new SuggestionItem
                {
                    DisplayTitle = string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title,
                    Url = entry.Url,
                    IsFavorite = false
                }, seen);
            }

            _suggestions.AddRange(_widePopular);
            _suggestions.AddRange(_wideSearches);
            _suggestions.AddRange(_widePages);

            PopulateWideColumn(WidePopularPanel, _widePopular,
                q.Length == 0 ? "还没有收藏" : "没有匹配的收藏");
            PopulateWideColumn(WideSearchPanel, _wideSearches, "没有可用的搜索引擎");
            PopulateWideColumn(WidePagePanel, _widePages,
                q.Length == 0 ? "暂无浏览记录" : "没有匹配的网页");
        }

        private void PopulateWideColumn(StackPanel panel, List<SuggestionItem> items, string emptyText)
        {
            panel.Children.Clear();
            if (items.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = emptyText,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 13,
                    Foreground = new SolidColorBrush(MetroColorPalette.MutedColor),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20, 6, 20, 0)
                });
                return;
            }

            foreach (var item in items)
            {
                var border = CreateSuggestionItem(item);
                _wideBorders[item] = border;
                panel.Children.Add(border);
            }
        }

        private void AddWidePage(SuggestionItem item, HashSet<string> seen)
        {
            string key = UrlKey(item.Url);
            if (key.Length == 0 || !seen.Add(key)) return;
            _widePages.Add(item);
        }

        // ---- 三列面板键盘选择：↑↓ 列内循环，←→ 切换非空列，Enter 打开 ----
        private bool HasWideSelection =>
            _wideColumn >= 0 && _wideColumn < _wideColumns.Length &&
            _wideRow >= 0 && _wideRow < _wideColumns[_wideColumn].Count;

        private string? GetSelectedWideUrl()
        {
            if (_compactSuggestions || !HasWideSelection) return null;
            return _wideColumns[_wideColumn][_wideRow].Url;
        }

        private void HandleWideDownKey()
        {
            if (!SuggestionsFlyout.IsOpen)
            {
                UpdateSuggestions();
                if (HasAnyWideItems())
                {
                    ShowSuggestions();
                    SelectFirstWideItem();
                }
                else
                {
                    CloseSuggestions();
                }
                return;
            }

            if (HasWideSelection)
                MoveWideSelection(0, 1);
            else
                SelectFirstWideItem();
        }

        private bool HasAnyWideItems()
        {
            for (int c = 0; c < _wideColumns.Length; c++)
                if (_wideColumns[c].Count > 0) return true;
            return false;
        }

        private void SelectFirstWideItem()
        {
            for (int c = 0; c < _wideColumns.Length; c++)
            {
                if (_wideColumns[c].Count == 0) continue;
                _wideColumn = c;
                _wideRow = 0;
                ApplyWideSelection();
                return;
            }
            ClearWideSelection();
        }

        private void MoveWideSelection(int columnDelta, int rowDelta)
        {
            if (!HasWideSelection) return;

            if (columnDelta != 0)
            {
                int c = _wideColumn + columnDelta;
                while (c >= 0 && c < _wideColumns.Length)
                {
                    if (_wideColumns[c].Count > 0)
                    {
                        _wideColumn = c;
                        _wideRow = Math.Min(_wideRow, _wideColumns[c].Count - 1);
                        ApplyWideSelection();
                        return;
                    }
                    c += columnDelta;
                }
                return;
            }

            if (rowDelta != 0)
            {
                int count = _wideColumns[_wideColumn].Count;
                if (count == 0) return;
                int r = _wideRow + rowDelta;
                if (r < 0) r = count - 1;
                else if (r >= count) r = 0;
                _wideRow = r;
                ApplyWideSelection();
            }
        }

        private void ApplyWideSelection()
        {
            if (_selectedWideBorder != null)
            {
                _selectedWideBorder.Background = new SolidColorBrush(Colors.Transparent);
                _selectedWideBorder = null;
            }

            if (!HasWideSelection) return;
            if (!_wideBorders.TryGetValue(_wideColumns[_wideColumn][_wideRow], out var border))
                return;

            border.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x78, 0xD7));
            _selectedWideBorder = border;
            try { border.StartBringIntoView(); } catch { }
        }

        private void ClearWideSelection()
        {
            if (_selectedWideBorder != null)
            {
                _selectedWideBorder.Background = new SolidColorBrush(Colors.Transparent);
                _selectedWideBorder = null;
            }
            _wideColumn = -1;
            _wideRow = -1;
        }

        private static string GetSearchEngineName()
        {
            try { return SettingsManager.GetCached("SearchEngine") ?? "Google"; }
            catch { return "Google"; }
        }

        private static List<(string Name, string Home)> GetOrderedEngines(string current)
        {
            var list = SearchEngines.ToList();
            int index = list.FindIndex(e => e.Name.Equals(current, StringComparison.OrdinalIgnoreCase));
            if (index > 0)
            {
                var engine = list[index];
                list.RemoveAt(index);
                list.Insert(0, engine);
            }
            return list;
        }

        private static string BuildSearchUrl(string engine, string query)
        {
            string encoded = Uri.EscapeDataString(query);
            return engine switch
            {
                "Bing" => "https://www.bing.com/search?q=" + encoded,
                "DuckDuckGo" => "https://duckduckgo.com/?q=" + encoded,
                _ => "https://www.google.com/search?q=" + encoded
            };
        }

        private static List<FavoriteItem> GetFavoriteMatches(string query)
        {
            try
            {
                var all = FavoritesManager.Instance.Favorites
                    .Where(f => !string.IsNullOrWhiteSpace(f.Url))
                    .ToList();

                if (query.Length == 0)
                {
                    return all.OrderBy(f => f.SortOrder)
                              .ThenByDescending(f => f.AddedDate)
                              .ToList();
                }

                string lq = query.ToLowerInvariant();
                return all.Where(f => Matches(f.Title, f.Url, lq))
                          .OrderBy(f => f.SortOrder)
                          .ThenByDescending(f => f.AddedDate)
                          .ToList();
            }
            catch { return new List<FavoriteItem>(); }
        }

        private static List<HistoryItem> GetHistoryMatches(string query)
        {
            try
            {
                var all = HistoryManager.History
                    .Where(h => !string.IsNullOrEmpty(h.Url) && h.Url != "about:blank");

                if (query.Length == 0)
                    return all.Take(30).ToList();

                string lq = query.ToLowerInvariant();
                return all.Where(h => Matches(h.Title, h.Url, lq)).Take(30).ToList();
            }
            catch { return new List<HistoryItem>(); }
        }

        private static bool Matches(string? title, string? url, string lowerQuery)
            => (title ?? "").ToLowerInvariant().Contains(lowerQuery)
            || (url ?? "").ToLowerInvariant().Contains(lowerQuery);

        private static SuggestionItem CreateFavoriteSuggestion(FavoriteItem favorite) => new SuggestionItem
        {
            DisplayTitle = string.IsNullOrWhiteSpace(favorite.Title) ? favorite.Url : favorite.Title,
            Url = favorite.Url,
            IsFavorite = true
        };

        private static string UrlKey(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            string key = url.Trim();
            if (key.EndsWith("/") && !key.EndsWith("//"))
                key = key.Substring(0, key.Length - 1);
            return key;
        }

        private void ShowSuggestions()
        {
            if (!HasAnySuggestions()) return;

            if (!_compactSuggestions)
            {
                SuggestionBorder.Visibility = Visibility.Collapsed;
                WideSuggestionBorder.Visibility = Visibility.Visible;

                // 面板与窗口同宽；用整条地址栏作为锚点，避免相对 URL 框居中导致左右溢出。
                double width = ActualWidth > 0 ? ActualWidth - 2 : 800;
                WideSuggestionBorder.Width = Math.Max(width, 480);

                double height = 340;
                try
                {
                    double windowHeight = Window.Current.Bounds.Height;
                    if (windowHeight > 0)
                        height = Math.Max(220, Math.Min(340, windowHeight - 140));
                }
                catch { }
                WideSuggestionBorder.Height = height;
            }
            else
            {
                WideSuggestionBorder.Visibility = Visibility.Collapsed;
                SuggestionBorder.Visibility = Visibility.Visible;
                SuggestionBorder.Width = Math.Max(UrlBox.ActualWidth, 360);
            }

            if (!SuggestionsFlyout.IsOpen)
                SuggestionsFlyout.ShowAt(_compactSuggestions ? (FrameworkElement)UrlBox : AddressBarRoot);
        }

        private void CloseSuggestions()
        {
            if (SuggestionsFlyout.IsOpen)
                SuggestionsFlyout.Hide();
        }

        private void SuggestionsFlyout_Closed(object sender, object e)
        {
            // 关闭后清掉键盘选中态，下次打开从第一列重新开始。
            ClearWideSelection();
        }

        private sealed class SuggestionItem
        {
            public string DisplayTitle { get; set; } = "";
            public string Url { get; set; } = "";
            public bool IsFavorite { get; set; }
            public bool IsPopular { get; set; }
            public bool IsSearch { get; set; }
            public bool ShowSubtitle { get; set; } = true;
        }
    }
}
