using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using EdgeRebuild.Core;
using EdgeRebuild.Services;

namespace EdgeRebuild
{
    /// <summary>
    /// 多窗口 Demo 页：极简，不写历史、不写会话。
    /// 主窗口 slot=0（App 直接导航），副窗口 slot=1..n（WindowManager 分配）。
    /// </summary>
    public sealed partial class DemoPage : Page
    {
        private int _slot;
        private bool _bound;

        public DemoPage()
        {
            InitializeComponent();
            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _slot = (e.Parameter is NewWindowArgs args) ? args.SessionSlot : 0;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            TitleText.Text = _slot <= 0 ? "多窗口 Demo — 主窗口" : $"多窗口 Demo — 窗口 {_slot}";
            InfoText.Text = $"slot={_slot}    线程={Environment.CurrentManagedThreadId}";

            if (!_bound)
            {
                _bound = true;
                GotFocus += (s, e2) => WindowManager.ReportSlotFocus(_slot);
                WindowManager.SlotActivated += OnSlotActivated;
            }
            OnSlotActivated(WindowManager.ActiveSlot);

            Diagnostics.Log($"[Demo] 页面加载 slot={_slot} thread={Environment.CurrentManagedThreadId} " +
                            $"已注册副窗口={WindowManager.WindowCount}");
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            if (_bound)
            {
                _bound = false;
                WindowManager.SlotActivated -= OnSlotActivated;
            }
        }

        private void OnSlotActivated(int slot)
        {
            ActiveText.Text = $"当前激活槽位：{slot}" + (slot == _slot ? "（本窗口）" : "");
        }

        private async void OnNewWindow(object sender, RoutedEventArgs e)
        {
            NewWindowBtn.IsEnabled = false;
            try
            {
                bool ok = await WindowManager.OpenDemoWindowAsync();
                Diagnostics.Log($"[Demo] 开新窗口结果={ok} 已注册副窗口={WindowManager.WindowCount}");
                if (!ok) InfoText.Text = "开窗失败，详见 LocalState\\crash.log";
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[Demo] 开新窗口异常: {ex}");
                InfoText.Text = $"异常: {ex.Message}";
            }
            finally
            {
                NewWindowBtn.IsEnabled = true;
            }
        }
    }
}
