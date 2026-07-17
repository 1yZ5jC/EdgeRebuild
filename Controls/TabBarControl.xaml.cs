using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using EdgeRebuild.Core;

namespace EdgeRebuild.Controls
{
    public sealed partial class TabBarControl : UserControl
    {
        // 事件
        public event Action NewTabRequested;
        public event Action<TabViewItem> TabClosed;
        public event Action<TabViewItem> TabSelected;

        // 布局参数
        private const int MinTabWidth = 100;
        private const int MaxTabWidth = 160;
        private const int MinDragWidth = 20;

        private double _rightReserved;

        // 公开标签边框列表（用于拖拽等）
        public List<Border> TabBorders { get; } = new List<Border>();

        public TabBarControl()
        {
            this.InitializeComponent();
        }

        // 应用皮肤
        public void ApplySkin(Brush toolbarBackground)
        {
            RootBorder.Background = toolbarBackground;
        }

        // 设置安全区偏移
        public void SetRightReserved(double rightReserved)
        {
            _rightReserved = rightReserved;
            RootBorder.Padding = new Thickness(0, 0, _rightReserved, 0);
        }

        // 更新布局
        public void UpdateLayout(int tabCount)
        {
            if (tabCount == 0) return;

            RootBorder.UpdateLayout();
            RightSidePanel.UpdateLayout();

            double rightFixed = RightSidePanel.ActualWidth + RightSidePanel.Margin.Left + RightSidePanel.Margin.Right;
            double leftFixed = ScrollLeftBtn.Visibility == Visibility.Visible
                ? ScrollLeftBtn.ActualWidth + ScrollLeftBtn.Margin.Left + ScrollLeftBtn.Margin.Right : 0;
            double contentWidth = RootBorder.ActualWidth - _rightReserved;
            double availableWidth = Math.Max(0, contentWidth - leftFixed - rightFixed - MinDragWidth);

            double idealTotal = tabCount * MaxTabWidth;
            double minTotal = tabCount * MinTabWidth;
            bool needScroll = false;
            double targetTabWidth = MaxTabWidth;

            if (idealTotal <= availableWidth) targetTabWidth = MaxTabWidth;
            else if (minTotal <= availableWidth) targetTabWidth = availableWidth / tabCount;
            else { targetTabWidth = MinTabWidth; needScroll = true; }

            foreach (var border in TabBorders)
            {
                border.Width = targetTabWidth;
            }

            TabScrollViewer.MaxWidth = availableWidth;
            if (!needScroll)
            {
                TabScrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
                ScrollLeftBtn.Visibility = Visibility.Collapsed;
                ScrollRightBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                TabScrollViewer.HorizontalScrollMode = ScrollMode.Enabled;
                ScrollLeftBtn.Visibility = Visibility.Visible;
                ScrollRightBtn.Visibility = Visibility.Visible;
            }
        }

        // 添加标签 UI，返回 Border 引用
        public Border AddTabUI(TabViewItem viewItem, Brush background, Brush foreground, Brush mutedForeground)
        {
            var tabBorder = new Border
            {
                Height = 32,
                Background = background,
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                Width = MaxTabWidth
            };

            var tabPanel = new Grid { VerticalAlignment = VerticalAlignment.Center };
            tabPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tabPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var engineMark = new TextBlock
            {
                Text = viewItem.Tab.Engine == EngineType.EdgeHtml ? "E" : "W",
                Foreground = viewItem.Tab.Engine == EngineType.EdgeHtml ? new SolidColorBrush(Colors.DodgerBlue) : new SolidColorBrush(Colors.MediumSeaGreen),
                FontSize = 11,
                FontWeight = Windows.UI.Text.FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };

            var faviconPlaceholder = new FontIcon
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Glyph = "\xE774",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var faviconImage = new Image
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            var titleText = new TextBlock
            {
                Text = "新标签页",
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 10,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center
            };

            var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            infoPanel.Children.Add(engineMark); infoPanel.Children.Add(faviconPlaceholder);
            infoPanel.Children.Add(faviconImage); infoPanel.Children.Add(titleText);

            Grid.SetColumn(infoPanel, 0);
            tabPanel.Children.Add(infoPanel);

            var closeBtn = new Button
            {
                Content = "\xE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = mutedForeground,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Width = 20,
                Height = 20,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 4, 0)
            };
            Grid.SetColumn(closeBtn, 1);
            tabPanel.Children.Add(closeBtn);

            tabBorder.Child = tabPanel;
            TabBarPanel.Children.Add(tabBorder);
            TabBorders.Add(tabBorder);

            // 保存 UI 引用
            viewItem.Container = tabBorder;
            viewItem.TitleText = titleText;
            viewItem.CloseButton = closeBtn;
            viewItem.FaviconImage = faviconImage;
            viewItem.FaviconPlaceholder = faviconPlaceholder;
            viewItem.EngineMark = engineMark;

            // 事件转发
            closeBtn.Click += (s, e) => TabClosed?.Invoke(viewItem);
            tabBorder.Tapped += (s, e) => TabSelected?.Invoke(viewItem);

            return tabBorder;
        }

        // 移除标签 UI
        public void RemoveTabUI(TabViewItem viewItem)
        {
            if (viewItem.Container != null && TabBarPanel.Children.Contains(viewItem.Container))
            {
                TabBarPanel.Children.Remove(viewItem.Container);
                TabBorders.Remove(viewItem.Container);
            }
        }

        // 拖拽占位符操作
        public void InsertPlaceholder(int index, Border placeholder)
        {
            TabBarPanel.Children.Insert(index, placeholder);
        }

        public void RemovePlaceholder(Border placeholder)
        {
            TabBarPanel.Children.Remove(placeholder);
        }

        // 重新排列标签（拖拽排序后调用）
        public void ReorderTabs(List<Border> newOrder)
        {
            TabBarPanel.Children.Clear();
            foreach (var border in newOrder)
            {
                TabBarPanel.Children.Add(border);
            }
        }

        public void ClearTabPanel()
        {
            TabBarPanel.Children.Clear();
            TabBorders.Clear();
        }

        // 更新标签标题
        public void UpdateTabTitle(TabViewItem viewItem, string title)
        {
            if (viewItem.TitleText != null)
                viewItem.TitleText.Text = string.IsNullOrEmpty(title) ? "新标签页" : title;
        }

        // 滚动
        private void ScrollLeftBtn_Click(object sender, RoutedEventArgs e) =>
            TabScrollViewer.ChangeView(TabScrollViewer.HorizontalOffset - 100, null, null);
        private void ScrollRightBtn_Click(object sender, RoutedEventArgs e) =>
            TabScrollViewer.ChangeView(TabScrollViewer.HorizontalOffset + 100, null, null);
        private void TabScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) { }
        private void NewTabButton_Click(object sender, RoutedEventArgs e) => NewTabRequested?.Invoke();
        private void RootBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 可通知 MainPage 更新安全区，这里由 MainPage 在窗口大小变化时调用 SetRightReserved 并更新布局
        }
    }
}