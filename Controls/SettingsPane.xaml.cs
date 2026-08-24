using EdgeRebuild.Services;
using System;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Controls
{
    public sealed partial class SettingsPane : UserControl
    {
        public event EventHandler CloseRequested;
        private UIElement[] _panels;
        private RadioButton _spartanRadio, _modernIERadio, _edgeRadio, _webviewRadio;
        private ToggleSwitch _askToggle, _suspendToggle, _doNotTrackToggle, _autoCleanDownloadToggle, _restoreToggle, _favoritesBarToggle;
        private ComboBox _ntpTypeCombo;
        private ComboBox _readerFontSizeCombo;
        private TextBox _homePageBox;
        private DispatcherTimer _homePageDebounce;
        private ComboBox _searchEngineCombo;
        private TextBlock _downloadPathBlock;
        private string _currentDownloadPath;

        public SettingsPane()
        {
            this.InitializeComponent();
            BuildContentPanels();
            SettingsNavView.SelectedItem = SettingsNavView.MenuItems[0];
            this.Loaded += OnSettingsPaneLoaded;
        }

        private async void OnSettingsPaneLoaded(object sender, RoutedEventArgs e)
        {
            this.Loaded -= OnSettingsPaneLoaded;
            await LoadSettingsAsync();
        }

        public void ApplySkinColors(Brush backgroundBrush)
        {
            SettingsNavView.Background = backgroundBrush;
        }

        private void BuildContentPanels()
        {
            _panels = new UIElement[7];

            // 常规 (0)
            var generalPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            generalPanel.Children.Add(new TextBlock { Text = "主页", FontWeight = Windows.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            _homePageBox = new TextBox { PlaceholderText = "about:blank", Margin = new Thickness(0, 0, 0, 12) };
            generalPanel.Children.Add(_homePageBox);

            _restoreToggle = new ToggleSwitch { Header = "启动时恢复上次会话", Margin = new Thickness(0, 0, 0, 8) };
            generalPanel.Children.Add(_restoreToggle);

            generalPanel.Children.Add(new TextBlock { Text = "新标签页主页类型", FontWeight = Windows.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            _ntpTypeCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
            _ntpTypeCombo.Items.Add("经典主页");
            _ntpTypeCombo.Items.Add("Metro 磁贴主页");
            _ntpTypeCombo.Items.Add("空白页");
            generalPanel.Children.Add(_ntpTypeCombo);

            generalPanel.Children.Add(new TextBlock { Text = "搜索引擎", FontWeight = Windows.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            _searchEngineCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
            _searchEngineCombo.Items.Add("Google");
            _searchEngineCombo.Items.Add("Bing");
            _searchEngineCombo.Items.Add("DuckDuckGo");
            generalPanel.Children.Add(_searchEngineCombo);

            generalPanel.Children.Add(new TextBlock { Text = "默认下载路径", FontWeight = Windows.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            var pathPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _downloadPathBlock = new TextBlock { Text = "(正在获取...)", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 200 };
            var browseBtn = new Button { Content = "浏览..." };
            browseBtn.Click += async (s, e) =>
            {
                var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
                picker.FileTypeFilter.Add("*");
                var folder = await picker.PickSingleFolderAsync();
                if (folder != null)
                {
                    _currentDownloadPath = folder.Path;
                    _downloadPathBlock.Text = folder.Path;
                    await SettingsManager.SetAsync("DownloadFolder", folder.Path);

                    string token = Guid.NewGuid().ToString();
                    Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.AddOrReplace(token, folder);
                    DownloadManager.SetCustomDownloadPath(folder.Path, token);
                }
            };
            pathPanel.Children.Add(_downloadPathBlock);
            pathPanel.Children.Add(browseBtn);
            generalPanel.Children.Add(pathPanel);
            _panels[0] = generalPanel;

            // 外观 (1)
            var appearancePanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            appearancePanel.Children.Add(new TextBlock { Text = "皮肤", FontWeight = Windows.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            _spartanRadio = new RadioButton { Content = "经典 Edge", GroupName = "Skin" };
            _modernIERadio = new RadioButton { Content = "Modern IE", GroupName = "Skin", Margin = new Thickness(0, 4, 0, 0) };
            appearancePanel.Children.Add(_spartanRadio);
            appearancePanel.Children.Add(_modernIERadio);
            _favoritesBarToggle = new ToggleSwitch { Header = "显示收藏夹栏", Margin = new Thickness(0, 10, 0, 0) };
            appearancePanel.Children.Add(_favoritesBarToggle);

            // 阅读视图默认字号（旧版 Edge 阅读排版设置；页内 A-/A+ 即时微调不持久化）
            appearancePanel.Children.Add(new TextBlock { Text = "阅读视图字号", FontWeight = Windows.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 8) });
            _readerFontSizeCombo = new ComboBox();
            _readerFontSizeCombo.Items.Add("小（15px）");
            _readerFontSizeCombo.Items.Add("标准（18px）");
            _readerFontSizeCombo.Items.Add("大（21px）");
            _readerFontSizeCombo.Items.Add("特大（24px）");
            appearancePanel.Children.Add(_readerFontSizeCombo);
            _panels[1] = appearancePanel;

            // 下载 (2)
            var downloadPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            _askToggle = new ToggleSwitch { Header = "下载前询问" };
            downloadPanel.Children.Add(_askToggle);
            _autoCleanDownloadToggle = new ToggleSwitch { Header = "自动清理已完成的下载", Margin = new Thickness(0, 8, 0, 0) };
            downloadPanel.Children.Add(_autoCleanDownloadToggle);
            _panels[2] = downloadPanel;

            // 引擎 (3)
            var enginePanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            enginePanel.Children.Add(new TextBlock { Text = "默认渲染引擎", FontWeight = Windows.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            _edgeRadio = new RadioButton { Content = "EdgeHTML", GroupName = "DefaultEngine" };
            _webviewRadio = new RadioButton { Content = "WebView2", GroupName = "DefaultEngine", Margin = new Thickness(0, 4, 0, 0) };
            enginePanel.Children.Add(_edgeRadio);
            enginePanel.Children.Add(_webviewRadio);
            _panels[3] = enginePanel;

            // 隐私 (4) – 清除 Cookie 按钮真实实现
            var privacyPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            _doNotTrackToggle = new ToggleSwitch { Header = "发送“请勿跟踪”请求" };
            privacyPanel.Children.Add(_doNotTrackToggle);

            var clearCookiesBtn = new Button { Content = "清除所有 Cookie", Margin = new Thickness(0, 12, 0, 0) };
            clearCookiesBtn.Click += async (s, e) =>
            {
                try
                {
                    var mainPage = (this.XamlRoot?.Content as Frame)?.Content as MainPage;
                    if (mainPage != null)
                    {
                        await mainPage.ClearAllCookiesInternalAsync();
                        await new ContentDialog
                        {
                            Title = "已清除",
                            Content = "已清除所有 WebView2 标签页的 Cookie。",
                            CloseButtonText = "确定",
                            XamlRoot = this.XamlRoot
                        }.ShowAsync();
                    }
                }
                catch (Exception ex)
                {
                    await new ContentDialog
                    {
                        Title = "错误",
                        Content = $"清除 Cookie 失败: {ex.Message}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    }.ShowAsync();
                }
            };
            privacyPanel.Children.Add(clearCookiesBtn);
            _panels[4] = privacyPanel;

            // 性能 (5)
            var perfPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            _suspendToggle = new ToggleSwitch { Header = "后台标签自动挂起（节省内存）" };
            _suspendToggle.Toggled += async (s, e) =>
            {
                await SettingsManager.SetAsync("EnableTabSuspend", _suspendToggle.IsOn.ToString());
            };
            perfPanel.Children.Add(_suspendToggle);
            _panels[5] = perfPanel;

            // 关于 (6)
            var aboutPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            aboutPanel.Children.Add(new TextBlock { Text = "Edge Rebuild", FontWeight = Windows.UI.Text.FontWeights.SemiBold, FontSize = 16 });
            aboutPanel.Children.Add(new TextBlock { Text = "版本 0.2 Alpha", Margin = new Thickness(0, 4, 0, 0) });
            aboutPanel.Children.Add(new TextBlock { Text = "基于 UWP 的双内核浏览器外壳。", Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
            _panels[6] = aboutPanel;
        }

        private async System.Threading.Tasks.Task LoadSettingsAsync()
        {
            try
            {
                string skin = await SettingsManager.GetAsync("Skin") ?? "Spartan";
                _spartanRadio.IsChecked = (skin != "ModernIE");
                _modernIERadio.IsChecked = (skin == "ModernIE");

                string favoritesBar = await SettingsManager.GetAsync("ShowFavoritesBar") ?? "False";
                _favoritesBarToggle.IsOn = (favoritesBar == "True" || favoritesBar == "true");

                string readerFontSize = await SettingsManager.GetAsync("ReaderFontSize") ?? "18";
                _readerFontSizeCombo.SelectedIndex = readerFontSize switch
                {
                    "15" => 0,
                    "21" => 2,
                    "24" => 3,
                    _ => 1
                };

                string ask = await SettingsManager.GetAsync("AskBeforeDownload") ?? "False";
                _askToggle.IsOn = (ask == "True" || ask == "true");
                string autoClean = await SettingsManager.GetAsync("AutoCleanDownload") ?? "False";
                _autoCleanDownloadToggle.IsOn = (autoClean == "True" || autoClean == "true");

                string engine = await SettingsManager.GetAsync("DefaultEngine") ?? "EdgeHtml";
                _edgeRadio.IsChecked = (engine != "WebView2");
                _webviewRadio.IsChecked = (engine == "WebView2");

                string suspendSetting = await SettingsManager.GetAsync("EnableTabSuspend") ?? "True";
                _suspendToggle.IsOn = (suspendSetting == "True" || suspendSetting == "true");

                string homeUrl = await SettingsManager.GetAsync("HomePage") ?? "about:blank";
                _homePageBox.Text = homeUrl;

                string restoreSession = await SettingsManager.GetAsync("RestoreSession") ?? "True";
                _restoreToggle.IsOn = (restoreSession == "True" || restoreSession == "true");

                string ntpType = await SettingsManager.GetAsync("NewTabPageType") ?? "Classic";
                string ntpShow = await SettingsManager.GetAsync("NewTabShowNtp") ?? "True";
                _ntpTypeCombo.SelectedIndex = ntpType switch
                {
                    "Metro" => 1,
                    "Blank" => 2,
                    _ => (ntpShow == "False" ? 2 : 0)
                };

                string searchEngine = await SettingsManager.GetAsync("SearchEngine") ?? "Google";
                _searchEngineCombo.SelectedIndex = searchEngine switch { "Bing" => 1, "DuckDuckGo" => 2, _ => 0 };

                string downloadPath = await SettingsManager.GetAsync("DownloadFolder");
                if (!string.IsNullOrEmpty(downloadPath))
                {
                    _currentDownloadPath = downloadPath;
                    _downloadPathBlock.Text = downloadPath;
                }
                else
                {
                    var defaultFolder = await DownloadManager.GetDownloadFolderAsync();
                    _currentDownloadPath = defaultFolder.Path;
                    _downloadPathBlock.Text = _currentDownloadPath;
                }

                string doNotTrack = await SettingsManager.GetAsync("DoNotTrack") ?? "False";
                _doNotTrackToggle.IsOn = (doNotTrack == "True" || doNotTrack == "true");

                _spartanRadio.Checked += async (s, e) => await SettingsManager.SetAsync("Skin", "Spartan");
                _modernIERadio.Checked += async (s, e) => await SettingsManager.SetAsync("Skin", "ModernIE");
                _edgeRadio.Checked += async (s, e) => await SettingsManager.SetAsync("DefaultEngine", "EdgeHtml");
                _webviewRadio.Checked += async (s, e) => await SettingsManager.SetAsync("DefaultEngine", "WebView2");
                _askToggle.Toggled += async (s, e) => await SettingsManager.SetAsync("AskBeforeDownload", _askToggle.IsOn.ToString());
                _autoCleanDownloadToggle.Toggled += async (s, e) => await SettingsManager.SetAsync("AutoCleanDownload", _autoCleanDownloadToggle.IsOn.ToString());
                _doNotTrackToggle.Toggled += async (s, e) => await SettingsManager.SetAsync("DoNotTrack", _doNotTrackToggle.IsOn.ToString());
                _homePageBox.TextChanged += (s, e) =>
                {
                    // 防抖：每键写 SQLite 太频；停输 500ms 后再持久化
                    if (_homePageDebounce == null)
                        _homePageDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    _homePageDebounce.Stop();
                    _homePageDebounce.Tick += async (s2, e2) =>
                    {
                        _homePageDebounce.Stop();
                        await SettingsManager.SetAsync("HomePage", _homePageBox.Text);
                    };
                    _homePageDebounce.Start();
                };
                _restoreToggle.Toggled += async (s, e) => await SettingsManager.SetAsync("RestoreSession", _restoreToggle.IsOn.ToString());
                _favoritesBarToggle.Toggled += async (s, e) => await SettingsManager.SetAsync("ShowFavoritesBar", _favoritesBarToggle.IsOn.ToString());
                _readerFontSizeCombo.SelectionChanged += async (s, e) =>
                {
                    string size = _readerFontSizeCombo.SelectedIndex switch
                    {
                        0 => "15",
                        2 => "21",
                        3 => "24",
                        _ => "18"
                    };
                    await SettingsManager.SetAsync("ReaderFontSize", size);
                };
                _ntpTypeCombo.SelectionChanged += async (s, e) =>
                {
                    string value = _ntpTypeCombo.SelectedIndex switch
                    {
                        1 => "Metro",
                        2 => "Blank",
                        _ => "Classic"
                    };
                    await SettingsManager.SetAsync("NewTabPageType", value);
                    await SettingsManager.SetAsync("NewTabShowNtp", value == "Blank" ? "False" : "True");
                };
                _searchEngineCombo.SelectionChanged += async (s, e) =>
                {
                    string engineName = _searchEngineCombo.SelectedItem?.ToString() ?? "Google";
                    await SettingsManager.SetAsync("SearchEngine", engineName);
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsPane load error: {ex.Message}");
            }
        }

        private void SettingsNavView_SelectionChanged(Microsoft.UI.Xaml.Controls.NavigationView sender, Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is Microsoft.UI.Xaml.Controls.NavigationViewItem item && item.Tag is string tag)
            {
                int index = tag switch
                {
                    "General" => 0,
                    "Appearance" => 1,
                    "Download" => 2,
                    "Engine" => 3,
                    "Privacy" => 4,
                    "Performance" => 5,
                    "About" => 6,
                    _ => 0
                };
                if (index < _panels.Length) ContentArea.Content = _panels[index];
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}