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

            // 订阅地址栏右键菜单事件
            UrlBox.ContextRequested += UrlBox_ContextRequested;
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

        // ---- 地址栏右键菜单 ----
        private void UrlBox_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            args.Handled = true; // 阻止系统默认菜单

            var flyout = new MenuFlyout();

            // 复制
            var copyItem = new MenuFlyoutItem { Text = "复制" };
            copyItem.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(UrlBox.SelectedText))
                {
                    var dp = new DataPackage();
                    dp.SetText(UrlBox.SelectedText);
                    Clipboard.SetContent(dp);
                }
                else
                {
                    var dp = new DataPackage();
                    dp.SetText(UrlBox.Text);
                    Clipboard.SetContent(dp);
                }
            };
            flyout.Items.Add(copyItem);

            // 粘贴
            var pasteItem = new MenuFlyoutItem { Text = "粘贴" };
            pasteItem.Click += async (s, e) =>
            {
                var content = Clipboard.GetContent();
                if (content.Contains(StandardDataFormats.Text))
                {
                    string text = await content.GetTextAsync();
                    // 将粘贴内容插入到光标位置或替换选中文本
                    int selStart = UrlBox.SelectionStart;
                    int selLength = UrlBox.SelectionLength;
                    string currentText = UrlBox.Text;
                    string newText = currentText.Substring(0, selStart) + text + currentText.Substring(selStart + selLength);
                    UrlBox.Text = newText;
                    UrlBox.SelectionStart = selStart + text.Length;
                }
            };
            flyout.Items.Add(pasteItem);

            // 粘贴并转到
            var pasteAndGoItem = new MenuFlyoutItem { Text = "粘贴并转到" };
            pasteAndGoItem.Click += async (s, e) =>
            {
                var content = Clipboard.GetContent();
                if (content.Contains(StandardDataFormats.Text))
                {
                    string text = await content.GetTextAsync();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        string url = ResolveInputToUrl(text.Trim());
                        UrlSubmitted?.Invoke(url);
                    }
                }
            };
            flyout.Items.Add(pasteAndGoItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // 全选
            var selectAllItem = new MenuFlyoutItem { Text = "全选" };
            selectAllItem.Click += (s, e) => UrlBox.SelectAll();
            flyout.Items.Add(selectAllItem);

            // 清除
            var clearItem = new MenuFlyoutItem { Text = "清除" };
            clearItem.Click += (s, e) => UrlBox.Text = "";
            flyout.Items.Add(clearItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // 重做（Ctrl+Y 通常对应重做，这里简单实现为撤销重做，但 TextBox 有内置撤销支持，我们只提供菜单项调用）
            // 由于 TextBox 的 Redo 方法可能不可用，我们省略具体功能，仅示意。
            var redoItem = new MenuFlyoutItem { Text = "重做" };
            redoItem.IsEnabled = false; // 暂不可用，可后续完善
            flyout.Items.Add(redoItem);

            flyout.ShowAt(UrlBox, new Point(0, 0));
        }

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

        // ---- 输入解析与搜索 ----
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

        // ---- 文字变化 ----
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

        // ---- 建议 ----
        private void ShowSuggestionsManually()
        {
            if (string.IsNullOrWhiteSpace(UrlBox.Text)) { CloseSuggestions(); return; }
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
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x66, 0x66, 0x66)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(icon, 0);

            var text = new TextBlock
            {
                Text = $"{item.DisplayTitle}  —  {item.Url}",
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.Black),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = maxWidth
            };
            Grid.SetColumn(text, 1);

            grid.Children.Add(icon);
            grid.Children.Add(text);
            border.Child = grid;

            border.Tapped += (s, e) => { SubmitUrl(item.Url); e.Handled = true; };
            border.PointerEntered += (s, e) => border.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3));
            border.PointerExited += (s, e) => border.Background = new SolidColorBrush(Colors.Transparent);
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