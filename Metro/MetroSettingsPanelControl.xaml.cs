using System;
using System.Globalization;
using EdgeRebuild.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Metro
{
    /// <summary>
    /// 设置面板（Windows 8 设置面板形态，从右侧滑入）：清除浏览数据 / 位置权限 / 缩放 / 编码 / 版本。
    /// 业务逻辑（清历史、缩放注入）由 MetroPage 处理，本控件只发事件 + 显示状态。
    /// </summary>
    public sealed partial class MetroSettingsPanelControl : UserControl
    {
        public event Action CloseRequested;
        public event Action ZoomInRequested;
        public event Action ZoomOutRequested;
        public event Action ZoomResetRequested;
        public event Action ClearHistoryRequested;

        public MetroSettingsPanelControl()
        {
            InitializeComponent();
            _ = LoadLocationSettingAsync();
        }

        public void UpdateZoomPercent(int percent) => ZoomPercentText.Text = percent.ToString(CultureInfo.InvariantCulture) + "%";

        private async System.Threading.Tasks.Task LoadLocationSettingAsync()
        {
            try
            {
                var value = await SettingsManager.GetAsync("MetroLocationPermission");
                LocationToggle.IsOn = (value == "True" || value == "true");
            }
            catch { }
        }

        private async void LocationToggle_Toggled(object sender, RoutedEventArgs e)
        {
            try { await SettingsManager.SetAsync("MetroLocationPermission", LocationToggle.IsOn ? "True" : "False"); } catch { }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
        private void ZoomInBtn_Click(object sender, RoutedEventArgs e) => ZoomInRequested?.Invoke();
        private void ZoomOutBtn_Click(object sender, RoutedEventArgs e) => ZoomOutRequested?.Invoke();
        private void ZoomResetBtn_Click(object sender, RoutedEventArgs e) => ZoomResetRequested?.Invoke();
        private void ClearHistoryBtn_Click(object sender, RoutedEventArgs e) => ClearHistoryRequested?.Invoke();

        private void EncodingBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EncodingBox.SelectedIndex > 0)
            {
                // WebView2 / EdgeHTML 均不提供手动编码切换 API，保持自动检测
                NotificationService.ShowToast("设置", "当前引擎不支持手动切换编码，保持自动检测");
                EncodingBox.SelectedIndex = 0;
            }
        }
    }
}
