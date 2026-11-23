using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace EdgeRebuild.Metro
{
    /// <summary>
    /// IE11 式底部 Tabs 面板：横向缩略图 + favicon/标题/关闭。
    /// 与全屏网格共用 WebCaptureService 捕获逻辑；面板由 MetroPage 放进 Flyout 承载，
    /// 从而避开 EdgeHTML WebView 的 airspace 覆盖问题。
    /// </summary>
    public sealed partial class MetroTabsPanelControl : UserControl
    {
        public event Action<TabViewItem>? TabSelected;
        public event Action<TabViewItem>? TabCloseRequested;
        public event Action? NewTabRequested;
        public event Action? ExpandRequested;
        public event Action? CloseAllRequested;
        public event Action? DismissRequested;

        private sealed class CardRefs
        {
            public Border Card = null!;
            public Image Thumb = null!;
            public ProgressRing Ring = null!;
        }

        private readonly Dictionary<TabViewItem, CardRefs> _cards = new Dictionary<TabViewItem, CardRefs>();
        private TabViewItem? _current;
        private int _generation;
        private const int CaptureTimeoutMs = 3000;

        public MetroTabsPanelControl()
        {
            InitializeComponent();
        }

        public void Refresh(IReadOnlyList<TabViewItem>? tabs, TabViewItem? current)
        {
            TabList.Items.Clear();
            _cards.Clear();
            _current = current;
            int gen = ++_generation;

            int built = 0;
            for (int i = 0; tabs != null && i < tabs.Count; i++)
            {
                try
                {
                    var card = BuildCard(tabs[i], tabs[i] == current);
                    TabList.Items.Add(card);
                    built++;
                }
                catch (Exception ex)
                {
                    Log($"第 {i} 张卡片构建异常: {ex}");
                }
            }

            EmptyHint.Visibility = tabs == null || tabs.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
            Log($"Refresh: 标签数={tabs?.Count ?? 0} 已建卡片={built}");

            _ = CaptureAllAsync(tabs, gen);
        }

        private Border BuildCard(TabViewItem item, bool isCurrent)
        {
            var card = new Border
            {
                Width = 172,
                Height = 146,
                Margin = new Thickness(6),
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F)),
                BorderBrush = new SolidColorBrush(isCurrent
                    ? MetroColorPalette.MetroBlue
                    : Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A)),
                BorderThickness = new Thickness(isCurrent ? 2 : 1),
                Tag = item
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

            // 缩略图区
            var thumbHost = new Grid { Background = new SolidColorBrush(MetroColorPalette.PanelColor) };
            var thumb = new Image { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
            var ring = new ProgressRing
            {
                IsActive = false,
                Visibility = Visibility.Collapsed,
                Width = 24,
                Height = 24
            };
            thumbHost.Children.Add(thumb);
            thumbHost.Children.Add(ring);

            // 标题行：favicon + 标题 + 关闭
            var titleRow = new Grid { Padding = new Thickness(8, 0, 2, 0) };
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var favicon = new Image
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 6, 0),
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center
            };
            var title = new TextBlock
            {
                Text = item.Tab.Title ?? "新标签页",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(title, 1);

            var closeBtn = new Button
            {
                Content = "\uE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = new SolidColorBrush(MetroColorPalette.MutedColor),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                MinWidth = 0,
                MinHeight = 0,
                Tag = item
            };
            closeBtn.Click += CloseCardBtn_Click;
            AutomationProperties.SetName(closeBtn, $"关闭标签页：{item.Tab.Title ?? "新标签页"}");
            Grid.SetColumn(closeBtn, 2);

            titleRow.Children.Add(favicon);
            titleRow.Children.Add(title);
            titleRow.Children.Add(closeBtn);

            Grid.SetRow(thumbHost, 0);
            Grid.SetRow(titleRow, 1);
            grid.Children.Add(thumbHost);
            grid.Children.Add(titleRow);
            card.Child = grid;

            _cards[item] = new CardRefs { Card = card, Thumb = thumb, Ring = ring };

            if (!string.IsNullOrEmpty(item.Tab.FaviconUri))
                _ = LoadFaviconAsync(item, favicon);

            return card;
        }

        private static async Task LoadFaviconAsync(TabViewItem item, Image favicon)
        {
            try
            {
                var bmp = new BitmapImage { UriSource = new Uri(item.Tab.FaviconUri) };
                favicon.Source = bmp;
                favicon.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Log($"favicon 加载失败 {item.Tab.FaviconUri}: {ex.Message}");
            }
        }

        private async Task CaptureAllAsync(IReadOnlyList<TabViewItem>? tabs, int gen)
        {
            if (tabs == null) return;
            try
            {
                var tasks = new List<Task>();
                foreach (var item in tabs)
                    tasks.Add(CaptureOneWithTimeoutAsync(item, gen));
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Log($"缩略图批任务异常: {ex}");
            }
        }

        private async Task CaptureOneWithTimeoutAsync(TabViewItem item, int gen)
        {
            try
            {
                var capture = CaptureOneAsync(item, gen);
                var done = await Task.WhenAny(capture, Task.Delay(CaptureTimeoutMs));
                if (done != capture)
                {
                    Log($"捕获超时: {item.Tab.Title ?? item.Tab.CurrentUrl}");
                    StopRing(item);
                }
            }
            catch (Exception ex)
            {
                Log($"捕获超时任务异常: {ex}");
                StopRing(item);
            }
        }

        private async Task CaptureOneAsync(TabViewItem item, int gen)
        {
            try
            {
                // 面板模式下只截当前标签：后台标签的容器是 Collapsed，若为捕获临时
                // 置为可见，WebView2 的 airspace 会盖在 Flyout 面板之上，造成闪烁。
                // 其余卡片保留 favicon + 标题，点击后切换即可看到真实缩略图。
                if (item != _current) return;
                if (item.Tab.IsSuspended) return;
                string url = item.Tab.CurrentUrl;
                if (string.IsNullOrEmpty(url) || url == "about:blank") return;
                if (!_cards.TryGetValue(item, out var refs)) return;

                refs.Ring.Visibility = Visibility.Visible;
                refs.Ring.IsActive = true;

                byte[]? png = null;
                try
                {
                    png = await WebCaptureService.CapturePagePngAsync(item.Tab);
                }
                catch (Exception ex)
                {
                    Log($"页面捕获失败: {ex.Message}");
                }

                if (gen != _generation) return;
                if (png == null || png.Length == 0) return;

                var bmp = await DecodeAsync(png);
                if (gen != _generation) return;
                if (bmp != null && _cards.TryGetValue(item, out var latest))
                    latest.Thumb.Source = bmp;
            }
            catch (Exception ex)
            {
                Log($"卡片捕获异常: {ex}");
            }
            finally
            {
                StopRing(item);
            }
        }

        private static async Task<BitmapImage?> DecodeAsync(byte[] png)
        {
            try
            {
                var bmp = new BitmapImage();
                var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var writer = new Windows.Storage.Streams.DataWriter(ras))
                {
                    writer.WriteBytes(png);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }
                ras.Seek(0);
                await bmp.SetSourceAsync(ras);
                return bmp;
            }
            catch (Exception ex)
            {
                Log($"缩略图解码失败: {ex.Message}");
                return null;
            }
        }

        private void StopRing(TabViewItem item)
        {
            if (_cards.TryGetValue(item, out var refs))
            {
                refs.Ring.IsActive = false;
                refs.Ring.Visibility = Visibility.Collapsed;
            }
        }

        private void TabList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Border card && card.Tag is TabViewItem item)
                TabSelected?.Invoke(item);
        }

        private void CloseCardBtn_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is TabViewItem item)
                TabCloseRequested?.Invoke(item);
        }

        private void NewTabBtn_Click(object sender, RoutedEventArgs e) => NewTabRequested?.Invoke();

        // 右上“…”：与原版一致，把低频操作收进菜单
        private void MoreBtn_Click(object sender, RoutedEventArgs e)
        {
            var menu = new MenuFlyout();

            var expandItem = new MenuFlyoutItem
            {
                Text = "全屏标签视图",
                Icon = new FontIcon { Glyph = "\uE740", FontFamily = new FontFamily("Segoe MDL2 Assets") }
            };
            expandItem.Click += (s, a) => ExpandRequested?.Invoke();
            menu.Items.Add(expandItem);

            var closeAllItem = new MenuFlyoutItem
            {
                Text = "关闭所有标签",
                Icon = new FontIcon { Glyph = "\uE711", FontFamily = new FontFamily("Segoe MDL2 Assets") }
            };
            closeAllItem.Click += (s, a) => CloseAllRequested?.Invoke();
            menu.Items.Add(closeAllItem);

            menu.ShowAt(MoreBtn);
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => DismissRequested?.Invoke();

        private static void Log(string msg)
        {
            System.Diagnostics.Debug.WriteLine("[Metro标签面板] " + msg);
            Services.Diagnostics.Log("[Metro标签面板] " + msg);
        }
    }
}
