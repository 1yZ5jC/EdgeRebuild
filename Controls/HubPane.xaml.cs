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
            HubNavView.SelectedItem = HubNavView.MenuItems[0];
            RefreshAll();
        }

        public void RefreshAll()
        {
            RefreshFavorites();
            RefreshHistory();
            RefreshDownloads();
        }

        public void ShowHistory()
        {
            if (HubNavView.MenuItems.Count > 1)
                HubNavView.SelectedItem = HubNavView.MenuItems[1];
        }

        public void RefreshFavorites()
        {
            if (FavStackPanel == null) return;
            FavStackPanel.Children.Clear();
            foreach (var fav in FavoritesManager.Instance.Favorites)
            {
                var stack = new StackPanel { Margin = new Thickness(4, 6, 4, 6), Padding = new Thickness(8), Background = new SolidColorBrush(Colors.Transparent) };
                stack.PointerEntered += (_, _) => stack.Background = new SolidColorBrush(Colors.LightGray);
                stack.PointerExited += (_, _) => stack.Background = new SolidColorBrush(Colors.Transparent);
                stack.Children.Add(new TextBlock { Text = fav.Title, FontWeight = Windows.UI.Text.FontWeights.SemiBold, FontSize = 14, Foreground = ForegroundBrush });
                stack.Children.Add(new TextBlock { Text = fav.Url, FontSize = 12, Foreground = MutedForegroundBrush, TextTrimming = TextTrimming.CharacterEllipsis });
                stack.PointerPressed += (_, ev) =>
                {
                    if (ev.Pointer.PointerDeviceType != Windows.Devices.Input.PointerDeviceType.Mouse || ev.GetCurrentPoint(stack).Properties.IsLeftButtonPressed)
                    {
                        NavigateRequested?.Invoke(fav.Url);
                    }
                };
                stack.RightTapped += (_, _) =>
                {
                    var flyout = new MenuFlyout();
                    var editItem = new MenuFlyoutItem { Text = "编辑" }; editItem.Click += async (_, _) =>
                    {
                        var titleBox = new TextBox { Text = fav.Title, PlaceholderText = "标题" }; var urlBox = new TextBox { Text = fav.Url, PlaceholderText = "网址" };
                        var panel = new StackPanel(); panel.Children.Add(titleBox); panel.Children.Add(urlBox);
                        var dialog = new ContentDialog { Title = "编辑收藏", Content = panel, PrimaryButtonText = "保存", SecondaryButtonText = "取消" };
                        if (await dialog.ShowAsync() == ContentDialogResult.Primary) { fav.Title = titleBox.Text; fav.Url = urlBox.Text; RefreshFavorites(); }
                    };
                    var deleteItem = new MenuFlyoutItem { Text = "删除" }; deleteItem.Click += (_, _) => { FavoritesManager.Instance.Remove(fav); RefreshFavorites(); };
                    flyout.Items.Add(editItem); flyout.Items.Add(deleteItem);
                    flyout.ShowAt(stack);
                };
                FavStackPanel.Children.Add(stack);
            }
        }

        public void RefreshHistory()
        {
            if (HistoryStackPanel == null) return;
            HistoryStackPanel.Children.Clear();
            foreach (var item in HistoryManager.History)
            {
                var stack = new StackPanel { Margin = new Thickness(4, 6, 4, 6) };
                stack.Children.Add(new TextBlock { Text = item.Title, FontWeight = Windows.UI.Text.FontWeights.SemiBold, FontSize = 14, Foreground = ForegroundBrush });
                stack.Children.Add(new TextBlock { Text = $"{item.Url} - {item.VisitTime:g}", FontSize = 11, Foreground = MutedForegroundBrush });
                stack.PointerPressed += (_, ev) =>
                {
                    if (ev.Pointer.PointerDeviceType != Windows.Devices.Input.PointerDeviceType.Mouse || ev.GetCurrentPoint(stack).Properties.IsLeftButtonPressed)
                    {
                        NavigateRequested?.Invoke(item.Url);
                    }
                };
                stack.RightTapped += (_, _) =>
                {
                    var flyout = new MenuFlyout();
                    var deleteItem = new MenuFlyoutItem { Text = "删除" }; deleteItem.Click += (_, _) => { HistoryManager.History.Remove(item); RefreshHistory(); };
                    flyout.Items.Add(deleteItem);
                    flyout.ShowAt(stack);
                };
                HistoryStackPanel.Children.Add(stack);
            }
        }

        public void RefreshDownloads()
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
                // 外层容器，确保边距和拉伸
                var container = new Grid
                {
                    Margin = new Thickness(4, 6, 4, 6),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 文件名区域
                container.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 按钮区域

                // 左侧：文件名、路径、状态、进度
                var leftStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };

                var nameBlock = new TextBlock
                {
                    Text = item.FileName,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    FontSize = 14,
                    Foreground = ForegroundBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 320  // 适当限制宽度，避免挤压按钮
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
                            var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                            await Windows.System.Launcher.LaunchFileAsync(file);
                        }
                        catch { }
                    };
                    nameBlock.PointerEntered += (_, _) => nameBlock.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
                    nameBlock.PointerExited += (_, _) => nameBlock.TextDecorations = Windows.UI.Text.TextDecorations.None;
                    nameBlock.Foreground = new SolidColorBrush(Colors.DodgerBlue);
                }
                leftStack.Children.Add(nameBlock);

                // 路径或状态文字
                if (item.Status == "已完成" && !item.Deleted)
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

                // 进度条（仅非终态且未删除显示）
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

                // 状态文本
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

                // 右侧：操作按钮
                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 0)
                };

                if (item.Status == "下载中")
                {
                    buttonsPanel.Children.Add(CreateIconButton("\uE769", "暂停", () => { item.Pause(); RefreshDownloads(); }));
                }
                else if (item.Status == "已暂停")
                {
                    buttonsPanel.Children.Add(CreateIconButton("\uE768", "继续", () => { item.Resume(); RefreshDownloads(); }));
                }

                if (item.Status == "下载中" || item.Status == "已暂停")
                {
                    buttonsPanel.Children.Add(CreateIconButton("\uE711", "取消", () => { item.Cancel(); RefreshDownloads(); }));
                }

                if (item.Status == "已中断" || item.Status == "下载失败" || item.Status == "已取消")
                {
                    buttonsPanel.Children.Add(CreateIconButton("\uE72C", "重试", async () => { await item.RetryAsync(); RefreshDownloads(); }));
                }

                if (item.Status == "已完成" && !item.Deleted)
                {
                    buttonsPanel.Children.Add(CreateIconButton("\uE8E5", "打开", async () =>
                    {
                        try
                        {
                            var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                            await Windows.System.Launcher.LaunchFileAsync(file);
                        }
                        catch { }
                    }));
                    buttonsPanel.Children.Add(CreateIconButton("\uE838", "文件夹", async () =>
                    {
                        try
                        {
                            var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                            var folder = await file.GetParentAsync();
                            if (folder != null) await Windows.System.Launcher.LaunchFolderAsync(folder);
                        }
                        catch { }
                    }));
                }

                // 删除按钮始终显示
                buttonsPanel.Children.Add(CreateIconButton("\uE74D", "删除记录", async () =>
                {
                    await DownloadManager.DeleteDownloadAsync(item);
                    RefreshDownloads();
                }));

                Grid.SetColumn(buttonsPanel, 1);
                container.Children.Add(buttonsPanel);

                DownloadsStackPanel.Children.Add(container);
            }
        }

        // 辅助方法：创建图标按钮
        private Button CreateIconButton(string glyph, string tooltip, Action clickAction)
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
            btn.Click += (_, _) => clickAction();
            return btn;
        }

        private void HubNavView_SelectionChanged(Microsoft.UI.Xaml.Controls.NavigationView sender, Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is Microsoft.UI.Xaml.Controls.NavigationViewItem item && item.Tag is string tag)
            {
                FavStackPanel.Visibility = (tag == "Favorites") ? Visibility.Visible : Visibility.Collapsed;
                HistoryStackPanel.Visibility = (tag == "History") ? Visibility.Visible : Visibility.Collapsed;
                DownloadsStackPanel.Visibility = (tag == "Downloads") ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}