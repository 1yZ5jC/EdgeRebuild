using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EdgeRebuild.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace EdgeRebuild.Metro
{
    /// <summary>
    /// Metro IE 风格底部标签条：横向滚动的一排标签（favicon + 标题），
    /// 当前标签蓝底高亮，最左侧“+”新建标签。关闭标签经缩略图网格完成。
    /// </summary>
    public sealed partial class MetroTabStripControl : UserControl
    {
        public event Action<TabViewItem> TabSelected;
        public event Action<TabViewItem> TabCloseRequested;
        public event Action NewTabRequested;

        public MetroTabStripControl()
        {
            InitializeComponent();
        }

        public void Refresh(IReadOnlyList<TabViewItem> tabs, TabViewItem current)
        {
            Host.Children.Clear();

            var newBtn = new Button
            {
                Style = (Style)Resources["MetroTabButtonStyle"],
                Width = 36,
                Content = new FontIcon
                {
                    Glyph = "\xE710",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White)
                }
            };
            newBtn.Click += (s, e) => NewTabRequested?.Invoke();
            Host.Children.Add(newBtn);

            foreach (var item in tabs)
                Host.Children.Add(BuildTab(item, item == current));

            ScrollCurrentIntoView();
        }

        private Button BuildTab(TabViewItem item, bool isCurrent)
        {
            var btn = new Button
            {
                Style = (Style)Resources["MetroTabButtonStyle"],
                Tag = item,
                MinWidth = 200,
                MaxWidth = 260,
                Background = isCurrent
                    ? new SolidColorBrush(MetroColorPalette.MetroBlue)
                    : new SolidColorBrush(Colors.Transparent)
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
                FontSize = 13,
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
                FontSize = 13,
                Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var closeBtn = new Button
            {
                Content = "\xE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = new SolidColorBrush(MetroColorPalette.MutedColor),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Tag = item,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
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
            var hostWidth = Host.ActualWidth;
            var scrollWidth = StripScroll.ActualWidth;
            if (hostWidth > scrollWidth && scrollWidth > 0)
                StripScroll.ChangeView(hostWidth - scrollWidth, null, null, true);
        }
    }
}
