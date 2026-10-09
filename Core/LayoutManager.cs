using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Core
{
    public class LayoutManager
    {
        // 公开常量，供外部使用
        public const int MinTabWidth = 100;
        public const int MaxTabWidth = 160;
        public const int PinnedTabWidth = 36;
        private const int AdditionalMargin = 30;
        private const int MinDragWidth = 20;
        private const int ButtonBaseOffset = 50;

        // 状态
        private double _rightReserved;
        private bool _needScroll;
        private double _tabWidth;
        private int _tabCount;
        private bool _recalcQueued;

        // UI 引用
        private UIElement _container;
        private ScrollViewer _tabScrollViewer;
        private StackPanel _tabBarPanel;
        private StackPanel _rightSidePanel;
        private Button _scrollLeftBtn;
        private Button _scrollRightBtn;

        // 事件
        public event Action LayoutUpdated;
        public event Action<double> ScrollPositionChanged;

        // 公共属性
        public double TabWidth => _tabWidth;
        public bool NeedScroll => _needScroll;
        public double RightReserved => _rightReserved;
        public int TabCount => _tabCount;

        public LayoutManager(
            UIElement container,
            ScrollViewer tabScrollViewer,
            StackPanel tabBarPanel,
            StackPanel rightSidePanel,
            Button scrollLeftBtn,
            Button scrollRightBtn)
        {
            _container = container;
            _tabScrollViewer = tabScrollViewer;
            _tabBarPanel = tabBarPanel;
            _rightSidePanel = rightSidePanel;
            _scrollLeftBtn = scrollLeftBtn;
            _scrollRightBtn = scrollRightBtn;

            if (_tabScrollViewer != null)
            {
                _tabScrollViewer.ViewChanged += OnScrollViewerChanged;
            }
        }

        public void Recalculate(double containerWidth, int tabCount, int pinnedCount = 0)
        {
            if (containerWidth <= 0 || tabCount == 0) return;

            // 原实现直接同步执行并在 UpdateRightPanelMargin 里强制 UpdateLayout()：
            // 当 Recalculate 来自 XamlRoot.Changed 等布局敏感回调时,会在布局过程中
            // 触发重入布局 → 嵌套 MeasureOverride → COMException 0x8000FFFF(E_UNEXPECTED),
            // 表现为调试器中断/整窗冻结。改为排到调度器低压队列,保证绝不在布局栈内执行,
            // 并用合并标记吸收高频连续调用。
            if (_recalcQueued) return;
            var dispatcher = Windows.UI.Core.CoreWindow.GetForCurrentThread()?.Dispatcher;
            if (dispatcher == null)
            {
                RecalculateCore(containerWidth, tabCount, pinnedCount);
                return;
            }
            _recalcQueued = true;
            _ = dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                _recalcQueued = false;
                RecalculateCore(containerWidth, tabCount, pinnedCount);
            });
        }

        private void RecalculateCore(double containerWidth, int tabCount, int pinnedCount = 0)
        {

            _tabCount = tabCount;

            UpdateSafeZonePadding(containerWidth);
            UpdateRightPanelMargin(containerWidth);

            double leftFixed = _scrollLeftBtn.Visibility == Visibility.Visible
                ? _scrollLeftBtn.ActualWidth + _scrollLeftBtn.Margin.Left + _scrollLeftBtn.Margin.Right
                : 0;

            double rightFixed = _rightSidePanel.ActualWidth
                + _rightSidePanel.Margin.Left
                + _rightSidePanel.Margin.Right;

            double contentWidth = containerWidth - _rightReserved;
            double availableWidth = Math.Max(0, contentWidth - leftFixed - rightFixed - MinDragWidth);

            // 固定标签占用固定宽度
            double pinnedTotalWidth = pinnedCount * LayoutManager.PinnedTabWidth;
            int normalTabCount = tabCount - pinnedCount;
            double availableForNormal = availableWidth - pinnedTotalWidth;

            _needScroll = false;

            if (normalTabCount > 0)
            {
                double idealTotal = normalTabCount * MaxTabWidth;
                double minTotal = normalTabCount * MinTabWidth;

                if (idealTotal <= availableForNormal)
                {
                    _tabWidth = MaxTabWidth;
                }
                else if (minTotal <= availableForNormal)
                {
                    _tabWidth = availableForNormal / normalTabCount;
                }
                else
                {
                    _tabWidth = MinTabWidth;
                    _needScroll = true;
                }
            }
            else
            {
                _tabWidth = MinTabWidth;
            }

            if (_tabScrollViewer != null)
            {
                _tabScrollViewer.MaxWidth = availableWidth;
                _tabScrollViewer.HorizontalScrollMode = _needScroll
                    ? ScrollMode.Enabled
                    : ScrollMode.Disabled;
            }

            _scrollLeftBtn.Visibility = _needScroll ? Visibility.Visible : Visibility.Collapsed;
            _scrollRightBtn.Visibility = _needScroll ? Visibility.Visible : Visibility.Collapsed;

            LayoutUpdated?.Invoke();
        }

        private void UpdateSafeZonePadding(double containerWidth)
        {
            double systemOverlay = 100;
            try
            {
                var xamlRoot = _container.XamlRoot;
                if (xamlRoot != null)
                {
                    // AppWindow 多窗口共享主 UI 线程：Window.Current / GetForCurrentView
                    // 只反映主窗口状态，改用 XamlRoot 获取本窗口尺寸
                    systemOverlay = xamlRoot.Size.Width
                        - Windows.UI.ViewManagement.ApplicationView.GetForCurrentView().VisibleBounds.Width;
                }
                else
                {
                    var bounds = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView().VisibleBounds;
                    var windowBounds = Windows.UI.Xaml.Window.Current.Bounds;
                    systemOverlay = windowBounds.Width - bounds.Width;
                }
            }
            catch { }

            if (systemOverlay <= 0) systemOverlay = 100;
            _rightReserved = systemOverlay + AdditionalMargin;

            if (_container is Border containerBorder)
            {
                containerBorder.Padding = new Thickness(0, 0, _rightReserved, 0);
            }
        }

        private void UpdateRightPanelMargin(double containerWidth)
        {
            if (_rightSidePanel == null) return;

            // 注意:不要在这里调 _rightSidePanel.UpdateLayout() 强制布局——
            // 主窗口树中存在某些原生元素(WebView2 等)在强制布局时测量必抛
            // COMException 0x8000FFFF(E_UNEXPECTED,三层嵌套 MeasureOverride)。
            // 打开副窗口/改尺寸 → XamlRoot.Changed → 重算 → 强制布局 = 必炸。
            // ActualWidth 在系统下一次正常布局后即为最新值,延迟一帧无碍。
            double rightFixed = _rightSidePanel.ActualWidth
                + _rightSidePanel.Margin.Left
                + _rightSidePanel.Margin.Right;

            double leftFixed = _scrollLeftBtn.Visibility == Visibility.Visible
                ? _scrollLeftBtn.ActualWidth + _scrollLeftBtn.Margin.Left + _scrollLeftBtn.Margin.Right
                : 0;

            double contentWidth = containerWidth - _rightReserved;
            double requiredForDrag = rightFixed + MinDragWidth;
            double extraOffset = Math.Max(0, requiredForDrag - (contentWidth - leftFixed));

            _rightSidePanel.Margin = new Thickness(0, 0, ButtonBaseOffset + extraOffset, 0);
        }

        private void OnScrollViewerChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            ScrollPositionChanged?.Invoke(_tabScrollViewer.HorizontalOffset);
        }

        public void ScrollToLeft()
        {
            if (_tabScrollViewer != null)
            {
                _tabScrollViewer.ChangeView(
                    _tabScrollViewer.HorizontalOffset - 100,
                    null,
                    null,
                    true
                );
            }
        }

        public void ScrollToRight()
        {
            if (_tabScrollViewer != null)
            {
                _tabScrollViewer.ChangeView(
                    _tabScrollViewer.HorizontalOffset + 100,
                    null,
                    null,
                    true
                );
            }
        }

        public void Dispose()
        {
            if (_tabScrollViewer != null)
            {
                _tabScrollViewer.ViewChanged -= OnScrollViewerChanged;
            }
        }
    }
}