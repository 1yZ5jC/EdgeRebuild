using EdgeRebuild.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace EdgeRebuild.Metro
{
    /// <summary>
    /// Metro IE 风格底部标签条：横向滚动的一排标签（favicon + 标题），
    /// 当前标签深灰底 + 强调色描边；右端固定圆形“+”/“…”按钮。
    /// 关闭标签经缩略图网格完成。
    /// </summary>
    public sealed partial class MetroTabStripControl : UserControl
    {
        public event Action<TabViewItem>? TabSelected;
        public event Action<TabViewItem>? TabCloseRequested;
        public event Action? NewTabRequested;
        public event Action? MoreRequested;

        private Button? _currentTabButton;

        public MetroTabStripControl()
        {
            InitializeComponent();

            // 圆圈颜色跟随按钮可用状态：禁用时灰圈，启用时白圈。
            foreach (var button in new[] { NewTabBtn, MoreBtn })
                button.IsEnabledChanged += (s, e) => ApplyCircleRing((Button)s);
        }

        /// <summary>设置页开关：关闭时去掉 +/… 按钮的外围圆圈。</summary>
        public void SetCircleButtonsVisible(bool visible)
        {
            _showCircleButtons = visible;
            ApplyCircleRing(NewTabBtn);
            ApplyCircleRing(MoreBtn);
        }

        private bool _showCircleButtons = true;

        private static readonly Color RingEnabledColor = Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF);
        private static readonly Color RingDisabledColor = Color.FromArgb(0xFF, 0x55, 0x55, 0x55);

        private void ApplyCircleRing(Button button)
        {
            var thickness = _showCircleButtons ? new Thickness(3) : new Thickness(0);
            var brush = new SolidColorBrush(!_showCircleButtons
                ? Colors.Transparent
                : (button.IsEnabled ? RingEnabledColor : RingDisabledColor));
            button.BorderThickness = thickness;
            button.BorderBrush = brush;

            // 1809 上 TemplateBinding 对运行时本地值更新不稳定：模板已展开时
            // 直接同步模板内 Border，保证开关立即生效。
            var bg = FindDescendantBorder(button, "Bg");
            if (bg != null)
            {
                bg.BorderThickness = thickness;
                bg.BorderBrush = brush;
            }
        }

        private static Border? FindDescendantBorder(DependencyObject root, string name)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Border border && border.Name == name) return border;
                var found = FindDescendantBorder(child, name);
                if (found != null) return found;
            }
            return null;
        }

        public void Refresh(IReadOnlyList<TabViewItem>? tabs, TabViewItem? current)
        {
            Host.Children.Clear();
            _currentTabButton = null;

            foreach (var item in tabs ?? Array.Empty<TabViewItem>())
            {
                var btn = BuildTab(item, item == current);
                Host.Children.Add(btn);
                if (item == current) _currentTabButton = btn;
            }

            // 先强制布局，才能拿到标签的实际位置做“滚入视野”。
            Host.UpdateLayout();
            ScrollCurrentIntoView();
        }

        private void NewTabBtn_Click(object sender, RoutedEventArgs e) => NewTabRequested?.Invoke();
        private void MoreBtn_Click(object sender, RoutedEventArgs e) => MoreRequested?.Invoke();

        private Button BuildTab(TabViewItem item, bool isCurrent)
        {
            var btn = new Button
            {
                Style = (Style)Resources["MetroTabButtonStyle"],
                Tag = item,
                MinWidth = 160,
                MaxWidth = 240,
                Background = isCurrent
                    ? new SolidColorBrush(MetroColorPalette.TabActiveColor)
                    : new SolidColorBrush(Colors.Transparent),
                BorderBrush = isCurrent
                    ? new SolidColorBrush(MetroColorPalette.TabActiveBorderColor)
                    : new SolidColorBrush(Colors.Transparent),
                BorderThickness = isCurrent ? new Thickness(2) : new Thickness(0)
            };
            ToolTipService.SetToolTip(btn, item.Tab.Title ?? "新标签页");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new FontIcon
            {
                Glyph = "\xE774",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = new SolidColorBrush(MetroColorPalette.MutedColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            var faviconImage = new Image
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            var title = new TextBlock
            {
                Text = item.Tab.Title ?? "新标签页",
                FontSize = 14,
                Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var closeBtn = new Button
            {
                Content = "\xE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = new SolidColorBrush(isCurrent ? Colors.White : MetroColorPalette.MutedColor),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Tag = item,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            closeBtn.Click += (s, e) =>
            {
                if ((s as Button)?.Tag is TabViewItem t)
                    TabCloseRequested?.Invoke(t);
            };

            Grid.SetColumn(icon, 0);
            Grid.SetColumn(faviconImage, 0);
            Grid.SetColumn(title, 1);
            Grid.SetColumn(closeBtn, 2);
            AutomationProperties.SetName(closeBtn, $"关闭标签页：{item.Tab.Title ?? "新标签页"}");
            grid.Children.Add(icon);
            grid.Children.Add(faviconImage);
            grid.Children.Add(title);
            grid.Children.Add(closeBtn);
            btn.Content = grid;

            if (!string.IsNullOrEmpty(item.Tab.FaviconUri))
                _ = LoadFaviconAsync(item, faviconImage, icon);

            btn.Click += (s, e) =>
            {
                // 点关闭按钮不触发切换（Click 会冒泡到标签按钮）
                if (e.OriginalSource is DependencyObject src && !IsWithinCloseButton(src, closeBtn))
                    TabSelected?.Invoke(item);
            };
            AutomationProperties.SetName(btn, $"标签页：{item.Tab.Title ?? "新标签页"}");
            return btn;
        }

        private bool IsWithinCloseButton(DependencyObject src, Button closeBtn)
        {
            DependencyObject cur = src;
            while (cur != null && cur != this)
            {
                if (cur == closeBtn) return true;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return false;
        }

        private static async Task LoadFaviconAsync(TabViewItem item, Image faviconImage, FontIcon icon)
        {
            try
            {
                var bmp = new BitmapImage { UriSource = new Uri(item.Tab.FaviconUri) };
                faviconImage.Source = bmp;
                faviconImage.Visibility = Visibility.Visible;
                icon.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private void ScrollCurrentIntoView()
        {
            if (_currentTabButton == null) return;

            try
            {
                double viewport = StripScroll.ViewportWidth;
                if (viewport <= 0) return;

                var transform = _currentTabButton.TransformToVisual(Host);
                var rect = transform.TransformBounds(
                    new Rect(0, 0, _currentTabButton.ActualWidth, _currentTabButton.ActualHeight));

                double offset = StripScroll.HorizontalOffset;
                if (rect.Left < offset)
                    StripScroll.ChangeView(rect.Left, null, null, true);
                else if (rect.Right > offset + viewport)
                    StripScroll.ChangeView(Math.Max(0, rect.Right - viewport), null, null, true);
            }
            catch { }
        }
    }
}
