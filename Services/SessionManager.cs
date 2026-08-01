using EdgeRebuild.Core;
using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace EdgeRebuild.Services
{
    [Table("SessionTabs")]
    public class SessionTabRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string Url { get; set; }
        public string Engine { get; set; }
        public bool IsPinned { get; set; }
        public int Position { get; set; }
    }

    /// <summary>
    /// 会话恢复：实时快照当前标签列表，启动时按设置恢复。
    /// 防抖合并高频写入（导航事件等），崩溃/挂起后均有最新状态。
    /// </summary>
    public static class SessionManager
    {
        private static readonly object _lock = new object();
        private static List<SessionTabRecord> _pendingSnapshot;
        private static DispatcherTimer _saveTimer;
        private const int SaveDebounceMs = 800;

        /// <summary>
        /// 调度一次延迟保存（UI 线程调用，防抖合并）。
        /// </summary>
        public static void ScheduleSave(IReadOnlyList<TabViewItem> tabs)
        {
            lock (_lock)
            {
                _pendingSnapshot = tabs.Select((t, i) => new SessionTabRecord
                {
                    Url = t.Tab.CurrentUrl ?? "",
                    Engine = t.Tab.Engine == EngineType.WebView2 ? "WebView2" : "EdgeHtml",
                    IsPinned = t.IsPinned,
                    Position = i
                }).ToList();
            }

            if (_saveTimer == null)
            {
                _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SaveDebounceMs) };
                _saveTimer.Tick += async (s, e) =>
                {
                    _saveTimer.Stop();
                    await FlushAsync();
                };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>
        /// 立即将待保存快照写入数据库（挂起/卸载时兜底调用）。
        /// </summary>
        public static async Task FlushAsync()
        {
            List<SessionTabRecord> snapshot;
            lock (_lock)
            {
                if (_pendingSnapshot == null) return;
                snapshot = _pendingSnapshot;
                _pendingSnapshot = null;
            }

            _saveTimer?.Stop();

            try
            {
                await DatabaseService.Database.ExecuteAsync("DELETE FROM SessionTabs");
                if (snapshot.Count > 0)
                    await DatabaseService.Database.InsertAllAsync(snapshot);
            }
            catch { }
        }

        public static async Task<List<SessionTabRecord>> LoadTabsAsync()
        {
            try
            {
                return await DatabaseService.Database.Table<SessionTabRecord>()
                    .OrderBy(r => r.Position)
                    .ToListAsync();
            }
            catch { return new List<SessionTabRecord>(); }
        }

        public static async Task ClearAsync()
        {
            try { await DatabaseService.Database.ExecuteAsync("DELETE FROM SessionTabs"); }
            catch { }
        }
    }
}
