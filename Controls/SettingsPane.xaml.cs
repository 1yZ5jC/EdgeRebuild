using EdgeRebuild.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Controls
{
    public sealed partial class SettingsPane : UserControl
    {
        public event EventHandler CloseRequested;

        private UIElement[] _panels;
        private RadioButton _spartanRadio, _modernIERadio;
        private RadioButton _edgeRadio, _webviewRadio;
        private ToggleSwitch _askToggle;

        public SettingsPane()
        {
            this.InitializeComponent();
            BuildContentPanels();
            SettingsNavView.SelectedItem = SettingsNavView.MenuItems[0];
        }

        private void BuildContentPanels()
        {
            _panels = new UIElement[5];

            // 外观面板
            var appearancePanel = new StackPanel { Margin = new Thickness { Left = 12, Top = 8, Right = 12, Bottom = 8 } };
            var skinLabel = new TextBlock { Text = "皮肤", FontWeight = Windows.UI.Text.FontWeights.SemiBold, Margin = new Thickness { Bottom = 8 } };
            _spartanRadio = new RadioButton { Content = "经典 Edge", GroupName = "Skin" };
            _modernIERadio = new RadioButton { Content = "Modern IE", GroupName = "Skin", Margin = new Thickness { Top = 4 } };
            appearancePanel.Children.Add(skinLabel);
            appearancePanel.Children.Add(_spartanRadio);
            appearancePanel.Children.Add(_modernIERadio);
            _panels[0] = appearancePanel;

            // 下载面板
            var downloadPanel = new StackPanel { Margin = new Thickness { Left = 12, Top = 8, Right = 12, Bottom = 8 } };
            _askToggle = new ToggleSwitch { Header = "下载前询问" };
            downloadPanel.Children.Add(_askToggle);
            _panels[1] = downloadPanel;

            // 引擎面板
            var enginePanel = new StackPanel { Margin = new Thickness { Left = 12, Top = 8, Right = 12, Bottom = 8 } };
            var engineLabel = new TextBlock { Text = "默认渲染引擎", FontWeight = Windows.UI.Text.FontWeights.SemiBold, Margin = new Thickness { Bottom = 8 } };
            _edgeRadio = new RadioButton { Content = "EdgeHTML", GroupName = "DefaultEngine" };
            _webviewRadio = new RadioButton { Content = "WebView2", GroupName = "DefaultEngine", Margin = new Thickness { Top = 4 } };
            enginePanel.Children.Add(engineLabel);
            enginePanel.Children.Add(_edgeRadio);
            enginePanel.Children.Add(_webviewRadio);
            _panels[2] = enginePanel;

            // 隐私面板
            var privacyPanel = new StackPanel { Margin = new Thickness { Left = 12, Top = 8, Right = 12, Bottom = 8 } };
            var clearBtn = new Button { Content = "清除浏览数据" };
            clearBtn.Click += async (s, e) =>
            {
                HistoryManager.Clear();
                DownloadManager.ClearCompleted();
                await new ContentDialog
                {
                    Title = "已清除",
                    Content = "浏览数据、下载记录已清除。",
                    CloseButtonText = "确定"
                }.ShowAsync();
            };
            privacyPanel.Children.Add(clearBtn);
            _panels[3] = privacyPanel;

            // 关于面板
            var aboutPanel = new StackPanel { Margin = new Thickness { Left = 12, Top = 8, Right = 12, Bottom = 8 } };
            aboutPanel.Children.Add(new TextBlock { Text = "Edge Rebuild", FontWeight = Windows.UI.Text.FontWeights.SemiBold, FontSize = 16 });
            aboutPanel.Children.Add(new TextBlock { Text = "版本 0.2 Alpha", Margin = new Thickness { Top = 4 } });
            aboutPanel.Children.Add(new TextBlock { Text = "基于 UWP 的双内核浏览器外壳。", Margin = new Thickness { Top = 8 }, TextWrapping = TextWrapping.Wrap });
            _panels[4] = aboutPanel;

            // 加载初始值（在绑定事件之前）
            LoadSettingsAsync();

            // 绑定事件（加载完成后绑定，避免初始化触发保存）
            _spartanRadio.Checked += (s, e) => SaveSkin("Spartan");
            _modernIERadio.Checked += (s, e) => SaveSkin("ModernIE");
            _edgeRadio.Checked += (s, e) => SaveEngine("EdgeHtml");
            _webviewRadio.Checked += (s, e) => SaveEngine("WebView2");
            _askToggle.Toggled += async (s, e) => await SettingsManager.SetAsync("AskBeforeDownload", _askToggle.IsOn.ToString());

            ContentArea.Content = _panels[0];
        }

        private async void LoadSettingsAsync()
        {
            // 皮肤
            string skin = await SettingsManager.GetAsync("Skin") ?? "Spartan";
            _spartanRadio.IsChecked = (skin != "ModernIE");
            _modernIERadio.IsChecked = (skin == "ModernIE");

            // 下载前询问
            string ask = await SettingsManager.GetAsync("AskBeforeDownload") ?? "False";
            _askToggle.IsOn = (ask == "True" || ask == "true");

            // 默认引擎
            string engine = await SettingsManager.GetAsync("DefaultEngine") ?? "EdgeHtml";
            _edgeRadio.IsChecked = (engine != "WebView2");
            _webviewRadio.IsChecked = (engine == "WebView2");
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
                    "About" => 4,
                    _ => 0
                };
                if (index < _panels.Length)
                    ContentArea.Content = _panels[index];
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}