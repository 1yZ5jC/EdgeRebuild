using EdgeRebuild.Services;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Controls
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
            LoadCurrentSettings();
        }

        private async void LoadCurrentSettings()
        {
            // 皮肤
            string skin = await SettingsManager.GetAsync("Skin");
            SpartanRadio.IsChecked = (skin != "ModernIE");
            ModernIERadio.IsChecked = (skin == "ModernIE");

            // 下载前询问
            string ask = await SettingsManager.GetAsync("AskBeforeDownload");
            AskBeforeDownloadToggle.IsOn = (ask == "True" || ask == "true");

            // 默认引擎
            string engine = await SettingsManager.GetAsync("DefaultEngine") ?? "EdgeHtml";
            EdgeHtmlDefaultRadio.IsChecked = (engine != "WebView2");
            WebView2DefaultRadio.IsChecked = (engine == "WebView2");
        }

        private async void SkinRadio_Checked(object sender, RoutedEventArgs e)
        {
            string skin = ModernIERadio.IsChecked == true ? "ModernIE" : "Spartan";
            await SettingsManager.SetAsync("Skin", skin);
        }

        private async void AskBeforeDownloadToggle_Toggled(object sender, RoutedEventArgs e)
        {
            await SettingsManager.SetAsync("AskBeforeDownload", AskBeforeDownloadToggle.IsOn.ToString());
        }

        private async void EngineRadio_Checked(object sender, RoutedEventArgs e)
        {
            string engine = WebView2DefaultRadio.IsChecked == true ? "WebView2" : "EdgeHtml";
            await SettingsManager.SetAsync("DefaultEngine", engine);
        }

        private async void ClearDataBtn_Click(object sender, RoutedEventArgs e)
        {
            HistoryManager.Clear();
            DownloadManager.ClearCompleted();
            await new ContentDialog
            {
                Title = "已清除",
                Content = "浏览数据、下载记录已清除。",
                CloseButtonText = "确定"
            }.ShowAsync();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }
    }
}