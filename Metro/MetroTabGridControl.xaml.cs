using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EdgeRebuild.Core;
using EdgeRebuild.Services;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace EdgeRebuild.Metro
{
    /// <summary>
    /// Metro IE 风格的全屏标签缩略图网格：卡片 = 缩略图 + 标题 + favicon + 关闭。
    /// 打开时按标签逐个异步捕获缩略图，失败降级为 favicon 卡片。
    /// </summary>
    public sealed partial class MetroTabGridControl : UserControl
    {
        public event Action<TabViewItem> TabSelected;
        public event Action<TabViewItem> TabCloseRequested;
        public event Action NewTabRequested;
        public event Action DismissRequested;

        private readonly Dictionary<TabViewItem, Border> _cards = new Dictionary<TabViewItem, Border>();
        private readonly Dictionary<TabViewItem, bool> _thumbReady = new Dictionary<TabViewItem, bool>();

        public MetroTabGridControl()
        {
            InitializeComponent();
        }

        public void Refresh(IReadOnlyList<TabViewItem> tabs, TabViewItem current)
        {
            GridHost.Children.Clear();
            _cards.Clear();
            _thumbReady.Clear();
            foreach (var item in tabs)
                GridHost.Children.Add(BuildCard(item, item == current));
            _ = CaptureAllAsync(tabs);
        }

        private Border BuildCard(TabViewItem item, bool isCurrent)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(isCurrent ? MetroColorPalette.MetroBlue : MetroColorPalette.TabActiveColor),
                BorderBrush = new SolidColorBrush(isCurrent ? MetroColorPalette.MetroBlue : MetroColorPalette.BorderColor),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(10)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });

            var thumb = new Grid { Background = new SolidColorBrush(MetroColorPalette.PanelColor) };
            var img = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false };
            var ring = new ProgressRing { IsActive = true, Width = 32, Height = 32, Visibility = Visibility.Collapsed };
            thumb.Children.Add(img);
            thumb.Children.Add(ring);

            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var favIcon = new Image { Width = 16, Height = 16, Margin = new Thickness(12, 0, 8, 0), Visibility = Visibility.Collapsed };
            var title = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI Light"),
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Text = item.Tab.Title ?? "新标签页"
            };
            Grid.SetColumn(title, 1);

            var closeBtn = new Button
            {
                Content = "\xE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = new SolidColorBrush(MetroColorPalette.MutedColor),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Width = 32,
                Height = 32,
                Tag = item
            };
            closeBtn.Click += CloseCardBtn_Click;
            Grid.SetColumn(closeBtn, 2);

            titleRow.Children.Add(favIcon);
            titleRow.Children.Add(title);
            titleRow.Children.Add(closeBtn);

            Grid.SetRow(thumb, 0);
            Grid.SetRow(titleRow, 1);
            grid.Children.Add(thumb);
            grid.Children.Add(titleRow);
            card.Child = grid;

            card.Tapped += (s, e) =>
            {
                // 点击关闭按钮不触发展开/切换（Tapped 会冒泡到卡片）
                if (e.OriginalSource is DependencyObject src && !IsWithinCloseButton(src))
                    TabSelected?.Invoke(item);
            };

            _cards[item] = card;
            _thumbReady[item] = false;

            // favicon 占位
            if (!string.IsNullOrEmpty(item.Tab.FaviconUri))
                _ = LoadFaviconAsync(item, favIcon);

            return card;
        }

        private bool IsWithinCloseButton(DependencyObject src)
        {
            DependencyObject cur = src;
            while (cur != null && cur != this)
            {
                if (cur is Button) return true;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return false;
        }

        private void CloseCardBtn_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is TabViewItem item)
                TabCloseRequested?.Invoke(item);
        }

        private static async Task LoadFaviconAsync(TabViewItem item, Image favIcon)
        {
            try
            {
                var bmp = new BitmapImage { UriSource = new Uri(item.Tab.FaviconUri) };
                favIcon.Source = bmp;
                favIcon.Visibility = Visibility.Visible;
            }
            catch { }
        }

        private async Task CaptureAllAsync(IReadOnlyList<TabViewItem> tabs)
        {
            foreach (var item in tabs)
            {
                if (_thumbReady.TryGetValue(item, out bool ready) && ready) continue;
                await CaptureOneAsync(item);
            }
        }

        private async Task CaptureOneAsync(TabViewItem item)
        {
            if (!_cards.TryGetValue(item, out var card)) return;
            var img = FindThumbImage(card);
            if (img == null) return;

            byte[] png = await WebCaptureService.CapturePagePngAsync(item.Tab);
            _thumbReady[item] = true;
            if (png == null || png.Length == 0) return;

            try
            {
                var bmp = new BitmapImage();
                var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var writer = new Windows.Storage.Streams.DataWriter(ras))
                {
                    writer.WriteBytes(png);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                }
                ras.Seek(0);
                await bmp.SetSourceAsync(ras);
                img.Source = bmp;
            }
            catch { }
        }

        private static Image FindThumbImage(Border card)
        {
            var grid = card.Child as Grid;
            if (grid?.Children.Count < 1) return null;
            return (grid.Children[0] as Grid)?.Children[0] as Image;
        }

        private void NewTabTile_Click(object sender, RoutedEventArgs e) => NewTabRequested?.Invoke();
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => DismissRequested?.Invoke();
        private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // 点击空白区域收起网格
            if (e.OriginalSource == RootGrid)
                DismissRequested?.Invoke();
        }
    }
}
