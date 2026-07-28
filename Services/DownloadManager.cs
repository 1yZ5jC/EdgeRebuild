using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Microsoft.Web.WebView2.Core;
using SQLite;

namespace EdgeRebuild.Services
{
    public enum DownloadStatus
    {
        Pending,
        Downloading,
        Paused,
        Completed,
        Cancelled,
        Interrupted,
        Failed,
        FileMissing
    }

    [Table("DownloadRecords")]
    public class DownloadRecord
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public string StatusString { get; set; }
        public double Progress { get; set; }
        public bool Deleted { get; set; }
        public bool IsCompleted { get; set; }
    }

    public class DownloadItem : INotifyPropertyChanged
    {
        private DownloadStatus _status = DownloadStatus.Downloading;
        private double _progress = 0;
        private long _totalBytes = 0;
        private bool _indeterminate = true;
        private bool _deleted = false;
        private bool _isCompleted = false;
        private Action _cleanupAction;
        private TaskCompletionSource<bool> _terminalTcs;

        public int Id { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public DateTime StartTime { get; set; } = DateTime.Now;
        public CoreWebView2DownloadOperation WebViewOperation { get; set; }

        public long TotalBytesToReceive { get => _totalBytes; set { _totalBytes = value; OnPropertyChanged(nameof(TotalBytesToReceive)); Indeterminate = (value <= 0); } }
        public bool Indeterminate { get => _indeterminate; set { _indeterminate = value; OnPropertyChanged(nameof(Indeterminate)); } }
        public DownloadStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(StatusText));
                    if (IsTerminalState(value))
                    {
                        _terminalTcs?.TrySetResult(true);
                        _cleanupAction?.Invoke();
                        _cleanupAction = null;
                    }
                }
            }
        }
        public string StatusText => Status switch
        {
            DownloadStatus.Pending => "准备中",
            DownloadStatus.Downloading => "下载中",
            DownloadStatus.Paused => "已暂停",
            DownloadStatus.Completed => "已完成",
            DownloadStatus.Cancelled => "已取消",
            DownloadStatus.Interrupted => "已中断",
            DownloadStatus.Failed => "下载失败",
            DownloadStatus.FileMissing => "文件已丢失",
            _ => "未知"
        };
        public double Progress { get => _progress; set { if (Math.Abs(_progress - value) > 0.05) { _progress = value; OnPropertyChanged(nameof(Progress)); } } }
        public bool Deleted { get => _deleted; set { _deleted = value; OnPropertyChanged(nameof(Deleted)); } }
        public bool IsCompleted { get => _isCompleted; set { _isCompleted = value; OnPropertyChanged(nameof(IsCompleted)); } }
        public bool IsActive => Status == DownloadStatus.Downloading || Status == DownloadStatus.Paused || Status == DownloadStatus.Pending;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void SetCleanupAction(Action cleanup) => _cleanupAction = cleanup;

        public Task WaitForTerminalStateAsync()
        {
            if (IsTerminalState(Status)) return Task.CompletedTask;
            _terminalTcs = _terminalTcs ?? new TaskCompletionSource<bool>();
            return _terminalTcs.Task;
        }

        private static bool IsTerminalState(DownloadStatus s) =>
            s == DownloadStatus.Completed || s == DownloadStatus.Cancelled || s == DownloadStatus.Failed || s == DownloadStatus.Interrupted;

        public void Pause()
        {
            if (WebViewOperation == null || Status != DownloadStatus.Downloading) return;
            try { WebViewOperation.Pause(); Status = DownloadStatus.Paused; } catch { }
        }

        public void Resume()
        {
            if (WebViewOperation == null || Status != DownloadStatus.Paused) return;
            try { WebViewOperation.Resume(); Status = DownloadStatus.Downloading; } catch { }
        }

        public void Cancel()
        {
            if (WebViewOperation == null) return;
            try { WebViewOperation.Cancel(); Status = DownloadStatus.Cancelled; } catch { }
        }

        public void Retry() => DownloadManager.OnRetryRequested(this);
    }

    public static class DownloadManager
    {
        public static ObservableCollection<DownloadItem> Downloads { get; } = new ObservableCollection<DownloadItem>();
        public static event Action<DownloadItem> DownloadProgressChanged;
        public static event Action<DownloadItem> DownloadStatusChanged;
        public static event Action<DownloadItem> RetryDownloadRequested;

        private static bool _canUseSystemFolder = false;
        private static bool _checkedSystemFolder = false;
        private static readonly SQLiteAsyncConnection _db = DatabaseService.Database;
        private static string _customDownloadPath;
        private static string _downloadFolderToken;

        public static string SystemDownloadFolderPath { get; private set; }

        public static void SetCustomDownloadPath(string path, string token = null)
        {
            _customDownloadPath = path;
            _downloadFolderToken = token;
        }

        public static async Task<StorageFolder> GetDownloadFolderAsync()
        {
            if (!string.IsNullOrEmpty(_downloadFolderToken))
            {
                try { return await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(_downloadFolderToken); } catch { }
            }
            if (!string.IsNullOrEmpty(_customDownloadPath))
            {
                try { return await StorageFolder.GetFolderFromPathAsync(_customDownloadPath); } catch { }
            }
            try { return await DownloadsFolder.CreateFolderAsync("EdgeRebuild", CreationCollisionOption.OpenIfExists); } catch { }
            return await ApplicationData.Current.LocalFolder.CreateFolderAsync("Downloads", CreationCollisionOption.OpenIfExists);
        }

        public static async Task UpdateSystemDownloadFolderAccessAsync()
        {
            try
            {
                var folder = await GetDownloadFolderAsync();
                SystemDownloadFolderPath = folder.Path;
                _canUseSystemFolder = !folder.Path.StartsWith(ApplicationData.Current.LocalFolder.Path, StringComparison.OrdinalIgnoreCase);
                if (_canUseSystemFolder) { var test = await folder.CreateFileAsync(".perm_test", CreationCollisionOption.ReplaceExisting); await test.DeleteAsync(); }
            }
            catch { _canUseSystemFolder = false; SystemDownloadFolderPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Downloads"); }
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
                bool fileExists = File.Exists(rec.FullPath);
                if (Enum.TryParse<DownloadStatus>(rec.StatusString, out var status))
                {
                    if (status == DownloadStatus.Completed && !fileExists) status = DownloadStatus.FileMissing;
                    Downloads.Add(new DownloadItem
                    {
                        Id = rec.Id,
                        Url = rec.Url,
                        FileName = rec.FileName,
                        FullPath = rec.FullPath,
                        Status = status,
                        Progress = rec.Progress,
                        Deleted = rec.Deleted,
                        IsCompleted = rec.IsCompleted
                    });
                }
                else
                {
                    Downloads.Add(new DownloadItem
                    {
                        Id = rec.Id,
                        Url = rec.Url,
                        FileName = rec.FileName,
                        FullPath = rec.FullPath,
                        Status = DownloadStatus.Completed,
                        Progress = 100,
                        Deleted = !fileExists,
                        IsCompleted = true
                    });
                }
            }
        }

        public static async Task<DownloadItem> AddAsync(string url, string fullPath, string fileName)
        {
            var item = new DownloadItem
            {
                Url = url,
                FullPath = fullPath,
                FileName = fileName,
                Status = DownloadStatus.Downloading,
                Progress = 0,
                StartTime = DateTime.Now,
                IsCompleted = false
            };
            await SaveDownloadAsync(item);
            Downloads.Insert(0, item);
            return item;
        }

        // 在 UI 线程显示 Toast 的专用方法，避免 IAsyncAction 类型转换问题
        private static async void ShowToastOnUIThread(string fileName)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                () =>
                {
                    try { NotificationService.ShowToast("下载完成", fileName); } catch { }
                }
            );
        }

        public static void StartDownloadOperation(DownloadItem item)
        {
            var op = item.WebViewOperation;
            if (op == null) return;
            op.BytesReceivedChanged += (_, _) =>
            {
                if (op.TotalBytesToReceive > 0)
                {
                    item.TotalBytesToReceive = op.TotalBytesToReceive;
                    item.Progress = Math.Min((double)op.BytesReceived / op.TotalBytesToReceive * 100, 100);
                }
                else item.Indeterminate = true;
                DownloadProgressChanged?.Invoke(item);
            };
            op.StateChanged += async (_, s) =>
            {
                if (op.State == CoreWebView2DownloadState.Completed)
                {
                    item.Status = DownloadStatus.Completed;
                    item.Progress = 100;
                    item.IsCompleted = true;
                    await SaveDownloadAsync(item);
                    DownloadStatusChanged?.Invoke(item);
                    ShowToastOnUIThread(item.FileName);

                    string autoClean = await SettingsManager.GetAsync("AutoCleanDownload");
                    if (autoClean == "True" || autoClean == "true")
                    {
                        if (item.Id > 0) await _db.DeleteAsync<DownloadRecord>(item.Id);
                        Downloads.Remove(item);
                    }
                }
                else if (op.State == CoreWebView2DownloadState.Interrupted)
                {
                    item.Status = DownloadStatus.Interrupted;
                    await SaveDownloadAsync(item);
                    DownloadStatusChanged?.Invoke(item);
                }
            };
        }

        public static async Task DeleteDownloadAsync(DownloadItem item, bool cancelIfActive = true)
        {
            if (item.IsActive)
            {
                if (cancelIfActive) { item.Cancel(); await item.WaitForTerminalStateAsync(); }
                else throw new InvalidOperationException("Cannot delete active download without cancelling.");
            }
            if (item.Id > 0) await _db.DeleteAsync<DownloadRecord>(item.Id);
            Downloads.Remove(item);
        }

        public static Task DeleteDownloadAsync(DownloadItem item) => DeleteDownloadAsync(item, true);

        public static DownloadItem FindCompletedByFileNameSync(string fileName, string url = null) =>
            Downloads.FirstOrDefault(d => d.Status == DownloadStatus.Completed && !d.Deleted && File.Exists(d.FullPath) && d.FileName == fileName && (url == null || d.Url == url));

        private static async Task SaveDownloadAsync(DownloadItem item)
        {
            var rec = new DownloadRecord
            {
                Id = item.Id,
                Url = item.Url,
                FileName = item.FileName,
                FullPath = item.FullPath,
                StatusString = item.Status.ToString(),
                Progress = item.Progress,
                Deleted = item.Deleted,
                IsCompleted = item.IsCompleted
            };
            if (item.Id == 0) { await _db.InsertAsync(rec); item.Id = rec.Id; }
            else await _db.UpdateAsync(rec);
        }

        public static async Task SaveAllDownloadsAsync()
        {
            foreach (var item in Downloads) await SaveDownloadAsync(item);
        }

        public static void OnRetryRequested(DownloadItem item) => RetryDownloadRequested?.Invoke(item);

        public static void ClearCompleted()
        {
            for (int i = Downloads.Count - 1; i >= 0; i--)
            {
                var item = Downloads[i];
                if (item.Status == DownloadStatus.Completed || item.Status == DownloadStatus.Cancelled || item.Status == DownloadStatus.Failed)
                {
                    Downloads.RemoveAt(i);
                    if (item.Id > 0) _ = _db.DeleteAsync<DownloadRecord>(item.Id);
                }
            }
        }
    }
}