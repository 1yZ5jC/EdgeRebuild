using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace EdgeRebuild.Core
{
    public class LayoutManager
    {
        // 公开常量，供外部使用
        public const int MinTabWidth = 100;
        public const int MaxTabWidth = 160;
        private const int AdditionalMargin = 30;
        private const int MinDragWidth = 20;
        private const int ButtonBaseOffset = 50;

        // 状态
        private double _rightReserved;
        private bool _needScroll;
        private double _tabWidth;
        private int _tabCount;

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

        public void Recalculate(double containerWidth, int tabCount)
        {
            if (containerWidth <= 0 || tabCount == 0) return;

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

            double idealTotal = tabCount * MaxTabWidth;
            double minTotal = tabCount * MinTabWidth;
            _needScroll = false;
            _tabWidth = MaxTabWidth;

            if (idealTotal <= availableWidth)
            {
                _tabWidth = MaxTabWidth;
            }
            else if (minTotal <= availableWidth)
            {
                _tabWidth = availableWidth / tabCount;
            }
            else
            {
                _tabWidth = MinTabWidth;
                _needScroll = true;
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
                var bounds = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView().VisibleBounds;
                var windowBounds = Windows.UI.Xaml.Window.Current.Bounds;
                systemOverlay = windowBounds.Width - bounds.Width;
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

            _rightSidePanel.UpdateLayout();
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
            _rightSidePanel.UpdateLayout();
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