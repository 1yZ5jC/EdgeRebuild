using System;
using System.IO;
using System.Linq;
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

        public static bool TryValidateFileName(string input, out string safeName, out string error)
        {
            safeName = null;
            error = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                error = "文件名不能为空。";
                return false;
            }

            string trimmed = input.Trim();
            safeName = Path.GetFileName(trimmed);
            if (!string.Equals(trimmed, safeName, StringComparison.Ordinal))
            {
                error = "文件名不能包含路径。";
                return false;
            }

            if (safeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = "文件名包含非法字符。";
                return false;
            }

            string nameWithoutExt = Path.GetFileNameWithoutExtension(safeName);
            string[] reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (reserved.Contains(nameWithoutExt, StringComparer.OrdinalIgnoreCase))
            {
                error = "该文件名是 Windows 保留名称。";
                return false;
            }

            return true;
        }

        public DownloadDialog(string defaultFileName, string url, StorageFolder defaultFolder = null, long? totalBytes = null)
        {
            this.InitializeComponent();
            FileNameBox.Text = defaultFileName ?? "download";
            UrlBlock.Text = $"来源: {url}";
            _selectedFolder = defaultFolder ?? KnownFolders.PicturesLibrary;
            FolderPathBlock.Text = _selectedFolder?.Path ?? "请选择文件夹";

            if (totalBytes.HasValue && totalBytes.Value > 0)
                SizeBlock.Text = $"大小：{FormatFileSize(totalBytes.Value)}";
            else
                SizeBlock.Text = "大小：未知";

            bool isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
            this.Background = new AcrylicBrush
            {
                BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                TintColor = isDark ? Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xFF, 0xE6, 0xF0, 0xFA),
                TintOpacity = 0.85,
                FallbackColor = isDark ? Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xFF, 0xE6, 0xF0, 0xFA)
            };

            var buttonStyle = new Style(typeof(Button));
            buttonStyle.Setters.Add(new Setter(Button.CornerRadiusProperty, new CornerRadius(0)));
            this.PrimaryButtonStyle = buttonStyle;
            this.SecondaryButtonStyle = buttonStyle;

            this.PrimaryButtonClick += OnPrimaryButtonClick;
        }

        private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (!TryValidateFileName(FileNameBox.Text, out _, out string error))
            {
                args.Cancel = true;
                _ = new ContentDialog
                {
                    Title = "文件名无效",
                    Content = error,
                    CloseButtonText = "确定"
                }.ShowAsync();
            }
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