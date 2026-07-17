using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Notifications;
using Microsoft.Web.WebView2.Core;
using SQLite;

namespace EdgeRebuild.Services
{
    [Table("DownloadRecords")]
    public class DownloadRecord
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public string Status { get; set; }
        public double Progress { get; set; }
        public bool Deleted { get; set; }
        public bool IsCompleted { get; set; }
    }

    public class DownloadItem : INotifyPropertyChanged
    {
        private string _status = "下载中";
        private double _progress = 0;
        private long _totalBytes = 0;
        private bool _indeterminate = true;
        private bool _deleted = false;
        private bool _isCompleted = false;

        public int Id { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public DateTime StartTime { get; set; } = DateTime.Now;
        public CoreWebView2DownloadOperation WebViewOperation { get; set; }

        public long TotalBytesToReceive { get => _totalBytes; set { _totalBytes = value; OnPropertyChanged(nameof(TotalBytesToReceive)); Indeterminate = (value <= 0); } }
        public bool Indeterminate { get => _indeterminate; set { _indeterminate = value; OnPropertyChanged(nameof(Indeterminate)); } }
        public string Status { get => _status; set { if (_status != value) { _status = value; OnPropertyChanged(nameof(Status)); } } }
        public double Progress { get => _progress; set { if (Math.Abs(_progress - value) > 0.05) { _progress = value; OnPropertyChanged(nameof(Progress)); } } }
        public bool Deleted { get => _deleted; set { _deleted = value; OnPropertyChanged(nameof(Deleted)); } }
        public bool IsCompleted { get => _isCompleted; set { _isCompleted = value; OnPropertyChanged(nameof(IsCompleted)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void Pause() { if (WebViewOperation != null && Status == "下载中") { WebViewOperation.Pause(); Status = "已暂停"; } }
        public void Resume() { if (WebViewOperation != null && Status == "已暂停") { WebViewOperation.Resume(); Status = "下载中"; } }
        public void Cancel() { if (WebViewOperation != null) { WebViewOperation.Cancel(); Status = "已取消"; } }

        public async Task RetryAsync()
        {
            if (Status != "已中断" && Status != "已取消" && Status != "下载失败") return;
            string url = Url;
            string targetPath = FullPath;
            WebViewOperation = null;
            Status = "下载中"; Progress = 0; Indeterminate = true; IsCompleted = false;
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
                            var buffer = new byte[8192]; long totalRead = 0;
                            while (true)
                            {
                                int read = await stream.AsStreamForRead().ReadAsync(buffer, 0, buffer.Length);
                                if (read == 0) break;
                                await fileStream.WriteAsync(buffer, 0, read);
                                totalRead += read;
                                if (totalSize.HasValue && totalSize.Value > 0) { Progress = (double)totalRead / totalSize.Value * 100.0; DownloadManager.DownloadProgressChanged?.Invoke(this); }
                            }
                        }
                    }
                    Status = "已完成"; Progress = 100; IsCompleted = true;
                    DownloadManager.DownloadStatusChanged?.Invoke(this);
                    NotificationService.ShowToast("下载完成", FileName);
                }
            }
            catch (Exception) { Status = "下载失败"; Progress = 0; DownloadManager.DownloadStatusChanged?.Invoke(this); NotificationService.ShowToast("下载失败", $"无法重新下载 {FileName}"); }
        }
    }

    public static class DownloadManager
    {
        public static ObservableCollection<DownloadItem> Downloads { get; } = new ObservableCollection<DownloadItem>();
        public static Action<DownloadItem> DownloadProgressChanged;
        public static Action<DownloadItem> DownloadStatusChanged;
        private static bool _canUseSystemFolder = false;
        private static bool _checkedSystemFolder = false;
        private static readonly SQLiteAsyncConnection _db = DatabaseService.Database;

        // 缓存的系统下载文件夹路径（EdgeRebuild 子文件夹）
        public static string SystemDownloadFolderPath { get; private set; }

        public static async Task<StorageFolder> GetDownloadFolderAsync()
        {
            try
            {
                Debug.WriteLine("[DownloadManager] Trying DownloadsFolder.CreateFolderAsync...");
                var folder = await DownloadsFolder.CreateFolderAsync("EdgeRebuild", CreationCollisionOption.OpenIfExists);
                Debug.WriteLine($"[DownloadManager] Success: {folder.Path}");
                return folder;
            }
            catch (Exception ex) { Debug.WriteLine($"[DownloadManager] DownloadsFolder failed: {ex.Message}"); }

            try
            {
                string downloadsPath = Windows.Storage.UserDataPaths.GetDefault().Downloads;
                Debug.WriteLine($"[DownloadManager] Trying UserDataPaths: {downloadsPath}");
                var parentFolder = await StorageFolder.GetFolderFromPathAsync(downloadsPath);
                var folder = await parentFolder.CreateFolderAsync("EdgeRebuild", CreationCollisionOption.OpenIfExists);
                Debug.WriteLine($"[DownloadManager] Success: {folder.Path}");
                return folder;
            }
            catch (Exception ex) { Debug.WriteLine($"[DownloadManager] UserDataPaths failed: {ex.Message}"); }

            Debug.WriteLine("[DownloadManager] Falling back to local folder.");
            var localFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("Downloads", CreationCollisionOption.OpenIfExists);
            Debug.WriteLine($"[DownloadManager] Local folder: {localFolder.Path}");
            return localFolder;
        }

        public static async Task UpdateSystemDownloadFolderAccessAsync()
        {
            try
            {
                var folder = await GetDownloadFolderAsync();
                SystemDownloadFolderPath = folder.Path;
                if (folder.Path.StartsWith(ApplicationData.Current.LocalFolder.Path, StringComparison.OrdinalIgnoreCase))
                    _canUseSystemFolder = false;
                else
                {
                    var testFile = await folder.CreateFileAsync(".perm_test", CreationCollisionOption.ReplaceExisting);
                    await testFile.DeleteAsync();
                    _canUseSystemFolder = true;
                }
            }
            catch
            {
                _canUseSystemFolder = false;
                SystemDownloadFolderPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Downloads");
            }
            _checkedSystemFolder = true;
            Debug.WriteLine($"[DownloadManager] CanUseSystemDownloadFolder: {_canUseSystemFolder}, Path: {SystemDownloadFolderPath}");
        }

        public static bool CanUseSystemDownloadFolder => _checkedSystemFolder && _canUseSystemFolder;

        public static async Task LoadDownloadsAsync()
        {
            var records = await _db.Table<DownloadRecord>().ToListAsync();
            Downloads.Clear();
            foreach (var rec in records)
            {
                var item = new DownloadItem
                {
                    Id = rec.Id,
                    Url = rec.Url,
                    FileName = rec.FileName,
                    FullPath = rec.FullPath,
                    Status = rec.Status,
                    Progress = rec.Progress,
                    Deleted = rec.Deleted,
                    IsCompleted = rec.IsCompleted
                };
                if (File.Exists(rec.FullPath))
                    item.Deleted = false;
                else if (rec.IsCompleted)
                {
                    item.Deleted = true;
                    item.Status = "已完成";
                    item.IsCompleted = true;
                }
                Downloads.Add(item);
            }
        }

        public static async Task SaveDownloadAsync(DownloadItem item)
        {
            var record = new DownloadRecord
            {
                Id = item.Id,
                Url = item.Url,
                FileName = item.FileName,
                FullPath = item.FullPath,
                Status = item.Status,
                Progress = item.Progress,
                Deleted = item.Deleted,
                IsCompleted = item.IsCompleted
            };
            await _db.InsertOrReplaceAsync(record);
            item.Id = record.Id;
        }

        public static async Task DeleteDownloadAsync(DownloadItem item)
        {
            if (item.Id > 0) await _db.DeleteAsync<DownloadRecord>(item.Id);
            Downloads.Remove(item);
        }

        public static DownloadItem FindCompletedByFileName(string fileName)
        {
            return Downloads.FirstOrDefault(d =>
                d.IsCompleted && !d.Deleted && File.Exists(d.FullPath) && d.FileName == fileName);
        }

        public static DownloadItem FindCompletedByFileNameSync(string fileName)
        {
            return Downloads.FirstOrDefault(d =>
                d.IsCompleted && !d.Deleted && File.Exists(d.FullPath) &&
                (d.FileName == fileName || d.FullPath.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)));
        }

        public static async Task<DownloadItem> EnqueueDownloadAsync(string url, string suggestedFileName = null)
        {
            string fileName = suggestedFileName ?? Path.GetFileName(new Uri(url).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "download";

            var existing = FindCompletedByFileName(fileName);
            if (existing != null)
            {
                Debug.WriteLine($"[DownloadManager] Found completed download: {existing.FullPath}");
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(existing.FullPath);
                    await Windows.System.Launcher.LaunchFileAsync(file);
                }
                catch { }
                return existing;
            }

            return await StartHttpDownloadAsync(url, fileName);
        }

        private static async Task<DownloadItem> StartHttpDownloadAsync(string url, string fileName)
        {
            var downloadsFolder = await GetDownloadFolderAsync();
            var file = await downloadsFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
            Debug.WriteLine($"[DownloadManager] New download file: {file.Path}");

            var item = new DownloadItem
            {
                Url = url,
                FullPath = file.Path,
                FileName = file.Name,
                Status = "下载中",
                Progress = 0,
                StartTime = DateTime.Now,
                IsCompleted = false
            };
            Downloads.Insert(0, item);
            await SaveDownloadAsync(item);
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
                    using (var fileStream = await file.OpenStreamForWriteAsync())
                    {
                        var buffer = new byte[8192]; long totalRead = 0;
                        while (true)
                        {
                            int read = await stream.AsStreamForRead().ReadAsync(buffer, 0, buffer.Length);
                            if (read == 0) break;
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;
                            if (totalSize.HasValue && totalSize.Value > 0) { item.Progress = (double)totalRead / totalSize.Value * 100.0; DownloadProgressChanged?.Invoke(item); }
                        }
                    }
                    item.Status = "已完成"; item.Progress = 100; item.IsCompleted = true;
                    await SaveDownloadAsync(item);
                    DownloadStatusChanged?.Invoke(item);
                    NotificationService.ShowToast("下载完成", item.FileName);
                }
            }
            catch (Exception)
            {
                item.Status = "下载失败"; item.IsCompleted = false;
                await SaveDownloadAsync(item);
                DownloadStatusChanged?.Invoke(item);
                NotificationService.ShowToast("下载失败", $"无法下载 {item.FileName}");
                try { if (File.Exists(file.Path)) File.Delete(file.Path); } catch { }
            }
            return item;
        }

        public static DownloadItem Add(string url, string fullPath, string fileName)
        {
            var item = new DownloadItem { Url = url, FullPath = fullPath, FileName = fileName, Status = "下载中", Progress = 0, StartTime = DateTime.Now, IsCompleted = false };
            Downloads.Insert(0, item);
            _ = SaveDownloadAsync(item);
            return item;
        }

        public static void ClearCompleted()
        {
            for (int i = Downloads.Count - 1; i >= 0; i--)
            {
                var item = Downloads[i];
                if (item.Status == "已完成" || item.Status == "已取消" || item.Status == "下载失败")
                {
                    Downloads.RemoveAt(i);
                    if (item.Id > 0) _ = _db.DeleteAsync<DownloadRecord>(item.Id);
                }
            }
        }
    }
} 