using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
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
        private Action _cleanupAction;

        public int Id { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public DateTime StartTime { get; set; } = DateTime.Now;
        public CoreWebView2DownloadOperation WebViewOperation { get; set; }

        public long TotalBytesToReceive { get => _totalBytes; set { _totalBytes = value; OnPropertyChanged(nameof(TotalBytesToReceive)); Indeterminate = (value <= 0); } }
        public bool Indeterminate { get => _indeterminate; set { _indeterminate = value; OnPropertyChanged(nameof(Indeterminate)); } }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                    // 终态时自动调用清理回调
                    if ((value == "已完成" || value == "已取消" || value == "已中断") && _cleanupAction != null)
                    {
                        _cleanupAction?.Invoke();
                        _cleanupAction = null;
                    }
                }
            }
        }

        public double Progress { get => _progress; set { if (Math.Abs(_progress - value) > 0.05) { _progress = value; OnPropertyChanged(nameof(Progress)); } } }
        public bool Deleted { get => _deleted; set { _deleted = value; OnPropertyChanged(nameof(Deleted)); } }
        public bool IsCompleted { get => _isCompleted; set { _isCompleted = value; OnPropertyChanged(nameof(IsCompleted)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void SetCleanupAction(Action cleanup) => _cleanupAction = cleanup;

        public void Pause()
        {
            if (WebViewOperation == null || Status != "下载中") return;
            try { WebViewOperation.Pause(); Status = "已暂停"; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"暂停失败: {ex.Message}"); }
        }

        public void Resume()
        {
            if (WebViewOperation == null || Status != "已暂停") return;
            try { WebViewOperation.Resume(); Status = "下载中"; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"继续失败: {ex.Message}"); }
        }

        public void Cancel()
        {
            if (WebViewOperation == null) return;
            try { WebViewOperation.Cancel(); Status = "已取消"; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"取消失败: {ex.Message}"); }
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

        public static string SystemDownloadFolderPath { get; private set; }

        public static async Task<StorageFolder> GetDownloadFolderAsync()
        {
            try { return await DownloadsFolder.CreateFolderAsync("EdgeRebuild", CreationCollisionOption.OpenIfExists); }
            catch { }
            try
            {
                string downloadsPath = Windows.Storage.UserDataPaths.GetDefault().Downloads;
                var parentFolder = await StorageFolder.GetFolderFromPathAsync(downloadsPath);
                return await parentFolder.CreateFolderAsync("EdgeRebuild", CreationCollisionOption.OpenIfExists);
            }
            catch { }
            return await ApplicationData.Current.LocalFolder.CreateFolderAsync("Downloads", CreationCollisionOption.OpenIfExists);
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
        }

        public static bool CanUseSystemDownloadFolder => _checkedSystemFolder && _canUseSystemFolder;

        public static async Task LoadDownloadsAsync()
        {
            await UpdateSystemDownloadFolderAccessAsync();
            var records = await _db.Table<DownloadRecord>().ToListAsync();
            Downloads.Clear();

            foreach (var rec in records)
            {
                bool fileExists = false;
                string realPath = rec.FullPath;

                if (File.Exists(realPath))
                    fileExists = true;
                else if (!string.IsNullOrEmpty(SystemDownloadFolderPath))
                {
                    string potentialPath = Path.Combine(SystemDownloadFolderPath, rec.FileName);
                    if (File.Exists(potentialPath))
                    {
                        realPath = potentialPath;
                        fileExists = true;
                    }
                }

                var item = new DownloadItem
                {
                    Id = rec.Id,
                    Url = rec.Url,
                    FileName = rec.FileName,
                    FullPath = realPath,
                    Status = rec.Status,
                    Progress = rec.Progress,
                    Deleted = !fileExists && rec.IsCompleted,
                    IsCompleted = rec.IsCompleted
                };

                Downloads.Add(item);
            }

            foreach (var item in Downloads.Where(d => d.Id > 0))
            {
                var record = records.FirstOrDefault(r => r.Id == item.Id);
                if (record != null && record.FullPath != item.FullPath)
                {
                    record.FullPath = item.FullPath;
                    await _db.UpdateAsync(record);
                }
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

        public static DownloadItem FindCompletedByFileNameSync(string fileName, string url = null)
        {
            return Downloads.FirstOrDefault(d =>
                d.IsCompleted && !d.Deleted && File.Exists(d.FullPath) &&
                d.FileName == fileName &&
                (url == null || d.Url == url));
        }

        public static DownloadItem Add(string url, string fullPath, string fileName)
        {
            var item = new DownloadItem
            {
                Url = url,
                FullPath = fullPath,
                FileName = fileName,
                Status = "下载中",
                Progress = 0,
                StartTime = DateTime.Now,
                IsCompleted = false
            };
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