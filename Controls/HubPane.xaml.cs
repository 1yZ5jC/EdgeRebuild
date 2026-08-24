using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Controls
{
    public sealed class HistoryTemplateSelector : DataTemplateSelector
    {
        public DataTemplate HeaderTemplate { get; set; }
        public DataTemplate ItemTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item)
            => item is HistoryGroup ? HeaderTemplate : ItemTemplate;
    }

    /// <summary>历史分组键(参考规范 §7.7:past-hour/today/yesterday/date-YYYY-M-D/earlier)。</summary>
    public sealed class HistoryGroup
    {
        public string Key { get; set; }
        public string HeaderText { get; set; }
        public List<HistoryItem> Items { get; } = new List<HistoryItem>();
        public bool IsExpanded { get; set; }

        public string CountText => Items.Count > 0 ? $"{Items.Count} 项" : "";
    }

    public sealed partial class HubPane : UserControl
    {
        public event Action<string> NavigateRequested;

        /// InPrivate 模式：历史面板内显示“不记录历史”提示，不加载列表
        public bool SuppressHistory { get; set; }

        public SolidColorBrush ForegroundBrush { get; set; } = new SolidColorBrush(Colors.Black);
        public SolidColorBrush MutedForegroundBrush { get; set; } = new SolidColorBrush(Colors.DimGray);

        private readonly Dictionary<DownloadItem, Grid> _downloadContainers = new Dictionary<DownloadItem, Grid>();

        // 搜索过滤
        private string _historySearchFilter = "";
        private string _favoritesSearchFilter = "";

        // 历史分组展开状态(参考规范:默认仅展开 past-hour)
        private readonly HashSet<string> _expandedHistoryGroups = new HashSet<string> { "past-hour" };

        // 历史项 → 组 映射,供虚拟化容器回收后恢复折叠状态
        private readonly Dictionary<HistoryItem, HistoryGroup> _historyGroupByItem = new Dictionary<HistoryItem, HistoryGroup>();

        public HubPane()
        {
            this.InitializeComponent();
            this.Loaded += OnHubPaneLoaded;
        }

        private void OnHubPaneLoaded(object sender, RoutedEventArgs e)
        {
            RefreshFavorites();
            RefreshHistory();
            RefreshReadingList();
            LoadDownloads();
            HubNavView.SelectedItem = HubNavView.MenuItems[0];
        }

        public void ApplySkinColors(SolidColorBrush foreground, SolidColorBrush muted, bool isDark)
        {
            ForegroundBrush = foreground;
            MutedForegroundBrush = muted;

            ImportFavBtn.Foreground = foreground;
            ExportFavBtn.Foreground = foreground;
            ClearHistoryBtn.Foreground = foreground;
            FavoritesSearchBox.Foreground = foreground;
            HistorySearchBox.Foreground = foreground;

            Style itemStyle = isDark
                ? (Style)Resources["HubListViewItemStyleDark"]
                : (Style)Resources["HubListViewItemStyleLight"];

            if (FavListView != null) FavListView.ItemContainerStyle = itemStyle;
            if (HistoryListView != null) HistoryListView.ItemContainerStyle = itemStyle;
        }

        public void RefreshAll()
        {
            RefreshFavorites();
            RefreshHistory();
            RefreshReadingList();
            LoadDownloads();
        }

        // ---- 收藏夹搜索 ----
        private void FavoritesSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _favoritesSearchFilter = FavoritesSearchBox.Text?.Trim() ?? "";
            RefreshFavorites();
        }

        public void RefreshFavorites()
        {
            if (FavListView == null) return;
            FavListView.Items.Clear();
            var items = string.IsNullOrEmpty(_favoritesSearchFilter)
                ? FavoritesManager.Instance.Favorites
                : FavoritesManager.Instance.Favorites.Where(f =>
                    f.Title?.Contains(_favoritesSearchFilter, StringComparison.OrdinalIgnoreCase) == true ||
                    f.Url?.Contains(_favoritesSearchFilter, StringComparison.OrdinalIgnoreCase) == true);
            foreach (var fav in items)
                FavListView.Items.Add(fav);
        }

        // ---- 历史记录搜索 ----
        private void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _historySearchFilter = HistorySearchBox.Text?.Trim() ?? "";
            RefreshHistory();
        }

        public void RefreshHistory()
        {
            if (SuppressHistory) return;   // InPrivate：面板显示提示，不加载列表
            if (HistoryListView == null) return;
            HistoryListView.Items.Clear();
            _historyGroupByItem.Clear();

            var filtered = string.IsNullOrEmpty(_historySearchFilter)
                ? HistoryManager.History
                : HistoryManager.History.Where(h =>
                    h.Title?.Contains(_historySearchFilter, StringComparison.OrdinalIgnoreCase) == true ||
                    h.Url?.Contains(_historySearchFilter, StringComparison.OrdinalIgnoreCase) == true);

            // 搜索时平铺显示;否则按时间分组(过去 1 小时/今天/昨天/各日期/更早)
            if (!string.IsNullOrEmpty(_historySearchFilter))
            {
                foreach (var item in filtered)
                    HistoryListView.Items.Add(item);
                return;
            }

            foreach (var group in BuildHistoryGroups(filtered))
            {
                HistoryListView.Items.Add(group);
                if (!group.IsExpanded) continue;
                foreach (var item in group.Items)
                {
                    HistoryListView.Items.Add(item);
                    _historyGroupByItem[item] = group;
                }
            }
        }

        private static readonly string[] WeekdayNames = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

        private static string GetGroupKey(DateTime t)
        {
            var now = DateTime.Now;
            if (t >= now.AddHours(-1)) return "past-hour";
            if (t.Date == now.Date) return "today";
            if (t.Date == now.Date.AddDays(-1)) return "yesterday";
            return t.Date >= now.Date.AddDays(-5) ? $"date-{t:yyyy-M-d}" : "earlier";
        }

        private static string GetGroupHeaderText(DateTime t)
        {
            var now = DateTime.Now;
            string key = GetGroupKey(t);
            if (key == "past-hour") return "过去 1 小时";
            if (key == "today") return $"今天 - {now.Month}/{now.Day}";
            if (key == "yesterday") return "昨天";
            if (key.StartsWith("date-"))
            {
                var day = t.DayOfWeek;
                return $"{WeekdayNames[(int)day]} - {t.Month}/{t.Day}";
            }
            return "更早";
        }

        private List<HistoryGroup> BuildHistoryGroups(IEnumerable<HistoryItem> items)
        {
            var groups = new Dictionary<string, HistoryGroup>();
            foreach (var item in items)
            {
                string key = GetGroupKey(item.VisitTime);
                if (!groups.TryGetValue(key, out var group))
                    groups[key] = group = new HistoryGroup { Key = key, HeaderText = GetGroupHeaderText(item.VisitTime) };
                group.Items.Add(item);
            }
            var ordered = groups.Values
                .OrderBy(g => g.Key == "past-hour" ? 0 : g.Key == "today" ? 1 : g.Key == "yesterday" ? 2 : g.Key.StartsWith("date-") ? 3 : 4)
                .ThenByDescending(g => g.Key)
                .ToList();
            foreach (var g in ordered)
            {
                g.Items.Sort((a, b) => b.VisitTime.CompareTo(a.VisitTime));
                g.IsExpanded = _expandedHistoryGroups.Contains(g.Key) || g.Key == "past-hour";
            }
            return ordered;
        }

        private void ToggleHistoryGroup(HistoryGroup group)
        {
            group.IsExpanded = !group.IsExpanded;
            if (group.IsExpanded) _expandedHistoryGroups.Add(group.Key);
            else _expandedHistoryGroups.Remove(group.Key);

            // 就地更新已实现的容器,避免整体重渲染丢滚动位置
            if (HistoryListView.ItemContainerGenerator.ContainerFromItem(group) is ListViewItem headerContainer
                && headerContainer.ContentTemplateRoot is Grid headerRoot
                && headerRoot.FindName("ChevronText") is TextBlock chevron)
            {
                chevron.Text = group.IsExpanded ? "\uE70E" : "\uE76C";
            }
            foreach (var item in group.Items)
            {
                if (HistoryListView.ItemContainerGenerator.ContainerFromItem(item) is ListViewItem itemContainer)
                    itemContainer.Visibility = group.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public void ShowHistory()
        {
            if (HubNavView.MenuItems.Count > 1)
                HubNavView.SelectedItem = HubNavView.MenuItems[1];
            if (!SuppressHistory)
                RefreshHistory();
        }

        public void ShowDownloads()
        {
            if (HubNavView.MenuItems.Count > 2)
                HubNavView.SelectedItem = HubNavView.MenuItems[2];
            LoadDownloads();
        }

        public void RefreshDownloads() => LoadDownloads();

        private async void ImportFavBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".html");
            picker.FileTypeFilter.Add(".htm");
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                int imported = await FavoritesManager.ImportFromHtmlAsync(file);
                await new ContentDialog
                {
                    Title = "导入完成",
                    Content = $"成功导入 {imported} 个书签。",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                }.ShowAsync();
                RefreshFavorites();
            }
        }

        private async void ExportFavBtn_Click(object sender, RoutedEventArgs e)
        {
            StorageFolder folder;
            try { folder = await DownloadsFolder.CreateFolderAsync("EdgeRebuild", CreationCollisionOption.OpenIfExists); }
            catch { folder = ApplicationData.Current.LocalFolder; }

            var file = await folder.CreateFileAsync("favorites.html", CreationCollisionOption.GenerateUniqueName);
            await FavoritesManager.ExportToHtmlAsync(file);

            var dialog = new ContentDialog
            {
                Title = "导出成功",
                Content = $"收藏夹已导出为 HTML 书签文件。\n保存位置：{file.Path}",
                PrimaryButtonText = "打开文件夹",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var parentFolder = await file.GetParentAsync();
                await Launcher.LaunchFolderAsync(parentFolder);
            }
        }

        private void ClearHistoryBtn_Click(object sender, RoutedEventArgs e)
        {
            _ = HistoryManager.ClearAsync();
            RefreshHistory();
        }

        private void FavItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var grid = sender as Grid;
            if (grid == null) return;
            var fav = grid.DataContext as FavoriteItem;
            if (fav == null) return;

            var menu = new MenuFlyout();
            var renameItem = new MenuFlyoutItem { Text = "重命名" };
            renameItem.Click += async (s, args) =>
            {
                var box = new TextBox { Text = fav.Title ?? "" };
                var dialog = new ContentDialog
                {
                    Title = "重命名书签",
                    Content = box,
                    PrimaryButtonText = "确定",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
                {
                    await FavoritesManager.Instance.RenameAsync(fav, box.Text.Trim());
                    RefreshFavorites();
                }
            };
            menu.Items.Add(renameItem);

            var deleteItem = new MenuFlyoutItem { Text = "删除" };
            deleteItem.Click += (s, args) =>
            {
                _ = FavoritesManager.Instance.RemoveAsync(fav);
                RefreshFavorites();
            };
            menu.Items.Add(deleteItem);

            var copyLinkItem = new MenuFlyoutItem { Text = "复制链接" };
            copyLinkItem.Click += (s, args) =>
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(fav.Url);
                Clipboard.SetContent(dataPackage);
            };
            menu.Items.Add(copyLinkItem);

            menu.ShowAt(grid, e.GetPosition(grid));
        }

        // ---- 多选管理模式 ----
        private void ToggleManageBtn_Click(object sender, RoutedEventArgs e)
        {
            bool managing = FavListView.SelectionMode == ListViewSelectionMode.None;
            FavListView.SelectionMode = managing ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
            FavListView.IsItemClickEnabled = !managing;
            ManageBar.Visibility = managing ? Visibility.Visible : Visibility.Collapsed;
            FavListView.SelectedItems.Clear();
            UpdateManageCount();
        }

        private void ManageCancelBtn_Click(object sender, RoutedEventArgs e)
            => ToggleManageBtn_Click(null, null);

        private void FavListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateManageCount();

        private void UpdateManageCount()
        {
            if (ManageCountText == null || ManageDeleteBtn == null) return;
            int count = FavListView.SelectedItems.Count;
            ManageCountText.Text = count + " 项已选择";
            ManageDeleteBtn.IsEnabled = count > 0;
        }

        private async void ManageDeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = FavListView.SelectedItems.Cast<FavoriteItem>().ToList();
            foreach (var fav in selected)
                await FavoritesManager.Instance.RemoveAsync(fav);
            ToggleManageBtn_Click(null, null);
            RefreshFavorites();
        }

        private void HistoryItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var grid = sender as Grid;
            if (grid == null) return;
            var hist = grid.DataContext as HistoryItem;
            if (hist == null) return;

            var menu = new MenuFlyout();
            var deleteItem = new MenuFlyoutItem { Text = "删除" };
            deleteItem.Click += (s, args) =>
            {
                _ = HistoryManager.RemoveAsync(hist);
                RefreshHistory();
            };
            menu.Items.Add(deleteItem);

            var copyLinkItem = new MenuFlyoutItem { Text = "复制链接" };
            copyLinkItem.Click += (s, args) =>
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(hist.Url);
                Clipboard.SetContent(dataPackage);
            };
            menu.Items.Add(copyLinkItem);

            menu.ShowAt(grid, e.GetPosition(grid));
        }

        private void FavListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;
            var fav = args.Item as FavoriteItem;
            if (fav == null) return;
            args.ItemContainer.DataContext = fav;

            var root = args.ItemContainer.ContentTemplateRoot as Grid;
            if (root == null) return;
            var titleText = root.FindName("TitleText") as TextBlock;
            var urlText = root.FindName("UrlText") as TextBlock;
            var dateText = root.FindName("DateText") as TextBlock;

            if (titleText != null) { titleText.Text = fav.Title ?? ""; titleText.Foreground = ForegroundBrush; }
            if (urlText != null) { urlText.Text = fav.Url ?? ""; urlText.Foreground = MutedForegroundBrush; }
            if (dateText != null)
            {
                dateText.Text = fav.AddedDate != DateTime.MinValue ? fav.AddedDate.ToString("yyyy-MM-dd HH:mm") : "";
                dateText.Foreground = MutedForegroundBrush;
            }
            args.Handled = true;
        }

        private void HistoryListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                args.ItemContainer.Visibility = Visibility.Visible;
                return;
            }

            if (args.Item is HistoryGroup group)
            {
                args.ItemContainer.DataContext = group;
                var headerRoot = args.ItemContainer.ContentTemplateRoot as Grid;
                if (headerRoot != null)
                {
                    if (headerRoot.FindName("ChevronText") is TextBlock chevron)
                    {
                        chevron.Text = group.IsExpanded ? "\uE70E" : "\uE76C";
                        chevron.Foreground = MutedForegroundBrush;
                    }
                    if (headerRoot.FindName("HeaderTitleText") is TextBlock titleText)
                    {
                        titleText.Text = group.HeaderText;
                        titleText.Foreground = ForegroundBrush;
                    }
                    if (headerRoot.FindName("HeaderCountText") is TextBlock countText)
                    {
                        countText.Text = group.CountText;
                        countText.Foreground = MutedForegroundBrush;
                    }
                }
                args.Handled = true;
                return;
            }

            var hist = args.Item as HistoryItem;
            if (hist == null) return;
            args.ItemContainer.DataContext = hist;

            var root = args.ItemContainer.ContentTemplateRoot as Grid;
            if (root != null)
            {
                var titleText = root.FindName("TitleText") as TextBlock;
                var urlText = root.FindName("UrlText") as TextBlock;
                var timeText = root.FindName("TimeText") as TextBlock;

                if (titleText != null) { titleText.Text = hist.Title ?? ""; titleText.Foreground = ForegroundBrush; }
                if (urlText != null) { urlText.Text = hist.Url ?? ""; urlText.Foreground = MutedForegroundBrush; }
                if (timeText != null)
                {
                    timeText.Text = hist.VisitTime != DateTime.MinValue ? hist.VisitTime.ToString("yyyy-MM-dd HH:mm") : "";
                    timeText.Foreground = MutedForegroundBrush;
                }
                if (_historyGroupByItem.TryGetValue(hist, out var itemGroup) && !itemGroup.IsExpanded)
                    args.ItemContainer.Visibility = Visibility.Collapsed;
            }
            args.Handled = true;
        }

        private void FavListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FavoriteItem fav)
                NavigateRequested?.Invoke(fav.Url);
        }

        private void HistoryListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is HistoryGroup group)
                ToggleHistoryGroup(group);
            else if (e.ClickedItem is HistoryItem hist)
                NavigateRequested?.Invoke(hist.Url);
        }

        private void HistoryHeader_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var headerRoot = sender as Grid;
            if (headerRoot == null) return;
            var group = headerRoot.DataContext as HistoryGroup;
            if (group == null) return;

            var menu = new MenuFlyout();
            var toggleItem = new MenuFlyoutItem { Text = group.IsExpanded ? "折叠" : "展开" };
            toggleItem.Click += (s, args) => ToggleHistoryGroup(group);
            menu.Items.Add(toggleItem);

            var deleteGroupItem = new MenuFlyoutItem { Text = $"清除此分组 ({group.Items.Count} 项)" };
            deleteGroupItem.Click += async (s, args) =>
            {
                await HistoryManager.RemoveManyAsync(group.Items.Select(i => i.Id).ToList());
                RefreshHistory();
            };
            menu.Items.Add(deleteGroupItem);

            menu.ShowAt(headerRoot, e.GetPosition(headerRoot));
        }

        public void UpdateDownloadItem(DownloadItem item)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (!_downloadContainers.TryGetValue(item, out var container))
                {
                    LoadDownloads();
                    return;
                }
                UpdateContainer(container, item);
            });
        }

        private void UpdateContainer(Grid container, DownloadItem item)
        {
            var leftStack = container.Children[0] as StackPanel;
            if (leftStack == null) return;

            if (leftStack.Children[0] is TextBlock nameBlock)
            {
                nameBlock.Text = item.FileName;
                if (item.Deleted)
                {
                    nameBlock.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
                    nameBlock.Foreground = new SolidColorBrush(Colors.Gray);
                }
                else if (item.Status == DownloadStatus.Completed)
                {
                    nameBlock.TextDecorations = Windows.UI.Text.TextDecorations.None;
                    nameBlock.Foreground = new SolidColorBrush(Colors.DodgerBlue);
                }
                else
                {
                    nameBlock.TextDecorations = Windows.UI.Text.TextDecorations.None;
                    nameBlock.Foreground = ForegroundBrush;
                }
            }

            while (leftStack.Children.Count > 1)
                leftStack.Children.RemoveAt(1);

            if (item.Status == DownloadStatus.Completed && !item.Deleted)
            {
                leftStack.Children.Add(new TextBlock
                {
                    Text = item.FullPath,
                    FontSize = 10,
                    Foreground = MutedForegroundBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 0, 2)
                });
            }
            else if (item.Deleted)
            {
                leftStack.Children.Add(new TextBlock
                {
                    Text = "文件已删除",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Red),
                    Margin = new Thickness(0, 0, 0, 2)
                });
            }

            if (!item.Deleted && item.IsActive && item.Status != DownloadStatus.Completed && item.Status != DownloadStatus.Failed)
            {
                leftStack.Children.Add(new ProgressBar
                {
                    Maximum = 100,
                    Value = item.Progress,
                    IsIndeterminate = item.Indeterminate,
                    Height = 4,
                    Margin = new Thickness(0, 2, 0, 2)
                });
            }

            string statusText = item.StatusText;
            if (item.Status == DownloadStatus.Downloading && !item.Indeterminate) statusText += $" - {item.Progress:F1}%";
            else if (item.Status == DownloadStatus.Downloading && item.Indeterminate) statusText += " (大小未知)";
            leftStack.Children.Add(new TextBlock { Text = statusText, FontSize = 11, Foreground = MutedForegroundBrush });

            var rightStack = container.Children[1] as StackPanel;
            if (rightStack != null)
            {
                rightStack.Children.Clear();
                BuildButtonPanel(rightStack, item);
            }
        }

        private void BuildButtonPanel(StackPanel panel, DownloadItem item)
        {
            if (item.Status == DownloadStatus.Downloading)
                panel.Children.Add(CreateIconButton("\uE769", "暂停", () => { item.Pause(); UpdateDownloadItem(item); }));
            else if (item.Status == DownloadStatus.Paused)
                panel.Children.Add(CreateIconButton("\uE768", "继续", () => { item.Resume(); UpdateDownloadItem(item); }));

            if (item.IsActive && item.Status != DownloadStatus.Paused)
                panel.Children.Add(CreateIconButton("\uE711", "取消", () => { item.Cancel(); UpdateDownloadItem(item); }));

            if (item.Status == DownloadStatus.Interrupted || item.Status == DownloadStatus.Failed || item.Status == DownloadStatus.Cancelled)
                panel.Children.Add(CreateIconButton("\uE72C", "重试", () => { item.Retry(); UpdateDownloadItem(item); }));

            if (item.Status == DownloadStatus.Completed && !item.Deleted)
            {
                panel.Children.Add(CreateIconButton("\uE8E5", "打开", async () =>
                {
                    try { var f = await StorageFile.GetFileFromPathAsync(item.FullPath); await Launcher.LaunchFileAsync(f); } catch { }
                }));
                panel.Children.Add(CreateIconButton("\uE838", "文件夹", async () =>
                {
                    try { var f = await StorageFile.GetFileFromPathAsync(item.FullPath); var folder = await f.GetParentAsync(); if (folder != null) await Launcher.LaunchFolderAsync(folder); } catch { }
                }));
            }

            panel.Children.Add(CreateIconButton("\uE74D", "删除记录", async () =>
            {
                var dlg = new ContentDialog
                {
                    Title = "删除下载",
                    Content = item.IsActive ? "下载正在进行，确定取消并删除吗？" : "确定删除这条下载记录吗？",
                    PrimaryButtonText = "确定",
                    SecondaryButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };
                var result = await dlg.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    await DownloadManager.DeleteDownloadAsync(item, item.IsActive);
                    _downloadContainers.Remove(item);
                    LoadDownloads();
                }
            }));
        }

        public void LoadDownloads()
        {
            if (DownloadsStackPanel == null) return;
            DownloadsStackPanel.Children.Clear();
            _downloadContainers.Clear();

            if (!DownloadManager.CanUseSystemDownloadFolder)
            {
                DownloadsStackPanel.Children.Add(new TextBlock
                {
                    Text = "无系统下载权限，文件将保存到应用本地文件夹。可在 Windows 设置 → 隐私 → 文件系统中开启权限。",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Orange),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 0, 4, 6)
                });
            }

            foreach (var item in DownloadManager.Downloads)
            {
                var container = new Grid { Margin = new Thickness(4, 6, 4, 6), HorizontalAlignment = HorizontalAlignment.Stretch };
                container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                container.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var leftStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
                var nameBlock = new TextBlock
                {
                    Text = item.FileName,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    FontSize = 14,
                    Foreground = ForegroundBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 320
                };

                if (!item.Deleted && item.Status == DownloadStatus.Completed)
                {
                    nameBlock.Tapped += async (_, _) =>
                    {
                        try { var f = await StorageFile.GetFileFromPathAsync(item.FullPath); await Launcher.LaunchFileAsync(f); } catch { }
                    };
                    nameBlock.PointerEntered += (_, _) => nameBlock.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
                    nameBlock.PointerExited += (_, _) => nameBlock.TextDecorations = Windows.UI.Text.TextDecorations.None;
                    nameBlock.Foreground = new SolidColorBrush(Colors.DodgerBlue);
                }
                else if (item.Deleted)
                {
                    nameBlock.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
                    nameBlock.Foreground = new SolidColorBrush(Colors.Gray);
                }
                leftStack.Children.Add(nameBlock);

                if (item.Status == DownloadStatus.Completed && !item.Deleted)
                    leftStack.Children.Add(new TextBlock { Text = item.FullPath, FontSize = 10, Foreground = MutedForegroundBrush, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 2) });
                else if (item.Deleted)
                    leftStack.Children.Add(new TextBlock { Text = "文件已删除", FontSize = 11, Foreground = new SolidColorBrush(Colors.Red), Margin = new Thickness(0, 0, 0, 2) });

                if (!item.Deleted && item.IsActive && item.Status != DownloadStatus.Completed && item.Status != DownloadStatus.Failed)
                    leftStack.Children.Add(new ProgressBar { Maximum = 100, Value = item.Progress, IsIndeterminate = item.Indeterminate, Height = 4, Margin = new Thickness(0, 2, 0, 2) });

                string statusText = item.StatusText;
                if (item.Status == DownloadStatus.Downloading && !item.Indeterminate) statusText += $" - {item.Progress:F1}%";
                else if (item.Status == DownloadStatus.Downloading && item.Indeterminate) statusText += " (大小未知)";
                leftStack.Children.Add(new TextBlock { Text = statusText, FontSize = 11, Foreground = MutedForegroundBrush });

                Grid.SetColumn(leftStack, 0);
                container.Children.Add(leftStack);

                var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                BuildButtonPanel(buttonsPanel, item);
                Grid.SetColumn(buttonsPanel, 1);
                container.Children.Add(buttonsPanel);

                container.RightTapped += DownloadItem_RightTapped;
                container.Tag = item;

                DownloadsStackPanel.Children.Add(container);
                _downloadContainers[item] = container;
            }
        }

        private void DownloadItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var grid = sender as Grid;
            if (grid == null) return;
            var item = grid.Tag as DownloadItem;
            if (item == null) return;

            var menu = new MenuFlyout();
            var copyLinkItem = new MenuFlyoutItem { Text = "复制下载链接" };
            copyLinkItem.Click += (s, args) => { var dp = new DataPackage(); dp.SetText(item.Url); Clipboard.SetContent(dp); };
            menu.Items.Add(copyLinkItem);

            if (item.Status == DownloadStatus.Completed && !item.Deleted && File.Exists(item.FullPath))
            {
                var openItem = new MenuFlyoutItem { Text = "打开文件" };
                openItem.Click += async (s, args) => { try { var f = await StorageFile.GetFileFromPathAsync(item.FullPath); await Launcher.LaunchFileAsync(f); } catch { } };
                menu.Items.Add(openItem);
            }

            if (!item.Deleted && (item.Status == DownloadStatus.Completed || File.Exists(item.FullPath)))
            {
                var openFolderItem = new MenuFlyoutItem { Text = "打开文件夹" };
                openFolderItem.Click += async (s, args) => { try { var f = await StorageFile.GetFileFromPathAsync(item.FullPath); var folder = await f.GetParentAsync(); if (folder != null) await Launcher.LaunchFolderAsync(folder); } catch { } };
                menu.Items.Add(openFolderItem);
            }

            menu.Items.Add(new MenuFlyoutSeparator());
            var deleteItem = new MenuFlyoutItem { Text = "删除记录" };
            deleteItem.Click += async (s, args) =>
            {
                await DownloadManager.DeleteDownloadAsync(item, item.IsActive);
                _downloadContainers.Remove(item);
                LoadDownloads();
            };
            menu.Items.Add(deleteItem);

            menu.ShowAt(grid, e.GetPosition(grid));
        }

        private Button CreateIconButton(string glyph, string tooltip, Action action)
        {
            var btn = new Button
            {
                Content = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = ForegroundBrush,
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0)
            };
            ToolTipService.SetToolTip(btn, tooltip);
            btn.Click += (_, _) => action();
            return btn;
        }

        private void HubNavView_SelectionChanged(Microsoft.UI.Xaml.Controls.NavigationView sender, Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is Microsoft.UI.Xaml.Controls.NavigationViewItem item && item.Tag is string tag)
            {
                if (SuppressHistory && tag == "History")
                {
                    // InPrivate 模式：不加载历史列表，面板内显示提示
                    FavPanel.Visibility = Visibility.Collapsed;
                    HistoryPanel.Visibility = Visibility.Visible;
                    DownloadsContainer.Visibility = Visibility.Collapsed;
                    HistoryToolbar.Visibility = Visibility.Collapsed;
                    HistoryListView.Visibility = Visibility.Collapsed;
                    InPrivateNotice.Visibility = Visibility.Visible;
                    return;
                }
                FavPanel.Visibility = tag == "Favorites" ? Visibility.Visible : Visibility.Collapsed;
                HistoryPanel.Visibility = tag == "History" ? Visibility.Visible : Visibility.Collapsed;
                ReadingPanel.Visibility = tag == "ReadingList" ? Visibility.Visible : Visibility.Collapsed;
                DownloadsContainer.Visibility = tag == "Downloads" ? Visibility.Visible : Visibility.Collapsed;
                if (tag == "Favorites") RefreshFavorites();
                if (tag == "History") RefreshHistory();
                if (tag == "ReadingList") RefreshReadingList();
                if (tag == "Downloads") LoadDownloads();
            }
        }

        // ---- 稍后读（旧版 Edge 阅读列表） ----
        public void RefreshReadingList()
        {
            if (ReadingListView == null) return;
            ReadingListView.Items.Clear();
            foreach (var item in ReadingListManager.Instance.Items)
                ReadingListView.Items.Add(item);
            ApplyThemeToReadingList();
        }

        private void ApplyThemeToReadingList()
        {
            var fg = ForegroundBrush?.Color ?? Colors.Black;
            var muted = MutedForegroundBrush?.Color ?? Colors.DimGray;
            foreach (var container in ReadingListView.ItemsPanelRoot?.Children ?? Enumerable.Empty<DependencyObject>())
            {
                if (!(container is ListViewItem lvi) || !(lvi.Content is ReadingItem)) continue;
                var root = FindElementByName(lvi, "RootGrid");
                if (root == null) continue;
                var title = FindElementByName(root, "TitleText") as TextBlock;
                var urlText = FindElementByName(root, "UrlText") as TextBlock;
                var dateText = FindElementByName(root, "DateText") as TextBlock;
                if (title != null) title.Foreground = new SolidColorBrush(fg);
                if (urlText != null) urlText.Foreground = new SolidColorBrush(muted);
                if (dateText != null) dateText.Foreground = new SolidColorBrush(muted);
            }
        }

        private static DependencyObject FindElementByName(DependencyObject parent, string name)
        {
            int count = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && fe.Name == name) return fe;
                var found = FindElementByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void ReadingListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ReadingItem item)
            {
                NavigateRequested?.Invoke(item.Url);
                // 读完即删（阅读列表语义：一次性队列）
                _ = ReadingListManager.Instance.RemoveAsync(item);
                RefreshReadingList();
            }
        }

        private void ReadingItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var grid = sender as Grid;
            if (grid?.DataContext is ReadingItem item)
            {
                var menu = new MenuFlyout();
                var openNewTab = new MenuFlyoutItem { Text = "在新标签页打开并移除" };
                openNewTab.Click += (s, a) =>
                {
                    NavigateRequested?.Invoke(item.Url);
                    _ = ReadingListManager.Instance.RemoveAsync(item);
                    RefreshReadingList();
                };
                menu.Items.Add(openNewTab);
                var removeItem = new MenuFlyoutItem { Text = "移除" };
                removeItem.Click += async (s, a) =>
                {
                    await ReadingListManager.Instance.RemoveAsync(item);
                    RefreshReadingList();
                };
                menu.Items.Add(removeItem);
                menu.ShowAt(grid, e.GetPosition(grid));
            }
            e.Handled = true;
        }
    }
}