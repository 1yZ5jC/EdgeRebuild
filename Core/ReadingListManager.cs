using System.Collections.Generic;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace EdgeRebuild.Core
{
    /// <summary>
    /// 稍后读（旧版 Edge Reading List）：与收藏夹分离的一次性阅读队列。
    /// 读过的条目直接移除；收藏是长期记忆，这里是待办。
    /// </summary>
    public class ReadingListManager
    {
        private static readonly object _collSync = new object();   // 真多视图：集合变更跨线程串行化
        public static ReadingListManager Instance = new ReadingListManager();
        public ObservableCollection<ReadingItem> Items { get; } = new ObservableCollection<ReadingItem>();

        private ReadingListManager() { }

        public async Task LoadAsync()
        {
            var items = await Services.DatabaseService.Database.Table<ReadingItem>().OrderBy(r => r.AddedDate).ToListAsync();
            lock (_collSync)
            {
                Items.Clear();
                foreach (var item in items) Items.Add(item);
            }
        }

        public bool ContainsUrl(string url)
        {
            lock (_collSync)
                return !string.IsNullOrEmpty(url) && Items.Any(r => r.Url == url);
        }

        public async Task AddAsync(string title, string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            var item = new ReadingItem { Title = title ?? url, Url = url, AddedDate = DateTime.Now };
            List<ReadingItem> replaced = null;
            lock (_collSync)
            {
                // 已存在 → 移除旧条目后置顶（最近添加优先显示）
                replaced = Items.Where(r => r.Url == url).ToList();
                foreach (var old in replaced) Items.Remove(old);
                Items.Insert(0, item);
            }
            foreach (var old in replaced)
                await Services.DatabaseService.Database.DeleteAsync(old);
            await Services.DatabaseService.Database.InsertAsync(item);
        }

        public async Task RemoveAsync(ReadingItem item)
        {
            lock (_collSync) Items.Remove(item);
            await Services.DatabaseService.Database.DeleteAsync(item);
        }
    }
}
