using EdgeRebuild.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EdgeRebuild.Core
{
    /// <summary>
    /// 标签页搁置（Tab Sweep）的数据层：
    /// 组与标签单独建表，绝不写入 SessionTabs，避免重启时被当成普通会话恢复。
    /// 全局抽屉，不按窗口槽位隔离；OriginSlot 仅用于诊断。
    /// </summary>
    public static class SweptTabManager
    {
        public static async Task<List<SweptTabGroup>> LoadGroupsAsync()
        {
            try
            {
                var groups = await DatabaseService.Database.Table<SweptTabGroup>()
                    .OrderByDescending(g => g.CreatedAt)
                    .ToListAsync();
                var tabs = await DatabaseService.Database.Table<SweptTab>()
                    .OrderBy(t => t.Position)
                    .ToListAsync();

                var byGroup = tabs
                    .GroupBy(t => t.GroupId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Position).ToList());

                var emptyGroups = new List<SweptTabGroup>();
                foreach (var group in groups)
                {
                    group.Tabs = byGroup.TryGetValue(group.Id, out var list)
                        ? list
                        : new List<SweptTab>();
                    if (group.Tabs.Count == 0)
                        emptyGroups.Add(group);
                }

                // 部分还原/异常退出可能留下空组，读取时顺手清理
                foreach (var group in emptyGroups)
                {
                    try { await DatabaseService.Database.DeleteAsync(group); }
                    catch { }
                }

                return groups.Where(g => g.Tabs.Count > 0).ToList();
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[标签搁置] 读取分组失败: {ex.Message}");
                return new List<SweptTabGroup>();
            }
        }

        /// <summary>
        /// 把给定标签页移入一个新建搁置组。先写库再关闭标签；
        /// 单条关闭失败时回滚该条记录，避免同一标签既在会话又在搁置组。
        /// 调用方需保证至少保留一个标签页。
        /// </summary>
        public static async Task<SweptTabGroup> SweepAsync(
            TabManager tabManager,
            IReadOnlyList<TabViewItem> tabs,
            int originSlot)
        {
            if (tabManager == null || tabs == null || tabs.Count == 0) return null;
            // InPrivate 会话不落盘；UI 已禁用入口，这里再做一层防御
            if (tabManager.IsInPrivateWindow) return null;

            var sweep = tabs
                .Where(t => t != null && tabManager.Tabs.Contains(t) && !t.IsPinned)
                .ToList();
            if (sweep.Count == 0) return null;
            if (tabManager.Tabs.Count - sweep.Count < 1) return null;

            var group = new SweptTabGroup
            {
                CreatedAt = DateTime.Now,
                OriginSlot = originSlot,
                Title = BuildGroupTitle(sweep.Count)
            };

            try
            {
                await DatabaseService.Database.InsertAsync(group);

                var records = new List<SweptTab>();
                for (int i = 0; i < sweep.Count; i++)
                {
                    var item = sweep[i];
                    records.Add(new SweptTab
                    {
                        GroupId = group.Id,
                        Url = item.Tab?.CurrentUrl ?? "",
                        Title = string.IsNullOrWhiteSpace(item.Tab?.Title)
                            ? (item.Tab?.CurrentUrl ?? "新标签页")
                            : item.Tab.Title,
                        Engine = item.Tab?.Engine == EngineType.WebView2 ? "WebView2" : "EdgeHtml",
                        Position = i,
                        IsPinned = item.IsPinned
                    });
                }
                await DatabaseService.Database.InsertAllAsync(records);
                group.Tabs = records;

                var failedIds = new HashSet<int>();
                for (int i = 0; i < sweep.Count; i++)
                {
                    try
                    {
                        await tabManager.CloseTabAsync(sweep[i], addToClosed: false);
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Log($"[标签搁置] 关闭标签失败: {ex.Message}");
                        failedIds.Add(records[i].Id);
                    }
                }

                if (failedIds.Count > 0)
                {
                    foreach (var record in records.Where(r => failedIds.Contains(r.Id)))
                    {
                        try { await DatabaseService.Database.DeleteAsync(record); }
                        catch { }
                    }
                    group.Tabs = records.Where(r => !failedIds.Contains(r.Id)).ToList();
                    if (group.Tabs.Count == 0)
                    {
                        await DatabaseService.Database.DeleteAsync(group);
                        return null;
                    }
                    group.Title = BuildGroupTitle(group.Tabs.Count);
                    await DatabaseService.Database.UpdateAsync(group);
                }

                Diagnostics.Log($"[标签搁置] 已搁置 {group.Tabs.Count} 个标签页");
                return group;
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[标签搁置] 搁置失败: {ex.Message}");
                // 写库失败时不能留下半组记录
                try
                {
                    await DeleteGroupAsync(group);
                }
                catch { }
                return null;
            }
        }

        /// <summary>整组还原，按原顺序追加到当前窗口；逐条删除记录，崩溃后不会重复还原。</summary>
        public static async Task<List<TabViewItem>> RestoreGroupAsync(
            TabManager tabManager,
            SweptTabGroup group)
        {
            var restored = new List<TabViewItem>();
            if (tabManager == null || group?.Tabs == null) return restored;

            foreach (var record in group.Tabs.OrderBy(t => t.Position).ToList())
            {
                var view = await RestoreRecordAsync(tabManager, record);
                if (view != null)
                    restored.Add(view);
            }

            await SyncGroupAfterRemovalAsync(group);
            return restored;
        }

        /// <summary>还原组内单个标签页；成功后删除该条记录，组空即删组。</summary>
        public static async Task<TabViewItem> RestoreTabAsync(
            TabManager tabManager,
            SweptTabGroup group,
            SweptTab tab)
        {
            if (tabManager == null || tab == null) return null;

            var view = await RestoreRecordAsync(tabManager, tab);
            if (view != null)
                await SyncGroupAfterRemovalAsync(group);
            return view;
        }

        private static async Task<TabViewItem> RestoreRecordAsync(TabManager tabManager, SweptTab record)
        {
            try
            {
                var engine = record.Engine == "WebView2" ? EngineType.WebView2 : EngineType.EdgeHtml;
                var view = await tabManager.CreateTabAsync(engine, record.Url, record.IsPinned);
                if (view != null)
                {
                    // 标签已创建成功，删除记录失败只记日志，不能把成功还原报成失败
                    try { await DatabaseService.Database.DeleteAsync(record); }
                    catch (Exception ex) { Diagnostics.Log($"[标签搁置] 删除已还原记录失败: {ex.Message}"); }
                }
                return view;
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[标签搁置] 还原标签失败: {ex.Message}");
                return null;
            }
        }

        public static async Task DeleteGroupAsync(SweptTabGroup group)
        {
            if (group == null) return;
            try
            {
                await DatabaseService.Database.ExecuteAsync(
                    "DELETE FROM SweptTabs WHERE GroupId = ?", group.Id);
                await DatabaseService.Database.DeleteAsync(group);
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[标签搁置] 删除分组失败: {ex.Message}");
            }
        }

        public static async Task DeleteTabAsync(SweptTabGroup group, SweptTab tab)
        {
            if (tab == null) return;
            try
            {
                await DatabaseService.Database.DeleteAsync(tab);
                await SyncGroupAfterRemovalAsync(group);
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[标签搁置] 删除标签失败: {ex.Message}");
            }
        }

        public static async Task ClearAllAsync()
        {
            try
            {
                await DatabaseService.Database.DeleteAllAsync<SweptTab>();
                await DatabaseService.Database.DeleteAllAsync<SweptTabGroup>();
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[标签搁置] 清空失败: {ex.Message}");
            }
        }

        /// <summary>单条记录被还原/删除后同步组：空组删除，非空组刷新标题中的数量。</summary>
        private static async Task SyncGroupAfterRemovalAsync(SweptTabGroup group)
        {
            if (group == null) return;
            try
            {
                int remaining = await DatabaseService.Database.Table<SweptTab>()
                    .Where(t => t.GroupId == group.Id)
                    .CountAsync();
                if (remaining == 0)
                {
                    await DatabaseService.Database.DeleteAsync(group);
                }
                else
                {
                    string title = BuildGroupTitle(remaining);
                    if (group.Title != title)
                    {
                        group.Title = title;
                        await DatabaseService.Database.UpdateAsync(group);
                    }
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[标签搁置] 清理空组失败: {ex.Message}");
            }
        }

        private static string BuildGroupTitle(int count)
            => count == 1 ? "1 个标签页" : $"{count} 个标签页";
    }
}
