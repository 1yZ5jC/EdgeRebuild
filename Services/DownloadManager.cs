using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Notifications;
using Microsoft.Web.WebView2.Core;

namespace EdgeRebuild.Services
{
    public class DownloadItem : INotifyPropertyChanged
    {
        private string _status = "下载中";
        private double _progress = 0;
        private long _totalBytes = 0;
        private bool _indeterminate = true;

        public string Url { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public DateTime StartTime { get; set; } = DateTime.Now;
        public CoreWebView2DownloadOperation WebViewOperation { get; set; }

        public long TotalBytesToReceive
        {
            get => _totalBytes;
            set
            {
                _totalBytes = value;
                OnPropertyChanged(nameof(TotalBytesToReceive));
                Indeterminate = (value <= 0);
            }
        }

        public bool Indeterminate
        {
            get => _indeterminate;
            set
            {
                _indeterminate = value;
                OnPropertyChanged(nameof(Indeterminate));
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public double Progress
        {
            get => _progress;
            set
            {
                if (Math.Abs(_progress - value) > 0.05)
                {
                    _progress = value;
                    OnPropertyChanged(nameof(Progress));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void Pause()
        {
            if (WebViewOperation != null && Status == "下载中")
            {
                WebViewOperation.Pause();
                Status = "已暂停";
            }
        }

        public void Resume()
        {
            if (WebViewOperation != null && Status == "已暂停")
            {
                WebViewOperation.Resume();
                Status = "下载中";
            }
        }

        public void Cancel()
        {
            if (WebViewOperation != null)
            {
                WebViewOperation.Cancel();
                Status = "已取消";
            }
        }

        public async Task RetryAsync()
        {
            if (Status != "已中断" && Status != "已取消" && Status != "下载失败")
                return;

            string url = Url;
            string targetPath = FullPath;
            WebViewOperation = null;

            Status = "下载中";
            Progress = 0;
            Indeterminate = true;

            try
            {
                using (var httpClient = new Windows.Web.Http.HttpClient())
                {
                    var response = await httpClient.GetAsync(new Uri(url));
                    response.EnsureSuccessStatusCode();

                    var totalSize = response.Content.Headers.ContentLength;
                    TotalBytesToReceive = totalSize.HasValue ? (long)totalSize.Value : 0;

                    using (var stream = await response.Content.ReadAsInputStreamAsync())
                    {
                        var file = await StorageFile.GetFileFromPathAsync(targetPath);
                        using (var fileStream = await file.OpenStreamForWriteAsync())
                        {
                            var buffer = new byte[8192];
                            long totalRead = 0;
                            while (true)
                            {
                                int read = await stream.AsStreamForRead().ReadAsync(buffer, 0, buffer.Length);
                                if (read == 0) break;
                                await fileStream.WriteAsync(buffer, 0, read);
                                totalRead += read;
                                if (totalSize.HasValue && totalSize.Value > 0)
                                {
                                    Progress = (double)totalRead / totalSize.Value * 100.0;
                                    DownloadManager.DownloadProgressChanged?.Invoke(this);
                                }
                            }
                        }
                    }
                }
                Status = "已完成";
                Progress = 100;
                DownloadManager.DownloadStatusChanged?.Invoke(this);
                NotificationService.ShowToast("下载完成", FileName);
            }
            catch (Exception)
            {
                Status = "下载失败";
                Progress = 0;
                DownloadManager.DownloadStatusChanged?.Invoke(this);
                NotificationService.ShowToast("下载失败", $"无法重新下载 {FileName}");
            }
        }
    }

    public static class DownloadManager
    {
        public static ObservableCollection<DownloadItem> Downloads { get; } = new ObservableCollection<DownloadItem>();

        public static Action<DownloadItem> DownloadProgressChanged;
        public static Action<DownloadItem> DownloadStatusChanged;

        public static DownloadItem Add(string url, string fullPath, string fileName)
        {
            var item = new DownloadItem
            {
                Url = url,
                FullPath = fullPath,
                FileName = fileName,
                Status = "下载中",
                Progress = 0,
                StartTime = DateTime.Now
            };
            Downloads.Insert(0, item);
            return item;
        }

        public static void ClearCompleted()
        {
            for (int i = Downloads.Count - 1; i >= 0; i--)
            {
                var item = Downloads[i];
                if (item.Status == "已完成" || item.Status == "已取消" || item.Status == "下载失败")
                    Downloads.RemoveAt(i);
            }
        }

        public static async Task<DownloadItem> StartHttpDownloadAsync(string url)
        {
            string fileName = "download";
            try
            {
                fileName = Path.GetFileName(new Uri(url).LocalPath);
                if (string.IsNullOrWhiteSpace(fileName)) fileName = "download";
            }
            catch { }

            // 尝试系统下载文件夹，若失败则回退到应用本地文件夹
            StorageFolder downloadsFolder;
            try
            {
                downloadsFolder = await DownloadsFolder.CreateFolderAsync("EdgeRebuild", CreationCollisionOption.OpenIfExists);
            }
            catch
            {
                downloadsFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("Downloads", CreationCollisionOption.OpenIfExists);
            }

            var file = await downloadsFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);

            var item = new DownloadItem
            {
                Url = url,
                FullPath = file.Path,
                FileName = file.Name,
                Status = "下载中",
                Progress = 0,
                StartTime = DateTime.Now
            };
            Downloads.Insert(0, item);

            NotificationService.ShowToast("下载开始", item.FileName);

            try
            {
                using (var httpClient = new Windows.Web.Http.HttpClient())
                {
                    var response = await httpClient.GetAsync(new Uri(url));
                    response.EnsureSuccessStatusCode();

                    var totalSize = response.Content.Headers.ContentLength;
                    item.TotalBytesToReceive = totalSize.HasValue ? (long)totalSize.Value : 0;

                    using (var stream = await response.Content.ReadAsInputStreamAsync())
                    {
                        using (var fileStream = await file.OpenStreamForWriteAsync())
                        {
                            var buffer = new byte[8192];
                            long totalRead = 0;
                            while (true)
                            {
                                int read = await stream.AsStreamForRead().ReadAsync(buffer, 0, buffer.Length);
                                if (read == 0) break;
                                await fileStream.WriteAsync(buffer, 0, read);
                                totalRead += read;
                                if (totalSize.HasValue && totalSize.Value > 0)
                                {
                                    item.Progress = (double)totalRead / totalSize.Value * 100.0;
                                    DownloadProgressChanged?.Invoke(item);
                                }
                            }
                        }
                    }
                    item.Status = "已完成";
                    item.Progress = 100;
                    DownloadStatusChanged?.Invoke(item);
                    NotificationService.ShowToast("下载完成", item.FileName);
                }
            }
            catch (Exception)
            {
                item.Status = "下载失败";
                DownloadStatusChanged?.Invoke(item);
                NotificationService.ShowToast("下载失败", $"无法下载 {item.FileName}");

                try
                {
                    if (File.Exists(file.Path))
                        File.Delete(file.Path);
                }
                catch { }
            }

            return item;
        }
    }
}