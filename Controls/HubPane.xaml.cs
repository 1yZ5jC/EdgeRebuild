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
    public sealed partial class HubPane : UserControl
    {
        public event Action<string> NavigateRequested;

        public SolidColorBrush ForegroundBrush { get; set; } = new SolidColorBrush(Colors.Black);
        public SolidColorBrush MutedForegroundBrush { get; set; } = new SolidColorBrush(Colors.DimGray);

        private readonly Dictionary<DownloadItem, Grid> _downloadContainers = new Dictionary<DownloadItem, Grid>();

        // 搜索过滤
        private string _historySearchFilter = "";
        private string _favoritesSearchFilter = "";

        public HubPane()
        {
            this.InitializeComponent();
            this.Loaded += OnHubPaneLoaded;
        }

        private void OnHubPaneLoaded(object sender, RoutedEventArgs e)
        {
            RefreshFavorites();
            RefreshHistory();
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
            if (HistoryListView == null) return;
            HistoryListView.Items.Clear();
            var items = string.IsNullOrEmpty(_historySearchFilter)
                ? HistoryManager.History
                : HistoryManager.History.Where(h =>
                    h.Title?.Contains(_historySearchFilter, StringComparison.OrdinalIgnoreCase) == true ||
                    h.Url?.Contains(_historySearchFilter, StringComparison.OrdinalIgnoreCase) == true);
            foreach (var item in items)
                HistoryListView.Items.Add(item);
        }

        public void ShowHistory()
        {
            if (HubNavView.MenuItems.Count > 1)
                HubNavView.SelectedItem = HubNavView.MenuItems[1];
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
            if (args.InRecycleQueue) return;
            var hist = args.Item as HistoryItem;
            if (hist == null) return;
            args.ItemContainer.DataContext = hist;

            var root = args.ItemContainer.ContentTemplateRoot as Grid;
            if (root == null) return;
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
            args.Handled = true;
        }

        private void FavListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FavoriteItem fav)
                NavigateRequested?.Invoke(fav.Url);
        }

        private void HistoryListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is HistoryItem hist)
                NavigateRequested?.Invoke(hist.Url);
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
                FavPanel.Visibility = tag == "Favorites" ? Visibility.Visible : Visibility.Collapsed;
                HistoryPanel.Visibility = tag == "History" ? Visibility.Visible : Visibility.Collapsed;
                DownloadsContainer.Visibility = tag == "Downloads" ? Visibility.Visible : Visibility.Collapsed;
                if (tag == "Favorites") RefreshFavorites();
                if (tag == "History") RefreshHistory();
                if (tag == "Downloads") LoadDownloads();
            }
        }
    }
}