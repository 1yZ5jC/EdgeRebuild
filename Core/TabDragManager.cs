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
        private readonly Panel _tabBarPanel;
        private readonly Canvas _dragCanvas;
        private readonly LayoutManager _layoutManager;

        private TabViewItem _dragItem;
        private Border _dragGhost;
        private Point _dragOffset;
        private Point _dragStartCanvasPoint;
        private Point _dragStartScreenPoint;
        private Point _tabBarPanelOffset;
        private bool _isDragging;
        private bool _hasMoved;
        private bool _isDetaching;
        private DateTime _lastReorderTime;
        private const int ReorderThrottleMs = 200;
        private const double DetachThreshold = 20;

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
            _dragStartScreenPoint = CoreWindow.GetForCurrentThread().PointerPosition;

            var colors = ThemeManager.Colors;
            _dragGhost = new Border
            {
                Width = _dragItem.Container.ActualWidth,
                Height = 32,
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
            }

            if (_isDragging)
            {
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

                bool outside = posInCanvas.Y < tabBarBounds.Top - DetachThreshold ||
                               posInCanvas.Y > tabBarBounds.Bottom + DetachThreshold;

                if (!_isDetaching && outside)
                {
                    _isDetaching = true;
                    CleanupGhostAndRestoreItem();
                    DetachRequested?.Invoke(_dragItem);
                    _dragItem = null;
                    _isDragging = false;
                    _hasMoved = false;
                }
            }
            e.Handled = true;
        }

        private void UpdateDragSort(Point posInCanvas)
        {
            if ((DateTime.Now - _lastReorderTime).TotalMilliseconds < ReorderThrottleMs) return;

            double ghostCenterInCanvas = posInCanvas.X - _dragOffset.X + _dragGhost.Width / 2;
            double ghostLocalX = ghostCenterInCanvas - _tabBarPanelOffset.X;
            int dragIndex = _tabManager.Tabs.ToList().IndexOf(_dragItem);

            double accumulatedWidth = 0;
            int targetIndex = -1;

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

            if (targetIndex != dragIndex)
            {
                SortRequested?.Invoke(dragIndex, targetIndex);
                _lastReorderTime = DateTime.Now;
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
                    _ = _tabManager.SwitchToTabAsync(_dragItem);
                    return;
                }

                SortRequested?.Invoke(-1, -1);
            }
            finally
            {
                CleanupDragState();
                e.Handled = true;
            }
        }

        private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
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