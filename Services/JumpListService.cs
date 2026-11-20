using EdgeRebuild.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.StartScreen;

namespace EdgeRebuild.Services
{
    /// <summary>
    /// 任务栏/开始菜单跳转菜单：
    /// - 固定任务：新建 InPrivate 窗口、新建窗口
    /// - 最近关闭的标签页：跨进程保留，点击后冷启动到对应 URL
    ///
    /// 最近关闭项只保存到 LocalSettings，跳转项参数里只放短 ID，
    /// 避免把长 URL 直接塞进系统参数。TabManager 记录关闭标签时通过
    /// 静态事件通知本服务；清除关闭标签时同步清空跳转菜单。
    /// </summary>
    public static class JumpListService
    {
        public const string ArgumentPrefix = "edgejump:";
        public const string InPrivateArgument = ArgumentPrefix + "inprivate";
        public const string NewWindowArgument = ArgumentPrefix + "new-window";
        public const string RecentPrefix = ArgumentPrefix + "reopen|";

        private const string EntriesKey = "JumpListRecentClosed";
        private const string RemovedKey = "JumpListRemovedRecentIds";
        private const int MaxEntries = 8;
        private const int MaxTitleLength = 80;
        private const string RecentGroupName = "最近关闭的标签页";

        // 跳转菜单项图标：44×44 应用 Logo；定宽路径由系统按 DPI 选 scale 变体。
        private static readonly Uri ItemLogo = new Uri("ms-appx:///Assets/Square44x44Logo.png");

        private sealed class RecentEntry
        {
            public string Id = "";
            public string Url = "";
            public string Title = "";
            public EngineType Engine;
        }

        private static readonly object _entryGate = new object();
        private static readonly SemaphoreSlim _rebuildGate = new SemaphoreSlim(1, 1);
        private static bool _initialized;

        /// <summary>订阅标签关闭事件；每个进程只调用一次。</summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            TabManager.ClosedTabRecorded += OnClosedTabRecorded;
            TabManager.ClosedTabsCleared += OnClosedTabsCleared;
        }

        /// <summary>启动时调用：订阅事件并刷新跳转菜单。</summary>
        public static async Task InitializeAsync()
        {
            Initialize();
            await RebuildAsync();
        }

        /// <summary>根据短 ID 取回关闭标签的 URL；找不到返回 null。</summary>
        public static string? ResolveClosedTab(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            foreach (var entry in LoadEntries())
            {
                if (entry.Id == id) return entry.Url;
            }
            return null;
        }

        private static void OnClosedTabRecorded(string url, string title, EngineType engine)
        {
            if (string.IsNullOrWhiteSpace(url) || url == "about:blank") return;

            lock (_entryGate)
            {
                var entries = LoadEntries();
                entries.RemoveAll(e => e.Url == url);
                entries.Insert(0, new RecentEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Url = url,
                    Title = ClampTitle(title, url),
                    Engine = engine
                });
                while (entries.Count > MaxEntries)
                    entries.RemoveAt(entries.Count - 1);
                SaveEntries(entries);
            }

            _ = RebuildAsync();
        }

        private static void OnClosedTabsCleared()
        {
            lock (_entryGate)
            {
                SaveEntries(new List<RecentEntry>());
                ApplicationData.Current.LocalSettings.Values.Remove(RemovedKey);
            }
            _ = RebuildAsync();
        }

        /// <summary>重建跳转菜单；系统不支持时静默返回。</summary>
        public static async Task RebuildAsync()
        {
            await _rebuildGate.WaitAsync();
            try
            {
                if (!JumpList.IsSupported()) return;

                var jumpList = await JumpList.LoadCurrentAsync();
                jumpList.SystemGroupKind = JumpListSystemGroupKind.None;

                // 用户在系统菜单里点过“从此列表中删除”的项不要重新加回去
                var removed = LoadRemovedIds();
                foreach (var item in jumpList.Items)
                {
                    if (item.RemovedByUser &&
                        item.Arguments != null &&
                        item.Arguments.StartsWith(RecentPrefix, StringComparison.Ordinal))
                    {
                        removed.Add(item.Arguments.Substring(RecentPrefix.Length));
                    }
                }
                SaveRemovedIds(removed);

                jumpList.Items.Clear();

                // 不设置 GroupName 的任务项会进入系统自带的“任务”分组（位于菜单底部）
                var inPrivate = JumpListItem.CreateWithArguments(
                    InPrivateArgument, "新建 InPrivate 窗口");
                inPrivate.Description = "打开一个不记录历史与会话的窗口";
                inPrivate.Logo = ItemLogo;
                jumpList.Items.Add(inPrivate);

                var newWindow = JumpListItem.CreateWithArguments(
                    NewWindowArgument, "新建窗口");
                newWindow.Description = "打开一个新的浏览器窗口";
                newWindow.Logo = ItemLogo;
                jumpList.Items.Add(newWindow);

                foreach (var entry in LoadEntries())
                {
                    if (removed.Contains(entry.Id)) continue;

                    var item = JumpListItem.CreateWithArguments(
                        RecentPrefix + entry.Id, entry.Title);
                    item.Description = entry.Url;
                    item.GroupName = RecentGroupName;
                    item.Logo = ItemLogo;
                    jumpList.Items.Add(item);
                }

                await jumpList.SaveAsync();
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[JumpList] 重建失败: {ex.Message}");
            }
            finally
            {
                _rebuildGate.Release();
            }
        }

        private static string ClampTitle(string title, string url)
        {
            if (string.IsNullOrWhiteSpace(title)) title = url;
            title = title.Trim();
            if (title.Length > MaxTitleLength)
                title = title.Substring(0, MaxTitleLength - 1) + "…";
            return title;
        }

        private static List<RecentEntry> LoadEntries()
        {
            var entries = new List<RecentEntry>();
            string? raw = null;
            try { raw = ApplicationData.Current.LocalSettings.Values[EntriesKey] as string; }
            catch { }
            if (string.IsNullOrEmpty(raw)) return entries;

            foreach (var line in raw!.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('|');
                if (parts.Length != 4) continue;
                try
                {
                    int engineValue = int.TryParse(parts[1], out int parsed) ? parsed : 0;
                    entries.Add(new RecentEntry
                    {
                        Id = parts[0],
                        Engine = (EngineType)engineValue,
                        Url = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])),
                        Title = Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]))
                    });
                }
                catch { }
            }
            return entries;
        }

        private static void SaveEntries(List<RecentEntry> entries)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.Id) || string.IsNullOrEmpty(entry.Url)) continue;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(entry.Id).Append('|')
                      .Append((int)entry.Engine).Append('|')
                      .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Url))).Append('|')
                      .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Title ?? "")));
                }
                ApplicationData.Current.LocalSettings.Values[EntriesKey] = sb.ToString();
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[JumpList] 保存最近关闭标签失败: {ex.Message}");
            }
        }

        private static HashSet<string> LoadRemovedIds()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (ApplicationData.Current.LocalSettings.Values[RemovedKey] is string raw)
                {
                    foreach (var id in raw.Split(','))
                    {
                        if (!string.IsNullOrWhiteSpace(id)) ids.Add(id.Trim());
                    }
                }
            }
            catch { }
            return ids;
        }

        private static void SaveRemovedIds(HashSet<string> ids)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[RemovedKey] = string.Join(",", ids);
            }
            catch { }
        }
    }
}
