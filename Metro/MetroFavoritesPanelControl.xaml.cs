using EdgeRebuild.Core;
using System;
using System.Linq;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Metro
{
    /// <summary>
    /// Metro IE 收藏夹面板：黑色 IE 面板，文件夹逐级进入，点击收藏在当前标签打开。
    /// 星标按钮打开；「添加当前页」总是添加到根目录（与全局收藏规则一致）。
    /// </summary>
    public sealed partial class MetroFavoritesPanelControl : UserControl
    {
        public event Action? CloseRequested;
        public event Action? AddCurrentRequested;
        public event Action<string>? FavoriteSelected;

        private int? _folderId;

        public MetroFavoritesPanelControl()
        {
            InitializeComponent();
        }

        /// <summary>重新读取收藏夹并重建当前层级的列表。</summary>
        public void Refresh()
        {
            if (ListHost == null) return;

            var manager = FavoritesManager.Instance;

            // 当前文件夹可能已被删除：退回根目录
            if (_folderId.HasValue && !manager.Folders.Any(f => f.Id == _folderId.Value))
                _folderId = null;

            BackBtn.Visibility = _folderId.HasValue ? Visibility.Visible : Visibility.Collapsed;
            string path = _folderId.HasValue ? manager.GetFolderPath(_folderId.Value) : "全部收藏";
            PathText.Text = string.IsNullOrWhiteSpace(path) ? "全部收藏" : path;

            var folders = manager.GetFolders(_folderId).ToList();
            var items = manager.GetItems(_folderId).ToList();

            ListHost.Children.Clear();
            foreach (var folder in folders)
                ListHost.Children.Add(BuildFolderRow(folder));
            foreach (var item in items)
                ListHost.Children.Add(BuildFavoriteRow(item));

            EmptyText.Text = _folderId.HasValue ? "此文件夹为空" : "还没有收藏";
            EmptyText.Visibility = folders.Count == 0 && items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>每次打开面板回到根目录，避免上次浏览位置造成困惑。</summary>
        public void ResetToRoot() => _folderId = null;

        public void SetAddEnabled(bool enabled) => AddCurrentBtn.IsEnabled = enabled;

        private Button BuildFolderRow(FavoriteFolder folder)
        {
            var button = new Button
            {
                Style = (Style)Resources["FavoriteRowStyle"],
                Tag = folder
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new FontIcon
            {
                Glyph = "\uE8B7",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(icon, 0);

            var title = new TextBlock
            {
                Text = folder.Name,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(title, 1);

            var chevron = new FontIcon
            {
                Glyph = "\uE76C",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x8E, 0x8E, 0x8E)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(chevron, 2);

            grid.Children.Add(icon);
            grid.Children.Add(title);
            grid.Children.Add(chevron);

            button.Content = grid;
            button.Click += FolderRow_Click;
            AutomationProperties.SetName(button, "文件夹 " + folder.Name);
            return button;
        }

        private Button BuildFavoriteRow(FavoriteItem item)
        {
            var button = new Button
            {
                Style = (Style)Resources["FavoriteRowStyle"],
                Height = 48,
                Tag = item
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new FontIcon
            {
                Glyph = "\uE734",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 15,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(icon, 0);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            text.Children.Add(new TextBlock
            {
                Text = item.Url,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x8E, 0x8E, 0x8E)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(text, 1);

            grid.Children.Add(icon);
            grid.Children.Add(text);

            button.Content = grid;
            button.Click += FavoriteRow_Click;
            AutomationProperties.SetName(button, "打开收藏 " + (item.Title ?? item.Url));
            return button;
        }

        private void FolderRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is FavoriteFolder folder)
            {
                _folderId = folder.Id;
                Refresh();
            }
        }

        private void FavoriteRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is FavoriteItem item)
                FavoriteSelected?.Invoke(item.Url);
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_folderId.HasValue) return;
            var folder = FavoritesManager.Instance.Folders.FirstOrDefault(f => f.Id == _folderId.Value);
            _folderId = folder?.ParentId;
            Refresh();
        }

        private void AddCurrentBtn_Click(object sender, RoutedEventArgs e) => AddCurrentRequested?.Invoke();

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

        private void Scrim_PointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                CloseRequested?.Invoke();
            }
        }
    }
}
