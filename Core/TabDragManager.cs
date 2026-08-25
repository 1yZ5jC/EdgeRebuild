using System;
using System.Linq;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using EdgeRebuild.Services;

namespace EdgeRebuild.Core
{
    public class TabDragManager
    {
        private readonly TabManager _tabManager;
        private Panel _tabBarPanel;
        private readonly Canvas _dragCanvas;
        private readonly LayoutManager _layoutManager;

        private TabViewItem _dragItem;
        private Border _dragGhost;
        private Point _dragOffset;
        private Point _dragStartCanvasPoint;
        private Point _tabBarPanelOffset;
        private bool _isDragging;
        private bool _hasMoved;
        private bool _isDetaching;
        private DateTime _lastReorderTime;
        private DateTime _lastMoveLogTime;
        private const int ReorderThrottleMs = 200;
        private const double DetachThreshold = 20;

        /// <summary>垂直标签模式：Y 轴排序 + X 轴脱离判定</summary>
        public bool IsVertical { get; set; }

        // 事件
        public event Action<int, int> SortRequested;
        public event Action<TabViewItem> DetachRequested;
        public event Action DragStarted;
        public event Action DragCompleted;
        public event Action<Border, TabViewItem> TabRightTapped;

        public TabDragManager(TabManager tabManager, Panel tabBarPanel, Canvas dragCanvas, LayoutManager layoutManager)
        {
            _tabManager = tabManager;
            _tabBarPanel = tabBarPanel;
            _dragCanvas = dragCanvas;
            _layoutManager = layoutManager;
        }

        /// <summary>切换拖拽目标面板（水平↔垂直）</summary>
        public void SetTargetPanel(Panel panel)
        {
            _tabBarPanel = panel;
        }

        public void AttachEvents(TabViewItem viewItem)
        {
            if (viewItem?.Container != null)
            {
                viewItem.Container.PointerPressed += OnPointerPressed;
                viewItem.Container.PointerMoved += OnPointerMoved;
                viewItem.Container.PointerReleased += OnPointerReleased;
                viewItem.Container.PointerCanceled += OnPointerCanceled;
                viewItem.Container.RightTapped += OnRightTapped;
            }
        }

        public void DetachEvents(TabViewItem viewItem)
        {
            if (viewItem?.Container != null)
            {
                viewItem.Container.PointerPressed -= OnPointerPressed;
                viewItem.Container.PointerMoved -= OnPointerMoved;
                viewItem.Container.PointerReleased -= OnPointerReleased;
                viewItem.Container.PointerCanceled -= OnPointerCanceled;
                viewItem.Container.RightTapped -= OnRightTapped;
            }
        }

        private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            var viewItem = _tabManager.Tabs.FirstOrDefault(v => v.Container == border);
            if (viewItem == null) return;

            TabRightTapped?.Invoke(border, viewItem);
            e.Handled = true;
            System.Diagnostics.Debug.WriteLine($"[TabDragManager] 右键点击标签: {viewItem.Tab.Title}");
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var properties = e.GetCurrentPoint((UIElement)sender).Properties;

            if (properties.IsMiddleButtonPressed)
            {
                var border = sender as Border;
                var viewItem = _tabManager.Tabs.FirstOrDefault(v => v.Container == border);
                if (viewItem != null)
                {
                    _ = _tabManager.CloseTabAsync(viewItem);
                    e.Handled = true;
                }
                return;
            }

            if (properties.IsRightButtonPressed) return;

            ResetDragState();
            if (_tabManager.TabCount <= 1) return;

            var borderLeft = sender as Border;
            if (borderLeft == null) return;
            var viewItemDrag = _tabManager.Tabs.FirstOrDefault(v => v.Container == borderLeft);
            if (viewItemDrag == null) return;

            _dragItem = viewItemDrag;
            _dragOffset = e.GetCurrentPoint(viewItemDrag.Container).Position;
            _hasMoved = false;
            _isDragging = false;
            _isDetaching = false;
            _dragStartCanvasPoint = e.GetCurrentPoint(_dragCanvas).Position;

            var colors = ThemeManager.Colors;
            _dragGhost = new Border
            {
                Width = IsVertical ? _tabBarPanel?.ActualWidth ?? _dragItem.Container.ActualWidth : _dragItem.Container.ActualWidth,
                Height = IsVertical ? 36 : 32,
                Background = colors.TabActiveBackground,
                BorderBrush = ThemeManager.GetSeparatorBrush(),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = _dragItem.Tab.Title ?? "新标签页",
                    FontSize = 12,
                    Foreground = colors.ForegroundBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0)
                }
            };

            var initialPos = viewItemDrag.Container.TransformToVisual(_dragCanvas).TransformPoint(new Point(0, 0));
            Canvas.SetLeft(_dragGhost, initialPos.X);
            Canvas.SetTop(_dragGhost, initialPos.Y);
            _dragCanvas.Children.Add(_dragGhost);
            Canvas.SetZIndex(_dragGhost, 999);

            _dragItem.Container.Tag = _dragItem.Container.Child;
            _dragItem.Container.Child = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            _dragItem.Container.Opacity = 1.0;

            var tabBarTransform = _tabBarPanel.TransformToVisual(_dragCanvas);
            _tabBarPanelOffset = tabBarTransform.TransformPoint(new Point(0, 0));
            _lastReorderTime = DateTime.MinValue;

            borderLeft.CapturePointer(e.Pointer);
            e.Handled = true;

            DragStarted?.Invoke();
            Services.Diagnostics.Log($"[拖拽] 拖拽开始 tab=\"{_dragItem.Tab.Title}\" index={_tabManager.Tabs.ToList().IndexOf(_dragItem)} tabCount={_tabManager.TabCount} " +
                $"barOffset=({_tabBarPanelOffset.X:0},{_tabBarPanelOffset.Y:0}) barSize=({_tabBarPanel.ActualWidth:0}x{_tabBarPanel.ActualHeight:0})");
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_dragItem == null || _dragGhost == null) return;

            var posInCanvas = e.GetCurrentPoint(_dragCanvas).Position;

            if (!_isDragging && (Math.Abs(posInCanvas.X - _dragStartCanvasPoint.X) > 3 ||
                                 Math.Abs(posInCanvas.Y - _dragStartCanvasPoint.Y) > 3))
            {
                _hasMoved = true;
                _isDragging = true;
                Services.Diagnostics.Log($"[拖拽] 超过阈值开始拖动 pos=({posInCanvas.X:0},{posInCanvas.Y:0})");
            }

            if (_isDragging)
            {
                // 关键修复：重排时 SyncTabOrder 会 Clear/重建 TabBarPanel，
                // 被捕获的标签容器从视觉树移除会释放指针捕获；
                // 一旦捕获丢失，指针离开标签本身后不再收到 move 事件，拖不出去。
                // CapturePointer 幂等，直接无条件重捕获。
                (sender as UIElement)?.CapturePointer(e.Pointer);

                double newLeft = posInCanvas.X - _dragOffset.X;
                Canvas.SetLeft(_dragGhost, newLeft);
                Canvas.SetTop(_dragGhost, posInCanvas.Y - _dragOffset.Y);

                if (!_isDetaching)
                {
                    UpdateDragSort(posInCanvas);
                }

                var tabBarBounds = new Rect(
                    _tabBarPanelOffset,
                    new Point(_tabBarPanelOffset.X + _tabBarPanel.ActualWidth,
                              _tabBarPanelOffset.Y + _tabBarPanel.ActualHeight)
                );

                bool outside;
                if (IsVertical)
                {
                    // 垂直模式：拖出侧边栏右侧 → 脱离
                    outside = posInCanvas.X < tabBarBounds.Left - DetachThreshold ||
                              posInCanvas.X > tabBarBounds.Right + DetachThreshold;
                }
                else
                {
                    // 水平模式：拖出标签栏上下 → 脱离
                    outside = posInCanvas.Y < tabBarBounds.Top - DetachThreshold ||
                              posInCanvas.Y > tabBarBounds.Bottom + DetachThreshold;
                }

                // 拖拽诊断：节流记录指针位置与脱离判定
                if ((DateTime.Now - _lastMoveLogTime).TotalMilliseconds >= 300)
                {
                    _lastMoveLogTime = DateTime.Now;
                    Services.Diagnostics.Log($"[拖拽] move pos=({posInCanvas.X:0},{posInCanvas.Y:0}) ghost=({newLeft:0},{posInCanvas.Y - _dragOffset.Y:0}) " +
                        $"bar=(T:{tabBarBounds.Top:0},B:{tabBarBounds.Bottom:0}) outside={outside} detaching={_isDetaching}");
                }

                if (!_isDetaching && outside)
                {
                    _isDetaching = true;
                    Services.Diagnostics.Log($"[拖拽] 拖出标签栏区域，准备脱离 tab=\"{_dragItem.Tab.Title}\" pos=({posInCanvas.X:0},{posInCanvas.Y:0}) barBounds=({tabBarBounds.X:0},{tabBarBounds.Y:0},{tabBarBounds.Width:0}x{tabBarBounds.Height:0})");
                    CleanupGhostAndRestoreItem();
                    DetachRequested?.Invoke(_dragItem);
                    _dragItem = null;
                    _isDragging = false;
                    _hasMoved = false;
                    // 通知 UI 拖拽结束，避免 _isDragging 卡死导致标签栏不再重排
                    DragCompleted?.Invoke();
                }
            }
            e.Handled = true;
        }

        private void UpdateDragSort(Point posInCanvas)
        {
            if ((DateTime.Now - _lastReorderTime).TotalMilliseconds < ReorderThrottleMs) return;

            int dragIndex = _tabManager.Tabs.ToList().IndexOf(_dragItem);
            int targetIndex = -1;

            if (IsVertical)
            {
                // 垂直模式：Y 轴排序
                double ghostCenterY = posInCanvas.Y - _dragOffset.Y + _dragGhost.Height / 2;
                double ghostLocalY = ghostCenterY - _tabBarPanelOffset.Y;
                double accumulatedHeight = 0;
                for (int i = 0; i < _tabManager.Tabs.Count; i++)
                {
                    if (i == dragIndex) continue;
                    var item = _tabManager.Tabs[i];
                    double containerCenter = accumulatedHeight + item.Container.ActualHeight / 2;
                    if (ghostLocalY < containerCenter)
                    {
                        targetIndex = i;
                        break;
                    }
                    accumulatedHeight += item.Container.ActualHeight;
                }
                if (targetIndex == -1)
                    targetIndex = _tabManager.Tabs.Count;
            }
            else
            {
                // 水平模式：X 轴排序
                double ghostCenterInCanvas = posInCanvas.X - _dragOffset.X + _dragGhost.Width / 2;
                double ghostLocalX = ghostCenterInCanvas - _tabBarPanelOffset.X;
                double accumulatedWidth = 0;
                for (int i = 0; i < _tabManager.Tabs.Count; i++)
                {
                    if (i == dragIndex) continue;
                    var item = _tabManager.Tabs[i];
                    double containerCenter = accumulatedWidth + item.Container.ActualWidth / 2;
                    if (ghostLocalX < containerCenter)
                    {
                        targetIndex = i;
                        break;
                    }
                    accumulatedWidth += item.Container.ActualWidth;
                }
                if (targetIndex == -1)
                    targetIndex = _tabManager.Tabs.Count;
            }

            if (targetIndex != dragIndex)
            {
                SortRequested?.Invoke(dragIndex, targetIndex);
                _lastReorderTime = DateTime.Now;
                Services.Diagnostics.Log($"[拖拽] 重排 {dragIndex} -> {targetIndex} (vertical={IsVertical})");
            }
        }

        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_dragItem == null) return;
            try
            {
                if (_isDetaching) return;

                if (!_hasMoved)
                {
                    Services.Diagnostics.Log($"[拖拽] 未移动，视为点击切换 tab=\"{_dragItem.Tab.Title}\"");
                    _ = _tabManager.SwitchToTabAsync(_dragItem);
                    return;
                }

                SortRequested?.Invoke(-1, -1);
                Services.Diagnostics.Log($"[拖拽] 释放，完成重排");
            }
            finally
            {
                CleanupDragState();
                e.Handled = true;
            }
        }

        private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            Services.Diagnostics.Log("[拖拽] 指针取消，清理拖拽状态");
            CleanupDragState();
        }

        private void CleanupGhostAndRestoreItem()
        {
            if (_dragGhost != null)
            {
                _dragCanvas.Children.Remove(_dragGhost);
                _dragGhost = null;
            }
            if (_dragItem != null && _dragItem.Container.Tag is UIElement originalChild)
            {
                _dragItem.Container.Child = originalChild;
                _dragItem.Container.Tag = null;
                _dragItem.Container.Opacity = 1.0;
            }
        }

        private void CleanupDragState()
        {
            CleanupGhostAndRestoreItem();
            _dragItem = null;
            _isDragging = false;
            _hasMoved = false;
            _isDetaching = false;
            DragCompleted?.Invoke();
        }

        private void ResetDragState()
        {
            CleanupDragState();
        }

        public void Dispose()
        {
            foreach (var item in _tabManager.Tabs)
            {
                DetachEvents(item);
            }
            ResetDragState();
        }
    }
}