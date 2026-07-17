using EdgeRebuild.Services;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Controls
{
    public sealed partial class SettingsPane : UserControl
    {
        public event EventHandler CloseRequested;

        private UIElement[] _panels;

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
            var spartanRadio = new RadioButton { Content = "经典 Edge", GroupName = "Skin" };
            var modernIERadio = new RadioButton { Content = "Modern IE", GroupName = "Skin", Margin = new Thickness { Top = 4 } };
            LoadSkinSettings(spartanRadio, modernIERadio);
            spartanRadio.Checked += (s, e) => SaveSkin(modernIERadio.IsChecked == true ? "ModernIE" : "Spartan");
            modernIERadio.Checked += (s, e) => SaveSkin(modernIERadio.IsChecked == true ? "ModernIE" : "Spartan");
            appearancePanel.Children.Add(skinLabel);
            appearancePanel.Children.Add(spartanRadio);
            appearancePanel.Children.Add(modernIERadio);
            _panels[0] = appearancePanel;

            // 下载面板
            var downloadPanel = new StackPanel { Margin = new Thickness { Left = 12, Top = 8, Right = 12, Bottom = 8 } };
            var askToggle = new ToggleSwitch { Header = "下载前询问" };
            LoadDownloadSettings(askToggle);
            askToggle.Toggled += async (s, e) => await SettingsManager.SetAsync("AskBeforeDownload", askToggle.IsOn.ToString());
            downloadPanel.Children.Add(askToggle);
            _panels[1] = downloadPanel;

            // 引擎面板
            var enginePanel = new StackPanel { Margin = new Thickness { Left = 12, Top = 8, Right = 12, Bottom = 8 } };
            var engineLabel = new TextBlock { Text = "默认渲染引擎", FontWeight = Windows.UI.Text.FontWeights.SemiBold, Margin = new Thickness { Bottom = 8 } };
            var edgeRadio = new RadioButton { Content = "EdgeHTML", GroupName = "DefaultEngine" };
            var webviewRadio = new RadioButton { Content = "WebView2", GroupName = "DefaultEngine", Margin = new Thickness { Top = 4 } };
            LoadEngineSettings(edgeRadio, webviewRadio);
            edgeRadio.Checked += (s, e) => SaveEngine(webviewRadio.IsChecked == true ? "WebView2" : "EdgeHtml");
            webviewRadio.Checked += (s, e) => SaveEngine(webviewRadio.IsChecked == true ? "WebView2" : "EdgeHtml");
            enginePanel.Children.Add(engineLabel);
            enginePanel.Children.Add(edgeRadio);
            enginePanel.Children.Add(webviewRadio);
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

            ContentArea.Content = _panels[0];
        }

        private async void LoadSkinSettings(RadioButton spartan, RadioButton modernIE)
        {
            string skin = await SettingsManager.GetAsync("Skin");
            spartan.IsChecked = (skin != "ModernIE");
            modernIE.IsChecked = (skin == "ModernIE");
        }

        private async void SaveSkin(string skin) => await SettingsManager.SetAsync("Skin", skin);

        private async void LoadDownloadSettings(ToggleSwitch toggle)
        {
            string ask = await SettingsManager.GetAsync("AskBeforeDownload");
            toggle.IsOn = (ask == "True" || ask == "true");
        }

        private async void LoadEngineSettings(RadioButton edge, RadioButton webview)
        {
            string engine = await SettingsManager.GetAsync("DefaultEngine") ?? "EdgeHtml";
            edge.IsChecked = (engine != "WebView2");
            webview.IsChecked = (engine == "WebView2");
        }

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