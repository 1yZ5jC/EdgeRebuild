using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace EdgeRebuild.Metro
{
    /// <summary>
    /// Metro IE 风格的全屏标签缩略图网格：卡片 = 缩略图 + 标题 + favicon + 关闭。
    /// 打开时按标签逐个异步捕获缩略图（并行 + 超时），失败/挂起/空白页降级为 favicon 卡片。
    /// 非活动标签的容器是 Collapsed（WebView2 捕获会失败），捕获前临时置为可见再还原。
    /// 布局用 GridView（ItemsWrapGrid 折行）：ScrollViewer+WrapGrid 的折行测量
    /// （HorizontalScrollMode=Disabled）在本 AppWindow 内首次布局即冻结 UI 线程。
    /// 覆盖层由 MetroPage 保持常驻可见（Opacity=0），打开/关闭只切透明度与命中。
    /// </summary>
    public sealed partial class MetroTabGridControl : UserControl
    {
        public event Action<TabViewItem> TabSelected;
        public event Action<TabViewItem> TabCloseRequested;
        public event Action NewTabRequested;
        public event Action DismissRequested;

        private readonly Dictionary<TabViewItem, Border> _cards = new Dictionary<TabViewItem, Border>();
        private readonly Dictionary<TabViewItem, bool> _thumbReady = new Dictionary<TabViewItem, bool>();
        private const int CaptureTimeoutMs = 3500;
        private int _generation;   // Refresh 递增：旧一轮捕获作废，防止切标签重入导致重复捕获/回写旧卡片

        // 诊断输出：VF 输出窗口即时可见 + crash.log 落盘（含完整堆栈）
        private static void GridDebug(string msg)
        {
            System.Diagnostics.Debug.WriteLine("[Metro网格] " + msg);
            Services.Diagnostics.Log("[Metro网格] " + msg);
        }

        public MetroTabGridControl()
        {
            InitializeComponent();
            GridDebug($"控件构造完成");
        }

        public void Refresh(IReadOnlyList<TabViewItem> tabs, TabViewItem current)
        {
            GridDebug($"Refresh 开始: 标签数={(tabs == null ? 0 : tabs.Count)} 当前={(current == null ? "null" : current.Tab.Title)}");
            GridHost.Items.Clear();
            _cards.Clear();
            _thumbReady.Clear();
            int built = 0;
            for (int i = 0; tabs != null && i < tabs.Count; i++)
            {
                try
                {
                    GridHost.Items.Add(BuildCard(tabs[i], tabs[i] == current));
                    built++;
                }
                catch (Exception ex)
                {
                    GridDebug($"第 {i} 张卡片构建异常: {ex}");
                }
            }
            GridDebug($"Refresh 完成: 已建卡片 {built} 网格项数={GridHost.Items.Count}");

            // 空状态提示
            EmptyHint.Visibility = (tabs == null || tabs.Count == 0)
                ? Visibility.Visible : Visibility.Collapsed;

            int gen = ++_generation;
            _ = CaptureAllAsync(tabs, current, gen);
        }

        // 点击卡片 → 选中标签（GridViewItem 点击；关闭按钮会吞掉自己的点击,不触发本事件）
        private void GridHost_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Border card && card.Tag is TabViewItem item)
            {
                GridDebug($"卡片点击: {item.Tab.Title}");
                TabSelected?.Invoke(item);
            }
        }

        private Border BuildCard(TabViewItem item, bool isCurrent)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(isCurrent ? MetroColorPalette.MetroBlue : MetroColorPalette.TabActiveColor),
                BorderBrush = new SolidColorBrush(isCurrent ? MetroColorPalette.MetroBlue : MetroColorPalette.BorderColor),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(10),
                Tag = item
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });

            var thumb = new Grid { Background = new SolidColorBrush(MetroColorPalette.PanelColor) };
            var img = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false };
            // ProgressRing 在显式 Width/Height + IsActive=true 时可能在布局测量阶段抛
            // E_UNEXPECTED(0x8000FFFF,crash.log 已捕获到),这里不给显式尺寸、改由 32x32
            // 包装 Grid 定尺寸,并在捕获开始时才置 IsActive=true(与文档「不可见时停动画」一致)
            var ringWrap = new Grid
            {
                Width = 32,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var ring = new ProgressRing { IsActive = false, Visibility = Visibility.Visible };
            ringWrap.Children.Add(ring);
            thumb.Children.Add(img);
            thumb.Children.Add(ringWrap);

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

            _cards[item] = card;
            _thumbReady[item] = false;

            // favicon 占位
            if (!string.IsNullOrEmpty(item.Tab.FaviconUri))
                _ = LoadFaviconAsync(item, favIcon);

            return card;
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
            catch (Exception ex) { GridDebug($"favicon 加载失败 {item.Tab.FaviconUri}: {ex}"); }
        }

        // ---- 缩略图捕获：并行 + 每标签超时，避免单个标签卡死整张网格 ----
        // 全程 try/catch + 日志：捕获触发的中断（未观察任务异常 / 标签关闭后旧任务
        // KeyNotFound / EdgeHTML 捕获空转）都不会再向上传播。
        private async Task CaptureAllAsync(IReadOnlyList<TabViewItem> tabs, TabViewItem current, int gen)
        {
            try
            {
                var tasks = new List<Task>();
                foreach (var item in tabs)
                    tasks.Add(CaptureOneWithTimeoutAsync(item, item == current, gen));
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                GridDebug($"缩略图捕获批任务异常: {ex}");
            }
        }

        private async Task CaptureOneWithTimeoutAsync(TabViewItem item, bool isCurrent, int gen)
        {
            try
            {
                var capture = CaptureOneAsync(item, isCurrent, gen);
                var done = await Task.WhenAny(capture, Task.Delay(CaptureTimeoutMs));
                // 超时 → 放弃，保留 favicon 卡片；字典可能已被 Refresh 重建（标签已关闭），须 ContainsKey
                if (done != capture)
                {
                    GridDebug($"捕获超时放弃: {(item.Tab.Title ?? item.Tab.CurrentUrl)}");
                    if (gen == _generation && _thumbReady.ContainsKey(item))
                        _thumbReady[item] = true;
                }
            }
            catch (Exception ex)
            {
                GridDebug($"单标签捕获超时任务异常: {ex}");
            }
        }

        private async Task CaptureOneAsync(TabViewItem item, bool isCurrent, int gen)
        {
            GridDebug($"开始捕获: {item.Tab.Title ?? item.Tab.CurrentUrl} 引擎={item.Tab.Engine} 挂起={item.Tab.IsSuspended}");
            try
            {
                if (item.Tab.IsSuspended) return;   // 已挂起标签无法捕获，保留 favicon 卡片
                if (item.Tab is EdgeHtmlTab) return;   // UWP RenderTargetBitmap 无法捕获 WebView 内容,只会空转阻塞 UI,跳过
                string url = item.Tab.CurrentUrl;
                if (string.IsNullOrEmpty(url) || url == "about:blank") return;

                if (!_cards.TryGetValue(item, out var card)) return;
                var img = FindThumbImage(card);
                if (img == null) return;
                var ring = FindThumbRing(card);
                if (ring != null) { ring.IsActive = true; ring.Visibility = Visibility.Visible; }

                // 非活动标签容器 Collapsed → 捕获必失败；临时置可见（覆盖层不透明，用户无感知）
                var container = item.ContentContainer;
                bool restoreVisibility = false;
                if (container != null && container.Visibility != Visibility.Visible)
                {
                    container.Visibility = Visibility.Visible;
                    restoreVisibility = true;
                }

                byte[] png = null;
                try { png = await WebCaptureService.CapturePagePngAsync(item.Tab); }
                catch (Exception ex) { GridDebug($"页面捕获失败: {ex}"); }
                finally
                {
                    try
                    {
                        if (restoreVisibility && container != null && !isCurrent)
                            container.Visibility = Visibility.Collapsed;
                    }
                    catch { }
                }

                // Refresh 已重建（切标签/关标签）→ 旧一轮作废，不再回写旧卡片
                if (gen != _generation)
                {
                    GridDebug("捕获完成但轮次已过期，丢弃结果");
                    return;
                }

                GridDebug($"捕获结果: 字节={(png == null ? 0 : png.Length)}");
                if (_thumbReady.ContainsKey(item)) _thumbReady[item] = true;
                if (ring != null) { ring.IsActive = false; ring.Visibility = Visibility.Collapsed; }
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
                catch (Exception ex) { GridDebug($"缩略图解码失败: {ex}"); }
            }
            catch (Exception ex)
            {
                GridDebug($"卡片捕获异常: {ex}");
            }
        }

        // 卡片内部结构：grid.Children[0]=thumb grid，thumb.Children[0]=Image，[1]=32x32 包装 Grid(内含 ProgressRing)
        private static Image FindThumbImage(Border card)
        {
            var grid = card.Child as Grid;
            if (grid?.Children.Count < 1) return null;
            return (grid.Children[0] as Grid)?.Children[0] as Image;
        }

        private static ProgressRing FindThumbRing(Border card)
        {
            var grid = card.Child as Grid;
            if (grid?.Children.Count < 1) return null;
            return ((grid.Children[0] as Grid)?.Children[1] as Grid)?.Children[0] as ProgressRing;
        }

        private void NewTabTile_Click(object sender, RoutedEventArgs e) => NewTabRequested?.Invoke();
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => DismissRequested?.Invoke();

        private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // 点击空白区域收起网格（卡片区域不触发：指针落在 GridView 子树上）
            if (e.OriginalSource == RootGrid)
                DismissRequested?.Invoke();
        }
    }
}