using EdgeRebuild.Services;
using System;
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
        private ToggleSwitch _askToggle, _suspendToggle;

        public SettingsPane()
        {
            this.InitializeComponent();
            BuildContentPanels();
            SettingsNavView.SelectedItem = SettingsNavView.MenuItems[0];
            // 延迟加载设置，确保控件已加入视觉树
            this.Loaded += OnSettingsPaneLoaded;
        }

        private async void OnSettingsPaneLoaded(object sender, RoutedEventArgs e)
        {
            this.Loaded -= OnSettingsPaneLoaded;
            // 确保所有控件已构建（尽管在构造函数中已构建，但 Loaded 更安全）
            if (_spartanRadio != null && _modernIERadio != null)
            {
                await LoadSettingsAsync();
            }
        }

        public void ApplySkinColors(Brush backgroundBrush)
        {
            SettingsNavView.Background = backgroundBrush;
        }

        private void BuildContentPanels()
        {
            _panels = new UIElement[6];

            // 外观 (0)
            var appearancePanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            appearancePanel.Children.Add(new TextBlock { Text = "皮肤", FontWeight = Windows.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            _spartanRadio = new RadioButton { Content = "经典 Edge", GroupName = "Skin" };
            _modernIERadio = new RadioButton { Content = "Modern IE", GroupName = "Skin", Margin = new Thickness(0, 4, 0, 0) };
            appearancePanel.Children.Add(_spartanRadio);
            appearancePanel.Children.Add(_modernIERadio);
            _panels[0] = appearancePanel;

            // 下载 (1)
            var downloadPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            _askToggle = new ToggleSwitch { Header = "下载前询问" };
            downloadPanel.Children.Add(_askToggle);
            _panels[1] = downloadPanel;

            // 引擎 (2)
            var enginePanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            enginePanel.Children.Add(new TextBlock { Text = "默认渲染引擎", FontWeight = Windows.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            _edgeRadio = new RadioButton { Content = "EdgeHTML", GroupName = "DefaultEngine" };
            _webviewRadio = new RadioButton { Content = "WebView2", GroupName = "DefaultEngine", Margin = new Thickness(0, 4, 0, 0) };
            enginePanel.Children.Add(_edgeRadio);
            enginePanel.Children.Add(_webviewRadio);
            _panels[2] = enginePanel;

            // 隐私 (3)
            var privacyPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            var clearBtn = new Button { Content = "清除浏览数据" };
            clearBtn.Click += async (s, e) =>
            {
                HistoryManager.Clear();
                DownloadManager.ClearCompleted();
                await new ContentDialog { Title = "已清除", Content = "浏览数据、下载记录已清除。", CloseButtonText = "确定" }.ShowAsync();
            };
            privacyPanel.Children.Add(clearBtn);
            _panels[3] = privacyPanel;

            // 性能 (4)
            var perfPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            _suspendToggle = new ToggleSwitch { Header = "后台标签自动挂起（节省内存）" };
            _suspendToggle.Toggled += async (s, e) =>
            {
                await SettingsManager.SetAsync("EnableTabSuspend", _suspendToggle.IsOn.ToString());
            };
            perfPanel.Children.Add(_suspendToggle);
            _panels[4] = perfPanel;

            // 关于 (5)
            var aboutPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            aboutPanel.Children.Add(new TextBlock { Text = "Edge Rebuild", FontWeight = Windows.UI.Text.FontWeights.SemiBold, FontSize = 16 });
            aboutPanel.Children.Add(new TextBlock { Text = "版本 0.2 Alpha", Margin = new Thickness(0, 4, 0, 0) });
            aboutPanel.Children.Add(new TextBlock { Text = "基于 UWP 的双内核浏览器外壳。", Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
            _panels[5] = aboutPanel;
        }

        private async System.Threading.Tasks.Task LoadSettingsAsync()
        {
            try
            {
                string skin = await SettingsManager.GetAsync("Skin") ?? "Spartan";
                _spartanRadio.IsChecked = (skin != "ModernIE");
                _modernIERadio.IsChecked = (skin == "ModernIE");

                string ask = await SettingsManager.GetAsync("AskBeforeDownload") ?? "False";
                _askToggle.IsOn = (ask == "True" || ask == "true");

                string engine = await SettingsManager.GetAsync("DefaultEngine") ?? "EdgeHtml";
                _edgeRadio.IsChecked = (engine != "WebView2");
                _webviewRadio.IsChecked = (engine == "WebView2");

                string suspendSetting = await SettingsManager.GetAsync("EnableTabSuspend") ?? "True";
                _suspendToggle.IsOn = (suspendSetting == "True" || suspendSetting == "true");

                _spartanRadio.Checked += (s, e) => SaveSkin("Spartan");
                _modernIERadio.Checked += (s, e) => SaveSkin("ModernIE");
                _edgeRadio.Checked += (s, e) => SaveEngine("EdgeHtml");
                _webviewRadio.Checked += (s, e) => SaveEngine("WebView2");
                _askToggle.Toggled += async (s, e) => await SettingsManager.SetAsync("AskBeforeDownload", _askToggle.IsOn.ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsPane load error: {ex.Message}");
            }
        }

        private async void SaveSkin(string skin) => await SettingsManager.SetAsync("Skin", skin);
        private async void SaveEngine(string engine) => await SettingsManager.SetAsync("DefaultEngine", engine);

        private void SettingsNavView_SelectionChanged(Microsoft.UI.Xaml.Controls.NavigationView sender, Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is Microsoft.UI.Xaml.Controls.NavigationViewItem item && item.Tag is string tag)
            {
                int index = tag switch
                {
                    "Appearance" => 0,
                    "Download" => 1,
                    "Engine" => 2,
                    "Privacy" => 3,
                    "Performance" => 4,
                    "About" => 5,
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