using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
        public event Action CaptureClicked;
        public event Action HubClicked;
        public event Action MenuClicked;
        public event Action<EngineType> EngineSwitched;

        private DispatcherTimer _suggestionTimer;
        private bool _isSuggestionsVisible;
        private List<SuggestionItem> _currentSuggestions = new List<SuggestionItem>();

        private Brush _foregroundBrush;
        private Brush _mutedForegroundBrush;
        private bool _isDarkTheme;
        private string _searchEngine = "Google";

        private bool _isSecure = false;
        private bool _isLocalInterface = false;
        private string _currentHost = "";

        // ---- 自动补全相关字段 ----
        private bool _isAutoCompleting = false;
        private string _lastInputText = "";
        private string _currentAutoCompleteMatch = "";
        private string _currentAutoCompleteDisplay = "";
        private bool _enableDebug = false;   // 调试开关

        public ToolbarControl()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;
            this.SizeChanged += OnSizeChanged;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            double w = e.NewSize.Width;
            if (w >= 720)
                VisualStateManager.GoToState(this, "Wide", false);
            else if (w >= 540)
                VisualStateManager.GoToState(this, "Medium", false);
            else
                VisualStateManager.GoToState(this, "Narrow", false);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _suggestionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _suggestionTimer.Tick += OnSuggestionTimerTick;
            SuggestionsFlyout.Closed += (_, _) => _isSuggestionsVisible = false;

            string[] placeholders = new[]
            {
                "输入网址或搜索（按“↓”来获取建议）",
                "输入网址或搜索（@引擎 临时切换）",
                "输入网址或搜索"
            };
            Random rand = new Random();
            UrlBox.PlaceholderText = placeholders[rand.Next(placeholders.Length)];

            var uiThread = Environment.CurrentManagedThreadId;
            var uiDispatcher = Dispatcher;
            _searchEngine = await SettingsManager.GetAsync("SearchEngine") ?? "Google";
            Services.Diagnostics.Log($"[Toolbar] OnLoaded续体 thread={Environment.CurrentManagedThreadId} " +
                $"(await前线程={uiThread}) " +
                $"ctx={(System.Threading.SynchronizationContext.Current?.GetType().Name ?? "NULL")} " +
                $"hasAccess={uiDispatcher.HasThreadAccess}");
            SettingsManager.SettingChanged += OnSettingChanged;
            UpdateSearchEngineIndicator();

            // 加载调试设置
            await LoadDebugSettingsAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            SettingsManager.SettingChanged -= OnSettingChanged;
            _suggestionTimer?.Stop();
        }

        private void OnSettingChanged(string key, string newValue)
        {
            if (key == "SearchEngine")
            {
                _searchEngine = newValue;
                if (string.IsNullOrEmpty(GetTemporaryEngineFromInput()))
                    UpdateSearchEngineIndicator();
            }
        }

        private async Task LoadDebugSettingsAsync()
        {
            var val = await SettingsManager.GetAsync("AutoCompleteDebug");
            _enableDebug = (val == "True" || val == "true");
            if (_enableDebug)
                System.Diagnostics.Debug.WriteLine("[AC] 调试模式已启用");
        }

        public async Task ToggleDebugAsync()
        {
            var current = await SettingsManager.GetAsync("AutoCompleteDebug");
            bool newVal = !(current == "True" || current == "true");
            await SettingsManager.SetAsync("AutoCompleteDebug", newVal ? "True" : "False");
            _enableDebug = newVal;
            System.Diagnostics.Debug.WriteLine($"[AC] 调试模式已{(newVal ? "启用" : "禁用")}");
        }

        public void FocusAddressBarAndClear()
        {
            UrlBox.Text = string.Empty;
            _lastInputText = string.Empty;
            _currentAutoCompleteMatch = string.Empty;
            _currentAutoCompleteDisplay = string.Empty;
            UrlBox.Focus(FocusState.Programmatic);
            UrlBox.SelectionStart = 0;
            UrlBox.SelectionLength = 0;
            UpdateSearchEngineIndicator();
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

            _foregroundBrush = colors.ForegroundBrush;
            _mutedForegroundBrush = colors.MutedForegroundBrush;
            _isDarkTheme = isDark;

            SuggestionsFlyout.FlyoutPresenterStyle = null;

            BackBtn.Foreground = _mutedForegroundBrush;
            ForwardBtn.Foreground = _mutedForegroundBrush;
            RefreshBtn.Foreground = _mutedForegroundBrush;
            AddFavBtn.Foreground = _mutedForegroundBrush;
            HubBtn.Foreground = _mutedForegroundBrush;
            MenuBtn.Foreground = _mutedForegroundBrush;
        }

        public void UpdateNavState(bool canGoBack, bool canGoForward, string currentUrl)
        {
            BackBtn.IsEnabled = canGoBack;
            ForwardBtn.IsEnabled = canGoForward;
            if (currentUrl == "about:blank" || IsNtpUrl(currentUrl))
            {
                // 空白页与新标签页（NTP）：隐藏地址，显示搜索占位提示
                UrlBox.Text = "";
                _lastInputText = "";
                _currentAutoCompleteMatch = "";
                _currentAutoCompleteDisplay = "";
                UrlBox.PlaceholderText = "搜索或输入网址";
            }
            else
            {
                UrlBox.Text = DecodeUrl(GetReaderDisplayUrl(currentUrl));
                UrlBox.PlaceholderText = "搜索或输入网址";
            }
        }

        public static bool IsNtpUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            try
            {
                var u = new Uri(url);
                if (u.Host?.EndsWith("ntp.local", StringComparison.OrdinalIgnoreCase) == true) return true;
                if (u.Host?.EndsWith("contentcache.local", StringComparison.OrdinalIgnoreCase) == true
                    && (url.Contains("/ntp/", StringComparison.OrdinalIgnoreCase)
                        || url.Contains("/ntpmetro/", StringComparison.OrdinalIgnoreCase))) return true;
                if (u.Scheme == "ms-appx-web" && url.Contains("Assets/Ntp", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 本地阅读器地址弱化：地址栏显示 src 参数里的来源网址（PDF/EPUB 原始地址），
        /// 完整阅读器内部地址写入诊断日志 crash.log。
        /// </summary>
        public static string GetReaderDisplayUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url;
            try
            {
                var host = u.Host ?? "";
                bool isReaderUrl = host.EndsWith("pdfviewer.local", StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith("epubreader.local", StringComparison.OrdinalIgnoreCase)
                    || (u.Scheme == "ms-appx-web" && (url.Contains("viewer.html") || url.Contains("reader.html")));
                if (isReaderUrl)
                {
                    Services.Diagnostics.Log($"[阅读器] 内部地址: {url}");
                    string src = ExtractQueryParam(url, "src");
                    if (!string.IsNullOrEmpty(src))
                        return src;
                    return url.Contains("viewer.html") ? "PDF 阅读器" : "EPUB 阅读器";
                }
            }
            catch { }
            return url;
        }

        private static string ExtractQueryParam(string url, string name)
        {
            // TryCreate：空/畸形 URL（新视图导航完成前 CurrentUrl 为空串）不再抛首轮异常
            if (string.IsNullOrEmpty(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
                return null;
            var query = parsed.Query;
            if (string.IsNullOrEmpty(query)) return null;
            foreach (var part in query.TrimStart('?').Split('&'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && kv[0] == name)
                    return Uri.UnescapeDataString(kv[1]);
            }
            return null;
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

        public void UpdateSiteInfo(bool isSecure, string host, bool isLocalInterface = false)
        {
            _isSecure = isSecure;
            _currentHost = host ?? "";
            _isLocalInterface = isLocalInterface;
            if (!string.IsNullOrEmpty(GetTemporaryEngineFromInput()))
                return;

            if (isLocalInterface)
            {
                // 本地界面（NTP 主页等）：不显示连接安全状态
                SiteInfoText.Text = "\uE80F";   // Home
                SiteInfoText.FontFamily = new FontFamily("Segoe MDL2 Assets");
                ToolTipService.SetToolTip(SiteInfoButton, "本地界面");
            }
            else
            {
                SiteInfoText.Text = isSecure ? "🔒" : "🌐";
                ToolTipService.SetToolTip(SiteInfoButton, isSecure ? $"安全连接 - {_currentHost}" : $"普通连接 - {_currentHost}");
            }
        }

        private void SiteInfoButton_Click(object sender, RoutedEventArgs e)
        {
            var flyout = new MenuFlyout();
            var text = _isLocalInterface
                ? "你正在查看的是浏览器提供的本地界面"
                : _isSecure
                    ? $"与 {_currentHost} 的连接是安全的"
                    : $"与 {_currentHost} 的连接未加密";
            flyout.Items.Add(new MenuFlyoutItem { Text = text, IsEnabled = false });
            flyout.ShowAt(SiteInfoButton, new Point(0, SiteInfoButton.ActualHeight));
        }

        private string GetTemporaryEngineFromInput()
        {
            string raw = UrlBox.Text?.Trim();
            if (string.IsNullOrEmpty(raw) || !raw.StartsWith("@")) return null;
            int spaceIndex = raw.IndexOf(' ');
            if (spaceIndex <= 1) return null;
            string engineName = raw.Substring(1, spaceIndex - 1).ToLowerInvariant();
            if (engineName == "g" || engineName == "google" ||
                engineName == "b" || engineName == "bing" ||
                engineName == "ddg" || engineName == "duckduckgo")
                return engineName;
            return null;
        }

        private string GetEngineShortName(string engineName)
        {
            if (string.IsNullOrEmpty(engineName)) return GetDefaultEngineShortName();
            return engineName switch
            {
                "g" or "google" => "G",
                "b" or "bing" => "B",
                "ddg" or "duckduckgo" => "DDG",
                _ => GetDefaultEngineShortName()
            };
        }

        private string GetDefaultEngineShortName()
        {
            return _searchEngine switch
            {
                "Bing" => "B",
                "DuckDuckGo" => "DDG",
                _ => "G"
            };
        }

        private void UpdateSearchEngineIndicator()
        {
            string tempEngine = GetTemporaryEngineFromInput();
            if (tempEngine != null)
            {
                SiteInfoText.Text = GetEngineShortName(tempEngine);
                SiteInfoText.FontSize = 14;
                ToolTipService.SetToolTip(SiteInfoButton, $"临时搜索引擎：{GetEngineFullName(tempEngine)}");
            }
            else
            {
                if (_isLocalInterface)
                {
                    SiteInfoText.Text = "\uE80F";
                    SiteInfoText.FontSize = 14;
                    SiteInfoText.FontFamily = new FontFamily("Segoe MDL2 Assets");
                    ToolTipService.SetToolTip(SiteInfoButton, "本地界面");
                }
                else
                {
                    SiteInfoText.Text = _isSecure ? "🔒" : "🌐";
                    SiteInfoText.FontSize = 14;
                    ToolTipService.SetToolTip(SiteInfoButton, _isSecure ? $"安全连接 - {_currentHost}" : $"普通连接 - {_currentHost}");
                }
            }
        }

        private string GetEngineFullName(string shortName)
        {
            return shortName switch
            {
                "g" or "google" => "Google",
                "b" or "bing" => "Bing",
                "ddg" or "duckduckgo" => "DuckDuckGo",
                _ => "Google"
            };
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();
        private void ForwardBtn_Click(object sender, RoutedEventArgs e) => ForwardRequested?.Invoke();
        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
        private void AddFavBtn_Click(object sender, RoutedEventArgs e) => AddFavoriteClicked?.Invoke();
        private void CaptureBtn_Click(object sender, RoutedEventArgs e) => CaptureClicked?.Invoke();
        private void HubBtn_Click(object sender, RoutedEventArgs e) => HubClicked?.Invoke();
        private void MenuBtn_Click(object sender, RoutedEventArgs e) => MenuClicked?.Invoke();

        // ---- 地址栏键盘事件 ----
        private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                // 如果有建议列表且不为空，选择第一个建议
                if (_isSuggestionsVisible && _currentSuggestions.Count > 0)
                {
                    SubmitUrl(_currentSuggestions[0].Url);
                    e.Handled = true;
                    return;
                }

                // 如果有自动补全，先接受补全再提交
                if (!string.IsNullOrEmpty(_currentAutoCompleteMatch) && UrlBox.SelectionLength > 0)
                {
                    AcceptAutoComplete();
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
            else if (e.Key == Windows.System.VirtualKey.Tab)
            {
                // Tab 键：如果有自动补全，接受补全；否则显示建议列表
                if (!string.IsNullOrEmpty(_currentAutoCompleteMatch) && UrlBox.SelectionLength > 0)
                {
                    AcceptAutoComplete();
                    e.Handled = true;
                }
                else
                {
                    ShowSuggestionsManually();
                    e.Handled = true;
                }
            }
            else if (e.Key == Windows.System.VirtualKey.Down)
            {
                ShowSuggestionsManually();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                // 如果有自动补全，取消补全
                if (!string.IsNullOrEmpty(_currentAutoCompleteMatch) && UrlBox.SelectionLength > 0)
                {
                    CancelAutoComplete();
                    e.Handled = true;
                }
                else
                {
                    CloseSuggestions();
                    e.Handled = true;
                }
            }
        }

        private void EnsureFlyoutPresenterStyle(double width)
        {
            var style = new Style(typeof(FlyoutPresenter));
            style.Setters.Add(new Setter(FlyoutPresenter.MinWidthProperty, width));
            style.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(0)));
            var acrylic = new AcrylicBrush
            {
                BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                TintColor = _isDarkTheme ? Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xFF, 0xE6, 0xF0, 0xFA),
                TintOpacity = 0.85,
                FallbackColor = _isDarkTheme ? Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xFF, 0xE6, 0xF0, 0xFA)
            };
            style.Setters.Add(new Setter(FlyoutPresenter.BackgroundProperty, acrylic));
            SuggestionsFlyout.FlyoutPresenterStyle = style;
        }

        private string ResolveInputToUrl(string raw)
        {
            if (raw.StartsWith("@"))
            {
                int spaceIndex = raw.IndexOf(' ');
                if (spaceIndex > 1)
                {
                    string engineName = raw.Substring(1, spaceIndex - 1).ToLowerInvariant();
                    string query = raw.Substring(spaceIndex + 1).Trim();
                    if (!string.IsNullOrEmpty(query))
                    {
                        string url = engineName switch
                        {
                            "g" or "google" => $"https://www.google.com/search?q={Uri.EscapeDataString(query)}",
                            "b" or "bing" => $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}",
                            "ddg" or "duckduckgo" => $"https://duckduckgo.com/?q={Uri.EscapeDataString(query)}",
                            _ => null
                        };
                        if (url != null) return url;
                    }
                }
            }

            // 智能解析（网页/搜索二合一 + 中文域名 Punycode）见 UrlInputResolver
            return Services.UrlInputResolver.Resolve(raw, _searchEngine);
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
            if (InPrivateMode) UrlBox.Opacity = 1.0;
        }
        private void UrlBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UrlBox.BorderBrush = new SolidColorBrush(Colors.LightGray);
            if (InPrivateMode) UrlBox.Opacity = 0.3;
        }

        /// InPrivate 窗口：地址栏失焦时半透明（提示隐身状态）
        private bool _inPrivateMode;
        public bool InPrivateMode
        {
            get => _inPrivateMode;
            set { _inPrivateMode = value; UrlBox.Opacity = value ? 0.3 : 1.0; }
        }

        // ---- 文本变化（核心：包含自动补全） ----
        private void UrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 如果是自动补全触发的文本变化，跳过
            if (_isAutoCompleting) return;

            UpdateSearchEngineIndicator();

            string currentText = UrlBox.Text ?? "";
            int cursorPosition = UrlBox.SelectionStart;

            // 检测是否是删除操作（文本变短）
            bool isDeleting = currentText.Length < _lastInputText.Length;

            // 检测光标是否在末尾
            bool cursorAtEnd = (cursorPosition == currentText.Length);

            // 调试日志
            if (_enableDebug)
            {
                System.Diagnostics.Debug.WriteLine($"[AC] TextChanged: '{currentText}', SelStart={cursorPosition}, Len={currentText.Length}");
            }

            // 只在非删除、光标在末尾、且文本不为空时触发补全
            if (!isDeleting && cursorAtEnd && !string.IsNullOrEmpty(currentText))
            {
                // 检查是否应该触发补全
                if (ShouldTriggerAutoComplete(currentText))
                {
                    // 查找最佳匹配
                    var match = FindBestMatch(currentText);
                    if (match.matchUrl != null)
                    {
                        ApplyAutoComplete(currentText, match.matchUrl, match.matchTitle);
                    }
                    else
                    {
                        // 没有匹配，清除补全状态
                        ClearAutoCompleteState();
                    }
                }
                else
                {
                    ClearAutoCompleteState();
                }
            }
            else
            {
                // 删除或光标不在末尾，清除补全状态
                ClearAutoCompleteState();
            }

            _lastInputText = currentText;

            // 触发建议列表更新
            if (_isSuggestionsVisible)
            {
                _suggestionTimer.Stop();
                _suggestionTimer.Start();
            }
        }

        // ---- 自动补全：判断是否应该触发 ----
        private bool ShouldTriggerAutoComplete(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            if (input.Length < 2) return false;
            if (input.Contains(" ")) return false;
            if (input.Contains("/") && input.Contains(".")) return false; // 可能是完整URL

            // 如果输入已经是完整URL格式，不触发补全
            if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // 但如果输入的是 "https://google"，可以补全为 "https://google.com"
                // 所以这里不直接阻止，而是进一步判断
                var uri = TryParseUri(input);
                if (uri != null && !string.IsNullOrEmpty(uri.Host) && uri.Host.Contains("."))
                {
                    // 已经是完整域名，不补全
                    return false;
                }
            }

            return true;
        }

        private Uri TryParseUri(string input)
        {
            try
            {
                if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
                    return uri;
                if (Uri.TryCreate("https://" + input, UriKind.Absolute, out uri))
                    return uri;
                return null;
            }
            catch { return null; }
        }

        // ---- 自动补全：查找最佳匹配 ----
        private (string matchUrl, string matchTitle) FindBestMatch(string input)
        {
            if (string.IsNullOrEmpty(input)) return (null, null);

            if (_enableDebug)
                System.Diagnostics.Debug.WriteLine($"[AC] FindBestMatch('{input}')");

            string inputLower = input.ToLower();
            var candidates = new List<(string title, string url, bool isFavorite, int score)>();

            // 从收藏夹获取
            foreach (var fav in FavoritesManager.Instance.Favorites)
            {
                string title = fav.Title ?? "";
                string url = fav.Url ?? "";
                int score = CalculateMatchScore(title, url, inputLower);
                if (score > 0)
                {
                    candidates.Add((title, url, true, score));
                }
            }

            // 从历史记录获取
            foreach (var hist in HistoryManager.History.Take(20))
            {
                string title = hist.Title ?? "";
                string url = hist.Url ?? "";
                if (candidates.Any(c => c.url == url)) continue;
                int score = CalculateMatchScore(title, url, inputLower);
                if (score > 0)
                {
                    candidates.Add((title, url, false, score));
                    if (_enableDebug)
                        System.Diagnostics.Debug.WriteLine($"  [历史] 标题='{title}', URL='{url}', 得分={score}");
                }
            }

            if (candidates.Count == 0)
            {
                if (_enableDebug)
                    System.Diagnostics.Debug.WriteLine("[AC] 无匹配候选");
                return (null, null);
            }

            // 按得分排序（收藏夹+10）
            var sorted = candidates
                .OrderByDescending(c => c.score + (c.isFavorite ? 10 : 0))
                .ThenByDescending(c => c.isFavorite)
                .ToList();

            var best = sorted[0];
            if (_enableDebug)
            {
                System.Diagnostics.Debug.WriteLine($"[AC] 最佳匹配: 标题='{best.title}', URL='{best.url}', 得分={best.score + (best.isFavorite ? 10 : 0)}");
                if (sorted.Count > 1)
                    System.Diagnostics.Debug.WriteLine($"[AC] 次佳匹配得分: {sorted[1].score + (sorted[1].isFavorite ? 10 : 0)}");
            }

            // 如果只有一个候选，或明显领先（>2分），直接补全
            if (sorted.Count == 1 || best.score - sorted[1].score > 2)
            {
                if (_enableDebug)
                    System.Diagnostics.Debug.WriteLine("[AC] 唯一匹配或明显领先，自动补全");
                return (best.url, best.title);
            }
            else if (best.score - sorted[1].score <= 2)
            {
                // 得分接近时，使用智能选择
                var second = sorted[1];

                // 1. 优先标题非空
                bool bestHasTitle = !string.IsNullOrEmpty(best.title);
                bool secondHasTitle = !string.IsNullOrEmpty(second.title);
                if (bestHasTitle && !secondHasTitle)
                {
                    if (_enableDebug)
                        System.Diagnostics.Debug.WriteLine("[AC] 智能选择：最佳有标题");
                    return (best.url, best.title);
                }
                if (!bestHasTitle && secondHasTitle)
                {
                    if (_enableDebug)
                        System.Diagnostics.Debug.WriteLine("[AC] 智能选择：次佳有标题");
                    return (second.url, second.title);
                }

                // 2. 优先 URL 更短（更干净的域名）
                if (best.url.Length < second.url.Length)
                {
                    if (_enableDebug)
                        System.Diagnostics.Debug.WriteLine("[AC] 智能选择：最佳URL更短");
                    return (best.url, best.title);
                }
                if (second.url.Length < best.url.Length)
                {
                    if (_enableDebug)
                        System.Diagnostics.Debug.WriteLine("[AC] 智能选择：次佳URL更短");
                    return (second.url, second.title);
                }

                // 3. 都差不多，选第一个
                if (_enableDebug)
                    System.Diagnostics.Debug.WriteLine("[AC] 智能选择：默认选最佳");
                return (best.url, best.title);
            }

            return (null, null);
        }

        private int CalculateMatchScore(string title, string url, string inputLower)
        {
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(url)) return 0;

            string titleLower = title?.ToLower() ?? "";
            string urlLower = url?.ToLower() ?? "";

            // 标题开头匹配 - 最高优先级
            if (titleLower.StartsWith(inputLower))
                return 100 + inputLower.Length;

            // URL开头匹配 - 次高优先级
            if (urlLower.StartsWith(inputLower))
                return 80 + inputLower.Length;

            // 标题包含匹配
            if (titleLower.Contains(inputLower))
                return 40 + inputLower.Length;

            // URL包含匹配
            if (urlLower.Contains(inputLower))
                return 20 + inputLower.Length;

            return 0;
        }

        // ---- 自动补全：应用补全 ----
        private void ApplyAutoComplete(string input, string matchUrl, string matchTitle)
        {
            if (string.IsNullOrEmpty(matchUrl)) return;

            // 如果匹配的URL和输入相同，不补全
            if (matchUrl.Equals(input, StringComparison.OrdinalIgnoreCase)) return;

            // 如果输入是完整URL且匹配相同，不补全
            if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (matchUrl.Equals(input, StringComparison.OrdinalIgnoreCase)) return;
            }

            // 确定补全目标
            string completeText = matchUrl;

            // 如果输入是URL协议开头，保留协议
            if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // 使用匹配的完整URL
            }
            else if (input.Contains(".") && !input.Contains(" "))
            {
                // 输入看起来像域名，补全为完整URL
                if (!matchUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !matchUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    completeText = "https://" + matchUrl;
                }
            }
            else
            {
                // 普通补全，使用匹配的URL
                if (!matchUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !matchUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    completeText = "https://" + matchUrl;
                }
            }

            // 检查补全是否真的比输入长
            if (completeText.Length <= input.Length) return;

            // 检查输入是否真的是completeText的前缀
            if (!completeText.StartsWith(input, StringComparison.OrdinalIgnoreCase)) return;

            // 应用补全
            _isAutoCompleting = true;
            _currentAutoCompleteMatch = completeText;
            _currentAutoCompleteDisplay = matchTitle ?? matchUrl;

            UrlBox.Text = completeText;
            // 选中补全的部分（从原输入长度到末尾）
            UrlBox.SelectionStart = input.Length;
            UrlBox.SelectionLength = completeText.Length - input.Length;

            _isAutoCompleting = false;

            // 不自动弹出建议列表，保持不打断输入

            if (_enableDebug)
                System.Diagnostics.Debug.WriteLine($"[AC] ApplyAutoComplete: 输入='{input}', 匹配URL='{matchUrl}', 匹配标题='{matchTitle}'");
        }

        // ---- 自动补全：接受补全 ----
        private void AcceptAutoComplete()
        {
            if (string.IsNullOrEmpty(_currentAutoCompleteMatch)) return;

            if (_enableDebug)
                System.Diagnostics.Debug.WriteLine($"[AC] AcceptAutoComplete: '{_currentAutoCompleteMatch}'");

            _isAutoCompleting = true;
            UrlBox.Text = _currentAutoCompleteMatch;
            UrlBox.SelectionStart = UrlBox.Text.Length;
            UrlBox.SelectionLength = 0;
            _isAutoCompleting = false;

            _currentAutoCompleteMatch = "";
            _currentAutoCompleteDisplay = "";
        }

        // ---- 自动补全：取消补全 ----
        private void CancelAutoComplete()
        {
            if (string.IsNullOrEmpty(_currentAutoCompleteMatch)) return;

            if (_enableDebug)
                System.Diagnostics.Debug.WriteLine($"[AC] CancelAutoComplete: '{_currentAutoCompleteMatch}'");

            string originalText = _lastInputText;
            _isAutoCompleting = true;
            UrlBox.Text = originalText;
            UrlBox.SelectionStart = originalText.Length;
            UrlBox.SelectionLength = 0;
            _isAutoCompleting = false;

            _currentAutoCompleteMatch = "";
            _currentAutoCompleteDisplay = "";
        }

        // ---- 自动补全：清除状态 ----
        private void ClearAutoCompleteState()
        {
            if (!string.IsNullOrEmpty(_currentAutoCompleteMatch))
            {
                if (_enableDebug)
                    System.Diagnostics.Debug.WriteLine("[AC] ClearAutoCompleteState");
                _currentAutoCompleteMatch = "";
                _currentAutoCompleteDisplay = "";
            }
        }

        // ---- 建议列表相关 ----
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
                Glyph = item.IsPopular ? "\uE721" : item.IsFavorite ? "\uE734" : "\uE81C",
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
            string q = query.Trim();

            // 空查询 → 搜索提供程序主页(参考规范 §7.1:空查询显示「热门站点」)
            if (string.IsNullOrWhiteSpace(q))
            {
                var homes = new (string name, string url)[]
                {
                    ("Google", "https://www.google.com"),
                    ("Bing", "https://www.bing.com"),
                    ("DuckDuckGo", "https://duckduckgo.com")
                };
                int currentIndex = Array.FindIndex(homes, h => h.name.Equals(_searchEngine, StringComparison.OrdinalIgnoreCase));
                if (currentIndex > 0)
                {
                    var current = homes[currentIndex];
                    homes[currentIndex] = homes[0];
                    homes[0] = current;
                }
                foreach (var h in homes)
                {
                    results.Add(new SuggestionItem
                    {
                        DisplayTitle = $"{h.name} 主页",
                        Url = h.url,
                        IsPopular = true
                    });
                }
                return results;
            }

            string lq = q.ToLower();

            var favs = FavoritesManager.Instance.Favorites
                .Where(f => f.Title.ToLower().Contains(lq) || f.Url.ToLower().Contains(lq))
                .Take(5)
                .Select(f => new SuggestionItem
                {
                    DisplayTitle = string.IsNullOrEmpty(f.Title) ? f.Url : f.Title,
                    Url = f.Url,
                    IsFavorite = true
                });

            var hists = HistoryManager.History
                .Where(h => h.Title.ToLower().Contains(lq) || h.Url.ToLower().Contains(lq))
                .Take(5)
                .Select(h => new SuggestionItem
                {
                    DisplayTitle = string.IsNullOrEmpty(h.Title) ? h.Url : h.Title,
                    Url = h.Url,
                    IsFavorite = false
                });

            results.AddRange(favs);
            foreach (var h in hists)
                if (!results.Any(r => r.Url == h.Url)) results.Add(h);

            return results.Take(8).ToList();
        }

        private void CloseSuggestions() { SuggestionsFlyout.Hide(); _isSuggestionsVisible = false; }

        private void SubmitUrl(string url)
        {
            CloseSuggestions();
            ClearAutoCompleteState();
            UrlBox.Text = DecodeUrl(url);
            UrlBox.SelectionStart = UrlBox.Text.Length;
            UrlBox.SelectionLength = 0;
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
            public bool IsPopular { get; set; }
        }
    }
}