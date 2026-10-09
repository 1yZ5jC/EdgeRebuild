using EdgeRebuild.Controls;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Services
{
    public class DownloadCoordinator
    {
        private readonly Dictionary<string, WebView2> _downloadWebViews = new();
        private readonly Panel _rootPanel;
        private bool _askBeforeDownload;

        public DownloadCoordinator(Panel rootPanel)
        {
            _rootPanel = rootPanel;
            _askBeforeDownload = false; // 将在初始化时从设置加载
        }

        public async Task InitializeAsync()
        {
            string askSetting = await SettingsManager.GetAsync("AskBeforeDownload");
            _askBeforeDownload = (askSetting == "True" || askSetting == "true");
        }

        public void UpdateAskSetting(bool ask) => _askBeforeDownload = ask;

        public async Task HandleEdgeHtmlDownloadRequested(string url)
        {
            string defaultFileName = "download";
            if (!string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    string name = Path.GetFileName(new Uri(url).AbsolutePath);
                    if (!string.IsNullOrWhiteSpace(name))
                        defaultFileName = name;
                }
                catch { }
            }

            if (_askBeforeDownload)
            {
                StorageFolder defaultFolder = null;
                try { defaultFolder = await DownloadManager.GetDownloadFolderAsync(); } catch { }

                var dialog = new DownloadDialog(defaultFileName, url, defaultFolder, null);
                dialog.XamlRoot = _rootPanel?.XamlRoot;
                var result = await dialog.ShowAsync();

                if (result != ContentDialogResult.Primary)
                    return;

                if (!DownloadDialog.TryValidateFileName(dialog.FileName, out string safeName, out string error))
                {
                    await new ContentDialog { Title = "文件名无效", Content = error, CloseButtonText = "确定", XamlRoot = _rootPanel?.XamlRoot }.ShowAsync();
                    return;
                }

                StorageFolder selectedFolder = dialog.SelectedFolder ?? defaultFolder;
                string token = Guid.NewGuid().ToString();
                StorageApplicationPermissions.FutureAccessList.AddOrReplace(token, selectedFolder);
                DownloadManager.SetCustomDownloadPath(selectedFolder.Path, token);

                await DownloadViaHiddenWebViewAsync(url, selectedFolder, safeName);
            }
            else
            {
                await DownloadViaHiddenWebViewAsync(url);
            }
        }

        private async Task DownloadViaHiddenWebViewAsync(string url, StorageFolder folder, string fileName)
        {
            string targetPath = Path.Combine(folder.Path, fileName);
            if (File.Exists(targetPath))
            {
                string ext = Path.GetExtension(fileName);
                string name = Path.GetFileNameWithoutExtension(fileName);
                int counter = 1;
                while (File.Exists(targetPath = Path.Combine(folder.Path, $"{name} ({counter++}){ext}"))) ;
            }
            await DownloadViaHiddenWebViewCoreAsync(url, targetPath);
        }

        private async Task DownloadViaHiddenWebViewAsync(string url)
        {
            StorageFolder folder = await DownloadManager.GetDownloadFolderAsync();
            string fileName = "download";
            if (!string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    string name = Path.GetFileName(new Uri(url).AbsolutePath);
                    if (!string.IsNullOrWhiteSpace(name)) fileName = name;
                }
                catch { }
            }

            string targetPath = Path.Combine(folder.Path, fileName);
            if (File.Exists(targetPath))
            {
                string ext = Path.GetExtension(fileName);
                string name = Path.GetFileNameWithoutExtension(fileName);
                int counter = 1;
                while (File.Exists(targetPath = Path.Combine(folder.Path, $"{name} ({counter++}){ext}"))) ;
            }
            await DownloadViaHiddenWebViewCoreAsync(url, targetPath);
        }

        private async Task DownloadViaHiddenWebViewCoreAsync(string url, string targetPath)
        {
            var downloadId = Guid.NewGuid().ToString();
            var wv = new WebView2 { Width = 0, Height = 0, Visibility = Visibility.Collapsed };
            _downloadWebViews[downloadId] = wv;
            _rootPanel.Children.Add(wv);

            try
            {
                await wv.EnsureCoreWebView2Async();
                wv.CoreWebView2.DownloadStarting += async (s, e) =>
                {
                    e.Handled = true;
                    e.ResultFilePath = targetPath;

                    var item = await DownloadManager.AddAsync(e.DownloadOperation.Uri, targetPath, Path.GetFileName(targetPath));
                    item.WebViewOperation = e.DownloadOperation;
                    item.TotalBytesToReceive = e.DownloadOperation.TotalBytesToReceive;
                    item.SetCleanupAction(() => CleanupDownloadWebView(downloadId));
                    DownloadManager.StartDownloadOperation(item);
                };
                wv.CoreWebView2.Navigate(url);
            }
            catch
            {
                CleanupDownloadWebView(downloadId);
            }
        }

        private void CleanupDownloadWebView(string id)
        {
            if (_downloadWebViews.TryGetValue(id, out var wv))
            {
                _downloadWebViews.Remove(id);
                try { (wv.Parent as Panel)?.Children.Remove(wv); wv.Close(); } catch { }
            }
        }

        public async Task RetryDownload(DownloadItem item)
        {
            try
            {
                if (File.Exists(item.FullPath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                    await file.DeleteAsync();
                }
            }
            catch { }
            await DownloadViaHiddenWebViewAsync(item.Url);
        }

        public void CleanupAll()
        {
            foreach (var kvp in _downloadWebViews.ToList())
            {
                try { (kvp.Value.Parent as Panel)?.Children.Remove(kvp.Value); kvp.Value.Close(); } catch { }
            }
            _downloadWebViews.Clear();
        }
    }
}