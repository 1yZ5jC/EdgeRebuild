using EdgeRebuild.Services;
using System;
using System.Globalization;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Metro
{
    /// <summary>
    /// 设置面板（IE10/IE11 Modern 设置页形态，从右侧滑入）：
    /// 白色系统面板 / 灰色按钮 / 矩形开关 / 分段缩放条 / 浅色下拉框。
    /// 业务逻辑（清历史、缩放注入、InPrivate）由 MetroPage 处理，本控件只发事件 + 显示状态。
    /// </summary>
    public sealed partial class MetroSettingsPanelControl : UserControl
    {
        public event Action? CloseRequested;
        public event Action<double>? ZoomChanged;
        public event Action? ClearHistoryRequested;
        public event Action<bool>? InPrivateToggled;       // P2-13：Metro 窗口 InPrivate 开关
        public event Action<bool>? AlwaysShowChromeChanged; // IE11：始终显示地址栏和标签
        public event Action<bool>? CircleButtonsChanged;    // 按钮外围是否画圆圈

        /// <summary>供宿主在面板关闭时补一次状态同步。</summary>
        public bool CircleButtonsEnabled => CircleButtonsToggle.IsChecked == true;

        private static readonly double[] ZoomSteps =
            { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0 };

        private static readonly Color ZoomActiveColor = Color.FromArgb(0xFF, 0x00, 0x78, 0xD7);
        private static readonly Color ZoomInactiveColor = Color.FromArgb(0xFF, 0xC8, 0xC8, 0xC8);

        private int _zoomIndex = 3;   // 100%
        private bool _loading = true;
        private bool _zoomDragging;

        public MetroSettingsPanelControl()
        {
            InitializeComponent();
            BuildZoomBar();

            // Button 的类处理会吞掉 PointerPressed，必须用 handledEventsToo 挂接，
            // 否则点击/拖动缩放条不生效（XAML 事件只收未处理事件）。
            ZoomBar.AddHandler(PointerPressedEvent,
                new PointerEventHandler(ZoomBar_PointerPressed), true);
            ZoomBar.AddHandler(PointerMovedEvent,
                new PointerEventHandler(ZoomBar_PointerMoved), true);
            ZoomBar.AddHandler(PointerReleasedEvent,
                new PointerEventHandler(ZoomBar_PointerReleased), true);
            ZoomBar.AddHandler(PointerCanceledEvent,
                new PointerEventHandler(ZoomBar_PointerReleased), true);

            _ = LoadSettingsAsync();
        }

        public void UpdateZoomPercent(int percent)
        {
            ZoomPercentText.Text = percent.ToString(CultureInfo.InvariantCulture) + "%";

            double factor = percent / 100.0;
            int nearest = 0;
            double best = double.MaxValue;
            for (int i = 0; i < ZoomSteps.Length; i++)
            {
                double diff = Math.Abs(ZoomSteps[i] - factor);
                if (diff < best)
                {
                    best = diff;
                    nearest = i;
                }
            }
            SetZoomIndex(nearest, raiseEvent: false);
        }

        private void BuildZoomBar()
        {
            for (int i = 0; i < ZoomSteps.Length; i++)
            {
                ZoomBarSegments.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var segment = new Border
                {
                    Background = new SolidColorBrush(ZoomInactiveColor),
                    Margin = new Thickness(0, 0, i == ZoomSteps.Length - 1 ? 0 : 2, 0)
                };
                Grid.SetColumn(segment, i);
                ZoomBarSegments.Children.Add(segment);
            }
            SetZoomIndex(3, raiseEvent: false);
        }

        private void SetZoomIndex(int index, bool raiseEvent)
        {
            _zoomIndex = Math.Max(0, Math.Min(ZoomSteps.Length - 1, index));
            for (int i = 0; i < ZoomBarSegments.Children.Count; i++)
            {
                if (ZoomBarSegments.Children[i] is Border segment)
                {
                    segment.Background = new SolidColorBrush(
                        i <= _zoomIndex ? ZoomActiveColor : ZoomInactiveColor);
                }
            }
            if (raiseEvent)
                ZoomChanged?.Invoke(ZoomSteps[_zoomIndex]);
        }

        private void ZoomBar_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _zoomDragging = true;
            ZoomBar.CapturePointer(e.Pointer);
            UpdateZoomFromPointer(e);
            e.Handled = true;
        }

        private void ZoomBar_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_zoomDragging) return;
            UpdateZoomFromPointer(e);
            e.Handled = true;
        }

        private void ZoomBar_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_zoomDragging) return;
            _zoomDragging = false;
            ZoomBar.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        private void UpdateZoomFromPointer(PointerRoutedEventArgs e)
        {
            double width = ZoomBarSegments.ActualWidth;
            if (width <= 0) width = ZoomBar.ActualWidth;
            if (width <= 0) return;

            double x = e.GetCurrentPoint(ZoomBarSegments).Position.X;
            int index = (int)Math.Floor(x / width * ZoomSteps.Length);
            SetZoomIndex(index, raiseEvent: true);
        }

        private void ZoomBar_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Left)
            {
                SetZoomIndex(_zoomIndex - 1, raiseEvent: true);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Right)
            {
                SetZoomIndex(_zoomIndex + 1, raiseEvent: true);
                e.Handled = true;
            }
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                string? location = await SettingsManager.GetAsync("MetroLocationPermission");
                LocationToggle.IsChecked = location == "True" || location == "true";
                LocationToggle.Content = LocationToggle.IsChecked == true ? "开" : "关";
            }
            catch { }

            try
            {
                string? always = await SettingsManager.GetAsync("MetroAlwaysShowChrome");
                AlwaysShowChromeToggle.IsChecked = always != "False";
                AlwaysShowChromeToggle.Content = AlwaysShowChromeToggle.IsChecked == true ? "开" : "关";
            }
            catch { }

            try
            {
                string? circles = await SettingsManager.GetAsync("MetroCircleButtons");
                CircleButtonsToggle.IsChecked = circles != "False";
                CircleButtonsToggle.Content = CircleButtonsToggle.IsChecked == true ? "开" : "关";
            }
            catch { }

            _loading = false;
        }

        private async void LocationToggle_Checked(object sender, RoutedEventArgs e)
        {
            LocationToggle.Content = "开";
            if (_loading) return;
            try { await SettingsManager.SetAsync("MetroLocationPermission", "True"); } catch { }
        }

        private async void LocationToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            LocationToggle.Content = "关";
            if (_loading) return;
            try { await SettingsManager.SetAsync("MetroLocationPermission", "False"); } catch { }
        }

        private void InPrivateToggle_Checked(object sender, RoutedEventArgs e)
        {
            InPrivateToggle.Content = "开";
            if (_loading) return;
            InPrivateToggled?.Invoke(true);
        }

        private void InPrivateToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            InPrivateToggle.Content = "关";
            if (_loading) return;
            InPrivateToggled?.Invoke(false);
        }

        private async void AlwaysShowChromeToggle_Checked(object sender, RoutedEventArgs e)
        {
            AlwaysShowChromeToggle.Content = "开";
            if (_loading) return;
            try { await SettingsManager.SetAsync("MetroAlwaysShowChrome", "True"); } catch { }
            AlwaysShowChromeChanged?.Invoke(true);
        }

        private async void AlwaysShowChromeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            AlwaysShowChromeToggle.Content = "关";
            if (_loading) return;
            try { await SettingsManager.SetAsync("MetroAlwaysShowChrome", "False"); } catch { }
            AlwaysShowChromeChanged?.Invoke(false);
        }

        private void CircleButtonsToggle_Checked(object sender, RoutedEventArgs e)
        {
            CircleButtonsToggle.Content = "开";
            if (_loading) return;
            CircleButtonsChanged?.Invoke(true);   // 先改 UI，再落盘
            _ = SaveCircleButtonsAsync(true);
        }

        private void CircleButtonsToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            CircleButtonsToggle.Content = "关";
            if (_loading) return;
            CircleButtonsChanged?.Invoke(false);
            _ = SaveCircleButtonsAsync(false);
        }

        private static async Task SaveCircleButtonsAsync(bool on)
        {
            try { await SettingsManager.SetAsync("MetroCircleButtons", on ? "True" : "False"); }
            catch { }
        }

        private void AutoEncodingToggle_Checked(object sender, RoutedEventArgs e)
        {
            AutoEncodingToggle.Content = "开";
            if (EncodingBox != null) EncodingBox.IsEnabled = false;
            if (TextDirectionBox != null) TextDirectionBox.IsEnabled = false;
        }

        private void AutoEncodingToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            AutoEncodingToggle.Content = "关";
            if (_loading) return;

            // WebView2 / EdgeHTML 均不提供手动编码切换 API：开关弹回“开”，避免假设置。
            AutoEncodingToggle.IsChecked = true;
            AutoEncodingToggle.Content = "开";
            NotificationService.ShowToast("设置", "当前引擎不支持手动切换编码，保持自动检测");
        }

        private void EncodingBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || EncodingBox.SelectedIndex <= 0) return;
            NotificationService.ShowToast("设置", "当前引擎不支持手动切换编码，保持自动检测");
            EncodingBox.SelectedIndex = 0;
        }

        private void TextDirectionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || TextDirectionBox.SelectedIndex <= 0) return;
            NotificationService.ShowToast("设置", "当前引擎不支持切换文字方向");
            TextDirectionBox.SelectedIndex = 0;
        }

        private async void ClearLocationBtn_Click(object sender, RoutedEventArgs e)
        {
            LocationToggle.IsChecked = false;
            LocationToggle.Content = "关";
            try { await SettingsManager.SetAsync("MetroLocationPermission", "False"); } catch { }
            NotificationService.ShowToast("设置", "已清除位置权限");
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
        private void ClearHistoryBtn_Click(object sender, RoutedEventArgs e) => ClearHistoryRequested?.Invoke();
    }
}
