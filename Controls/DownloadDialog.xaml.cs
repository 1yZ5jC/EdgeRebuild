using System;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Controls
{
    public sealed partial class DownloadDialog : ContentDialog
    {
        private StorageFolder _selectedFolder;

        public string FileName => FileNameBox.Text?.Trim();
        public StorageFolder SelectedFolder => _selectedFolder;

        public DownloadDialog(string defaultFileName, string url, StorageFolder defaultFolder = null, long? totalBytes = null)
        {
            this.InitializeComponent();
            FileNameBox.Text = defaultFileName ?? "download";
            UrlBlock.Text = $"来源: {url}";
            _selectedFolder = defaultFolder ?? KnownFolders.PicturesLibrary;
            FolderPathBlock.Text = _selectedFolder?.Path ?? "请选择文件夹";

            // 显示文件大小（如果已知）
            if (totalBytes.HasValue && totalBytes.Value > 0)
                SizeBlock.Text = $"大小：{FormatFileSize(totalBytes.Value)}";
            else
                SizeBlock.Text = "大小：未知";

            // 跟随系统主题设置亚克力背景
            bool isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
            this.Background = new AcrylicBrush
            {
                BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                TintColor = isDark ? Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xFF, 0xE6, 0xF0, 0xFA),
                TintOpacity = 0.85,
                FallbackColor = isDark ? Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xFF, 0xE6, 0xF0, 0xFA)
            };
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }

        private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.Downloads
            };
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                _selectedFolder = folder;
                FolderPathBlock.Text = folder.Path;
            }
        }
    }
}