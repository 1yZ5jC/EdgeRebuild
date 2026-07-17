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

        public DownloadDialog(string defaultFileName, string url, StorageFolder defaultFolder = null)
        {
            this.InitializeComponent();
            FileNameBox.Text = defaultFileName ?? "download";
            UrlBlock.Text = $"来源: {url}";
            _selectedFolder = defaultFolder ?? KnownFolders.PicturesLibrary;
            FolderPathBlock.Text = _selectedFolder?.Path ?? "请选择文件夹";

            // 应用亚克力背景
            this.Background = new AcrylicBrush
            {
                BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                TintColor = Color.FromArgb(0xFF, 0xF0, 0xF0, 0xF0),
                TintOpacity = 0.8,
                FallbackColor = Color.FromArgb(0xFF, 0xF0, 0xF0, 0xF0)
            };
        }

        private async void BrowseFolder_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
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