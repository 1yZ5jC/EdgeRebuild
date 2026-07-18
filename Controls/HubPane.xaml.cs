using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Controls
{
    public sealed partial class HubPane : UserControl
    {
        public event Action<string> NavigateRequested;

        public SolidColorBrush ForegroundBrush { get; set; } = new SolidColorBrush(Colors.Black);
        public SolidColorBrush MutedForegroundBrush { get; set; } = new SolidColorBrush(Colors.DimGray);

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

        public void RefreshAll()
        {
            RefreshFavorites();
            RefreshHistory();
            LoadDownloads();
        }

        public void RefreshFavorites()
        {
            if (FavListView == null) return;
            // 向 Items 添加纯数据对象，保留虚拟化
            FavListView.Items.Clear();
            foreach (var fav in FavoritesManager.Instance.Favorites)
                FavListView.Items.Add(fav);
        }

        public void RefreshHistory()
        {
            if (HistoryListView == null) return;
            HistoryListView.Items.Clear();
            foreach (var hist in HistoryManager.History)
                HistoryListView.Items.Add(hist);
        }

        public void RefreshDownloads() => LoadDownloads();

        public void ShowHistory()
        {
            if (HubNavView.MenuItems.Count > 1)
                HubNavView.SelectedItem = HubNavView.MenuItems[1];
        }

        // ========== 收藏夹容器填充（虚拟化 + 手动赋值） ==========
        private void FavListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
                return;

            // 获取数据对象
            var fav = args.Item as FavoriteItem;
            if (fav == null) return;

            // 从模板根元素查找命名控件
            var root = args.ItemContainer.ContentTemplateRoot as Grid;
            if (root == null) return;

            var titleText = root.FindName("TitleText") as TextBlock;
            var urlText = root.FindName("UrlText") as TextBlock;

            if (titleText != null) titleText.Text = fav.Title ?? "";
            if (urlText != null) urlText.Text = fav.Url ?? "";

            args.Handled = true;
        }

        // ========== 历史记录容器填充 ==========
        private void HistoryListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
                return;

            var hist = args.Item as HistoryItem;
            if (hist == null) return;

            var root = args.ItemContainer.ContentTemplateRoot as Grid;
            if (root == null) return;

            var titleText = root.FindName("TitleText") as TextBlock;
            var urlText = root.FindName("UrlText") as TextBlock;

            if (titleText != null) titleText.Text = hist.Title ?? "";
            if (urlText != null) urlText.Text = hist.Url ?? "";

            args.Handled = true;
        }

        // ========== 点击事件 ==========
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

        // ========== 下载面板（完整保留） ==========
        public void LoadDownloads()
        {
            if (DownloadsStackPanel == null) return;
            DownloadsStackPanel.Children.Clear();

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
                var container = new Grid
                {
                    Margin = new Thickness(4, 6, 4, 6),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
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

                if (item.Deleted)
                {
                    nameBlock.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
                    nameBlock.Foreground = new SolidColorBrush(Colors.Gray);
                }
                else if (item.Status == "已完成")
                {
                    nameBlock.Tapped += async (_, _) =>
                    {
                        try
                        {
                            var f = await StorageFile.GetFileFromPathAsync(item.FullPath);
                            await Windows.System.Launcher.LaunchFileAsync(f);
                        }
                        catch { }
                    };
                    nameBlock.PointerEntered += (_, _) =>
                        nameBlock.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
                    nameBlock.PointerExited += (_, _) =>
                        nameBlock.TextDecorations = Windows.UI.Text.TextDecorations.None;
                    nameBlock.Foreground = new SolidColorBrush(Colors.DodgerBlue);
                }
                leftStack.Children.Add(nameBlock);

                if (item.Status == "已完成" && !item.Deleted)
                    leftStack.Children.Add(new TextBlock
                    {
                        Text = item.FullPath,
                        FontSize = 10,
                        Foreground = MutedForegroundBrush,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 0, 0, 2)
                    });
                else if (item.Deleted)
                    leftStack.Children.Add(new TextBlock
                    {
                        Text = "文件已删除",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.Red),
                        Margin = new Thickness(0, 0, 0, 2)
                    });

                if (!item.Deleted && item.Status != "已完成" && item.Status != "下载失败")
                {
                    var progress = new Windows.UI.Xaml.Controls.ProgressBar
                    {
                        Maximum = 100,
                        Value = item.Progress,
                        IsIndeterminate = item.Indeterminate,
                        Height = 4,
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    leftStack.Children.Add(progress);
                }

                string statusText = item.Status;
                if (item.Status == "下载中" && !item.Indeterminate)
                    statusText += $" - {item.Progress:F1}%";
                else if (item.Status == "下载中" && item.Indeterminate)
                    statusText += " (大小未知)";
                leftStack.Children.Add(new TextBlock
                {
                    Text = statusText,
                    FontSize = 11,
                    Foreground = MutedForegroundBrush
                });

                Grid.SetColumn(leftStack, 0);
                container.Children.Add(leftStack);

                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };

                if (item.Status == "下载中")
                    buttonsPanel.Children.Add(CreateIconButton("\uE769", "暂停", () =>
                    {
                        item.Pause();
                        LoadDownloads();
                    }));
                else if (item.Status == "已暂停")
                    buttonsPanel.Children.Add(CreateIconButton("\uE768", "继续", () =>
                    {
                        item.Resume();
                        LoadDownloads();
                    }));

                if (item.Status == "下载中" || item.Status == "已暂停")
                    buttonsPanel.Children.Add(CreateIconButton("\uE711", "取消", () =>
                    {
                        item.Cancel();
                        LoadDownloads();
                    }));

                if (item.Status == "已中断" || item.Status == "下载失败" || item.Status == "已取消")
                    buttonsPanel.Children.Add(CreateIconButton("\uE72C", "重试", async () =>
                    {
                        await item.RetryAsync();
                        LoadDownloads();
                    }));

                if (item.Status == "已完成" && !item.Deleted)
                {
                    buttonsPanel.Children.Add(CreateIconButton("\uE8E5", "打开", async () =>
                    {
                        try
                        {
                            var f = await StorageFile.GetFileFromPathAsync(item.FullPath);
                            await Windows.System.Launcher.LaunchFileAsync(f);
                        }
                        catch { }
                    }));
                    buttonsPanel.Children.Add(CreateIconButton("\uE838", "文件夹", async () =>
                    {
                        try
                        {
                            var f = await StorageFile.GetFileFromPathAsync(item.FullPath);
                            var folder = await f.GetParentAsync();
                            if (folder != null)
                                await Windows.System.Launcher.LaunchFolderAsync(folder);
                        }
                        catch { }
                    }));
                }

                buttonsPanel.Children.Add(CreateIconButton("\uE74D", "删除记录", async () =>
                {
                    await DownloadManager.DeleteDownloadAsync(item);
                    LoadDownloads();
                }));

                Grid.SetColumn(buttonsPanel, 1);
                container.Children.Add(buttonsPanel);
                DownloadsStackPanel.Children.Add(container);
            }
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
                FavListView.Visibility = tag == "Favorites" ? Visibility.Visible : Visibility.Collapsed;
                HistoryListView.Visibility = tag == "History" ? Visibility.Visible : Visibility.Collapsed;
                DownloadsContainer.Visibility = tag == "Downloads" ? Visibility.Visible : Visibility.Collapsed;

                if (tag == "Downloads")
                    LoadDownloads();
            }
        }
    }
}