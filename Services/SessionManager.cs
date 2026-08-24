using EdgeRebuild.Core;
using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

        // 多窗口会话槽位：0 = 主窗口，1..n = AppWindow 副主窗口（按创建序复用）。
        // sqlite-net CreateTableAsync 会为已存在的表自动补列（ALTER TABLE ADD COLUMN）。
        public int Slot { get; set; }
    }

    /// <summary>
    /// 会话恢复（多窗口版）：每个参与持久化的窗口持有独立槽位快照，
    /// 启动时主窗口恢复槽位 0，其余槽位由 WindowManager 重建对应副窗口。
    /// 防抖合并高频写入（导航事件等），崩溃/挂起后均有最新状态。
    /// </summary>
    public static class SessionManager
    {
        private static readonly object _lock = new object();
        private static Dictionary<int, List<SessionTabRecord>> _pendingSnapshots = new Dictionary<int, List<SessionTabRecord>>();

        // 真·多视图：各窗口在不同线程调用 ScheduleSave，DispatcherTimer 有线程亲和
        // （跨线程 Stop/Start 直接崩溃）——改用无线程亲和的 Threading.Timer，
        // 回调只触碰 DB 与纯托管状态，不涉及任何 UI 对象。
        private static System.Threading.Timer _saveTimer;
        private const int SaveDebounceMs = 800;

        /// <summary>
        /// 调度一次延迟保存（任意视图线程调用，防抖合并；slot 内全量替换）。
        /// </summary>
        public static void ScheduleSave(int slot, IReadOnlyList<TabViewItem> tabs)
        {
            lock (_lock)
            {
                _pendingSnapshots[slot] = tabs.Select((t, i) => new SessionTabRecord
                {
                    Url = t.Tab.CurrentUrl ?? "",
                    Engine = t.Tab.Engine == EngineType.WebView2 ? "WebView2" : "EdgeHtml",
                    IsPinned = t.IsPinned,
                    Position = i,
                    Slot = slot
                }).ToList();
            }

            if (_saveTimer == null)
                _saveTimer = new System.Threading.Timer(
                    async _ => { try { await FlushAsync(); } catch { } },
                    null, Timeout.Infinite, Timeout.Infinite);
            _saveTimer.Change(SaveDebounceMs, Timeout.Infinite);
        }

        /// <summary>
        /// 立即将所有槽位的待保存快照写入数据库（挂起/卸载时兜底调用）。
        /// </summary>
        public static async Task FlushAsync()
        {
            Dictionary<int, List<SessionTabRecord>> snapshot;
            lock (_lock)
            {
                if (_pendingSnapshots.Count == 0) return;
                snapshot = _pendingSnapshots;
                _pendingSnapshots = new Dictionary<int, List<SessionTabRecord>>();
            }

            _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            try
            {
                // 与既有行为一致：整表重写。各槽位记录合并写入（Slot 列区分归属）
                var all = snapshot.Values.SelectMany(v => v).ToList();
                await DatabaseService.Database.ExecuteAsync("DELETE FROM SessionTabs");
                if (all.Count > 0)
                    await DatabaseService.Database.InsertAllAsync(all);
            }
            catch { }
        }

        /// <summary>读取指定槽位的标签列表。</summary>
        public static async Task<List<SessionTabRecord>> LoadTabsAsync(int slot = 0)
        {
            try
            {
                return await DatabaseService.Database.Table<SessionTabRecord>()
                    .Where(r => r.Slot == slot)
                    .OrderBy(r => r.Position)
                    .ToListAsync();
            }
            catch { return new List<SessionTabRecord>(); }
        }

        /// <summary>读取存在持久化数据的全部非主窗口槽位号（用于启动重建副窗口）。</summary>
        public static async Task<List<int>> LoadSlotsAsync()
        {
            try
            {
                var rows = await DatabaseService.Database.Table<SessionTabRecord>().ToListAsync();
                return rows.Select(r => r.Slot).Where(s => s > 0).Distinct().OrderBy(s => s).ToList();
            }
            catch { return new List<int>(); }
        }

        /// <summary>
        /// 移除某槽位的持久化数据（副窗口关闭时调用：其标签已不存在，
        /// 若不清理会在下次启动时被恢复成幽灵窗口）。同时丢弃待保存快照。
        /// </summary>
        public static async Task RemoveSlotAsync(int slot)
        {
            lock (_lock)
            {
                _pendingSnapshots.Remove(slot);
            }
            try
            {
                await DatabaseService.Database.ExecuteAsync("DELETE FROM SessionTabs WHERE Slot = ?", slot);
            }
            catch { }
        }
    }
}
