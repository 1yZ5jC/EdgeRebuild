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
        public static ReadingListManager Instance = new ReadingListManager();
        public ObservableCollection<ReadingItem> Items { get; } = new ObservableCollection<ReadingItem>();

        private ReadingListManager() { }

        public async Task LoadAsync()
        {
            var items = await Services.DatabaseService.Database.Table<ReadingItem>().OrderBy(r => r.AddedDate).ToListAsync();
            Items.Clear();
            foreach (var item in items) Items.Add(item);
        }

        public bool ContainsUrl(string url)
        {
            return !string.IsNullOrEmpty(url) && Items.Any(r => r.Url == url);
        }

        public async Task AddAsync(string title, string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (ContainsUrl(url))
            {
                // 已存在 → 置顶（最近添加优先显示）
                var existing = Items.First(r => r.Url == url);
                await RemoveAsync(existing);
            }
            var item = new ReadingItem { Title = title ?? url, Url = url, AddedDate = DateTime.Now };
            await Services.DatabaseService.Database.InsertAsync(item);
            Items.Insert(0, item);
        }

        public async Task RemoveAsync(ReadingItem item)
        {
            await Services.DatabaseService.Database.DeleteAsync(item);
            Items.Remove(item);
        }
    }
}
