using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace EdgeRebuild.Controls
{
    /// <summary>
    /// “已搁置的标签页”面板：只负责搁置组的展示、还原与删除。
    /// 搁置动作由宿主执行（需要预览缓存），本控件通过 SweepOtherRequested 通知宿主。
    /// </summary>
    public sealed partial class SweptTabsPane : UserControl
    {
        public event Action CloseRequested;
        public event Action SweepOtherRequested;

        private TabManager _tabManager;
        private List<SweptTabGroup> _groups = new List<SweptTabGroup>();
        private readonly Dictionary<int, byte[]> _thumbnails = new Dictionary<int, byte[]>();
        private bool _refreshing;
        private bool _refreshPending;
        private bool _busy;

        public SweptTabsPane()
        {
            InitializeComponent();
        }

        public void Initialize(TabManager tabManager)
        {
            if (_tabManager != null || tabManager == null) return;
            _tabManager = tabManager;
            _tabManager.TabCountChanged += OnTabStateChanged;
            _tabManager.TabChanged += OnTabChanged;
        }

        private void OnTabStateChanged(int count) => UpdateSweepButtonState();

        private void OnTabChanged(TabViewItem item) => UpdateSweepButtonState();

        public async Task RefreshAsync()
        {
            // 刷新期间又有新请求（例如刚搁置完）时，等本轮结束后再读一次，避免显示旧列表
            if (_refreshing)
            {
                _refreshPending = true;
                return;
            }

            _refreshing = true;
            try
            {
                do
                {
                    _refreshPending = false;
                    LoadingRing.Visibility = Visibility.Visible;
                    LoadingRing.IsActive = true;
                    GroupsScroll.Visibility = Visibility.Collapsed;
                    EmptyState.Visibility = Visibility.Collapsed;

                    _groups = await SweptTabManager.LoadGroupsAsync();

                    // 丢弃已被还原/删除条目的缩略图，避免缓存随使用时间无限增长
                    var liveIds = new HashSet<int>(_groups.SelectMany(g => g.Tabs).Select(t => t.Id));
                    foreach (int staleId in _thumbnails.Keys.Where(id => !liveIds.Contains(id)).ToList())
                        _thumbnails.Remove(staleId);

                    BuildGroups();
                }
                while (_refreshPending);
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
                _refreshing = false;
            }
        }

        public void StoreThumbnails(SweptTabGroup group, IReadOnlyList<byte[]> thumbnails)
        {
            if (group?.Tabs == null || thumbnails == null) return;
            int count = Math.Min(group.Tabs.Count, thumbnails.Count);
            for (int i = 0; i < count; i++)
            {
                if (thumbnails[i] != null && thumbnails[i].Length > 0)
                    _thumbnails[group.Tabs[i].Id] = thumbnails[i];
            }
        }

        public void ClearThumbnailCache() => _thumbnails.Clear();

        public void FocusPane()
        {
            CloseBtn.Focus(FocusState.Programmatic);
        }

        public void ApplyTheme()
        {
            var colors = ThemeManager.Colors;
            if (colors == null) return;

            RootGrid.Background = colors.ToolbarBackground;
            HeaderBorder.BorderBrush = colors.SeparatorBrush;
            HeaderIcon.Foreground = colors.AccentBrush;
            TitleText.Foreground = colors.ForegroundBrush;
            SubtitleText.Foreground = colors.MutedForegroundBrush;
            CloseBtn.Foreground = colors.ForegroundBrush;

            var accentForeground = new SolidColorBrush(Colors.White);
            SweepOtherBtn.Background = colors.AccentBrush;
            SweepOtherBtn.BorderBrush = colors.AccentBrush;
            SweepOtherBtn.Foreground = accentForeground;
            EmptySweepBtn.Background = colors.AccentBrush;
            EmptySweepBtn.BorderBrush = colors.AccentBrush;
            EmptySweepBtn.Foreground = accentForeground;

            EmptyIcon.Foreground = colors.MutedForegroundBrush;
            EmptyTitle.Foreground = colors.ForegroundBrush;
            EmptyHint.Foreground = colors.MutedForegroundBrush;
            LoadingRing.Foreground = colors.AccentBrush;

            // 动态卡片在主题切换时重建，确保画笔不在旧主题下滞留
            if (GroupsScroll.Visibility == Visibility.Visible)
                BuildGroups();
        }

        private void BuildGroups()
        {
            GroupsPanel.Children.Clear();

            if (_groups.Count == 0)
            {
                GroupsScroll.Visibility = Visibility.Collapsed;
                EmptyState.Visibility = Visibility.Visible;
                UpdateSweepButtonState();
                return;
            }

            GroupsScroll.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;

            var colors = ThemeManager.Colors;
            var buckets = _groups
                .GroupBy(g => SweptTabTime.GetBucket(g.CreatedAt, DateTime.Now))
                .OrderBy(g => g.Key);

            foreach (var bucket in buckets)
            {
                GroupsPanel.Children.Add(new TextBlock
                {
                    Text = SweptTabTime.GetBucketTitle(bucket.Key),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = colors.MutedForegroundBrush,
                    Margin = new Thickness(2, 12, 0, 6)
                });

                foreach (var group in bucket.OrderByDescending(g => g.CreatedAt))
                    GroupsPanel.Children.Add(BuildGroupPanel(group, colors));
            }

            UpdateSweepButtonState();
        }

        private Border BuildGroupPanel(SweptTabGroup group, SkinManager.SkinColors colors)
        {
            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = group.Title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = colors.ForegroundBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var meta = new TextBlock
            {
                Text = FormatTime(group.CreatedAt),
                FontSize = 11,
                Foreground = colors.MutedForegroundBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };

            var restoreBtn = new Button
            {
                Content = "还原全部",
                Style = (Style)Resources["PaneTextButtonStyle"],
                Foreground = colors.ForegroundBrush,
                BorderBrush = colors.SeparatorBrush,
                Margin = new Thickness(0, 0, 4, 0)
            };
            AutomationProperties.SetName(restoreBtn, $"还原 {group.Title}");
            restoreBtn.Click += async (s, e) => await RestoreGroupAsync(group);

            var deleteBtn = new Button
            {
                Content = "删除",
                Style = (Style)Resources["PaneTextButtonStyle"],
                Foreground = colors.MutedForegroundBrush,
                BorderBrush = colors.SeparatorBrush
            };
            AutomationProperties.SetName(deleteBtn, $"删除 {group.Title}");
            deleteBtn.Click += async (s, e) => await DeleteGroupWithConfirmAsync(group);

            Grid.SetColumn(title, 0);
            Grid.SetColumn(meta, 1);
            Grid.SetColumn(restoreBtn, 2);
            Grid.SetColumn(deleteBtn, 3);
            header.Children.Add(title);
            header.Children.Add(meta);
            header.Children.Add(restoreBtn);
            header.Children.Add(deleteBtn);

            var cards = new ItemsControl
            {
                ItemsPanel = (ItemsPanelTemplate)Resources["SweptCardsPanel"],
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            foreach (var tab in group.Tabs.OrderBy(t => t.Position))
                cards.Items.Add(BuildCard(group, tab, colors));

            var content = new StackPanel();
            content.Children.Add(header);
            content.Children.Add(cards);

            return new Border
            {
                Background = colors.TabInactiveBackground,
                BorderBrush = colors.SeparatorBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 10),
                Margin = new Thickness(0, 0, 0, 12),
                Child = content
            };
        }

        private Button BuildCard(SweptTabGroup group, SweptTab tab, SkinManager.SkinColors colors)
        {
            var image = new Image
            {
                Stretch = Stretch.UniformToFill,
                Opacity = 0
            };
            var placeholder = new FontIcon
            {
                Glyph = "\uE7C3",
                FontSize = 26,
                Foreground = colors.MutedForegroundBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var thumbHost = new Grid { Background = colors.TabActiveBackground };
            thumbHost.Children.Add(placeholder);
            thumbHost.Children.Add(image);

            var favicon = new FontIcon
            {
                Glyph = "\uE774",
                FontSize = 11,
                Foreground = colors.MutedForegroundBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            string titleText = string.IsNullOrWhiteSpace(tab.Title)
                ? (string.IsNullOrWhiteSpace(tab.Url) ? "新标签页" : tab.Url)
                : tab.Title;
            var title = new TextBlock
            {
                Text = titleText,
                FontSize = 11,
                Foreground = colors.ForegroundBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(favicon, 0);
            Grid.SetColumn(title, 1);
            footerGrid.Children.Add(favicon);
            footerGrid.Children.Add(title);
            var footer = new Border
            {
                Background = colors.ToolbarBackground,
                Padding = new Thickness(6, 4, 6, 4),
                Child = footerGrid
            };

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(thumbHost, 0);
            Grid.SetRow(footer, 1);
            content.Children.Add(thumbHost);
            content.Children.Add(footer);

            var button = new Button
            {
                Content = content,
                Width = 142,
                Height = 120,
                Padding = new Thickness(0),
                Background = colors.TabActiveBackground,
                BorderBrush = colors.SeparatorBrush,
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 0, 8, 8),
                Tag = tab
            };
            ToolTipService.SetToolTip(button, titleText);
            AutomationProperties.SetName(button, $"{titleText}，按 Enter 还原此标签页");
            button.Click += async (s, e) => await RestoreTabAsync(group, tab);

            var menu = new MenuFlyout();
            var restoreItem = new MenuFlyoutItem { Text = "还原此标签页" };
            restoreItem.Click += async (s, e) => await RestoreTabAsync(group, tab);
            var deleteItem = new MenuFlyoutItem { Text = "删除此标签页" };
            deleteItem.Click += async (s, e) => await DeleteTabAsync(group, tab);
            menu.Items.Add(restoreItem);
            menu.Items.Add(deleteItem);
            button.ContextFlyout = menu;

            // 只复用宿主已有的悬停预览缓存；没有就显示骨架，不为搁置额外截图
            byte[] png = _thumbnails.TryGetValue(tab.Id, out var cached) ? cached : null;
            if (png != null && png.Length > 0)
                _ = LoadThumbnailAsync(image, placeholder, png);

            return button;
        }

        private static async Task LoadThumbnailAsync(Image image, FontIcon placeholder, byte[] png)
        {
            try
            {
                var bmp = new BitmapImage { DecodePixelWidth = 320 };
                using (var ms = new MemoryStream(png))
                    await bmp.SetSourceAsync(ms.AsRandomAccessStream());
                image.Source = bmp;
                image.Opacity = 1;
                placeholder.Visibility = Visibility.Collapsed;
            }
            catch
            {
                // 损坏/已释放的预览缓存按骨架处理
            }
        }

        private async Task RestoreGroupAsync(SweptTabGroup group)
        {
            if (_busy || group == null || _tabManager == null) return;
            _busy = true;
            try
            {
                var restored = await SweptTabManager.RestoreGroupAsync(_tabManager, group);
                if (restored.Count > 0)
                {
                    await _tabManager.SwitchToTabAsync(restored[0]);
                    NotificationService.ShowToast("标签页", $"已还原 {restored.Count} 个标签页");
                    CloseRequested?.Invoke();
                }
                else
                {
                    NotificationService.ShowToast("标签页", "还原失败，标签页可能已不可用");
                    await RefreshAsync();
                }
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task RestoreTabAsync(SweptTabGroup group, SweptTab tab)
        {
            if (_busy || tab == null || _tabManager == null) return;
            _busy = true;
            try
            {
                var view = await SweptTabManager.RestoreTabAsync(_tabManager, group, tab);
                if (view != null)
                {
                    await _tabManager.SwitchToTabAsync(view);
                    CloseRequested?.Invoke();
                }
                else
                {
                    NotificationService.ShowToast("标签页", "还原失败，标签页可能已不可用");
                    await RefreshAsync();
                }
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task DeleteGroupWithConfirmAsync(SweptTabGroup group)
        {
            if (_busy || group == null) return;
            var dialog = new ContentDialog
            {
                Title = "删除搁置组",
                Content = $"将删除 {group.Tabs.Count} 个标签页，无法撤销。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };
            PlatformCompat.SetDialogRoot(dialog, this);
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _busy = true;
            try
            {
                await SweptTabManager.DeleteGroupAsync(group);
                await RefreshAsync();
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task DeleteTabAsync(SweptTabGroup group, SweptTab tab)
        {
            if (_busy || tab == null) return;
            _busy = true;
            try
            {
                await SweptTabManager.DeleteTabAsync(group, tab);
                await RefreshAsync();
            }
            finally
            {
                _busy = false;
            }
        }

        private void UpdateSweepButtonState()
        {
            bool canSweep = _tabManager != null
                && !_tabManager.IsInPrivateWindow
                && _tabManager.Tabs.Any(t => !t.IsPinned && t.Tab != _tabManager.CurrentTab);
            SweepOtherBtn.IsEnabled = canSweep;
            EmptySweepBtn.IsEnabled = canSweep;
        }

        private static string FormatTime(DateTime time)
        {
            var now = DateTime.Now;
            if (SweptTabTime.GetBucket(time, now) == SweptTimeBucket.JustNow)
                return $"刚刚 · {time:HH:mm}";
            if (time.Date == now.Date)
                return $"今天 {time:HH:mm}";
            if (time.Date == now.Date.AddDays(-1))
                return $"昨天 {time:HH:mm}";
            return time.ToString("M月d日 HH:mm");
        }

        private void SweepOtherBtn_Click(object sender, RoutedEventArgs e)
            => SweepOtherRequested?.Invoke();

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
            => CloseRequested?.Invoke();

        private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CloseRequested?.Invoke();
                e.Handled = true;
            }
        }
    }
}
