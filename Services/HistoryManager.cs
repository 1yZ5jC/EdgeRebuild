using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace EdgeRebuild.Services
{
    public static class HistoryManager
    {
        private static readonly object _collSync = new object();   // 真多视图：集合变更跨线程串行化
        public static ObservableCollection<HistoryItem> History { get; } = new ObservableCollection<HistoryItem>();

        public static async Task LoadAsync()
        {
            var items = await DatabaseService.Database.Table<HistoryItem>().ToListAsync();
            lock (_collSync)
            {
                History.Clear();
                foreach (var item in items) History.Add(item);
            }
        }

        /// <summary>
        /// 先以 URL 作占位标题入库（导航瞬间真实标题尚未解析），返回记录供
        /// TitleChanged 到来后经 UpdateTitleAsync 修正，避免"旧页标题配新网址"。
        /// </summary>
        public static async Task<HistoryItem> AddAsync(string title, string url)
        {
            var item = new HistoryItem { Title = title ?? url, Url = url, VisitTime = DateTime.Now };
            await DatabaseService.Database.InsertAsync(item);
            List<HistoryItem> overflow = null;
            lock (_collSync)
            {
                History.Insert(0, item);
                if (History.Count > 1000)
                {
                    overflow = History.Skip(1000).ToList();
                    foreach (var last in overflow) History.Remove(last);
                }
            }
            if (overflow != null)
                foreach (var last in overflow)
                    await DatabaseService.Database.DeleteAsync(last);
            return item;
        }

        public static async Task UpdateTitleAsync(HistoryItem item, string title)
        {
            if (item == null || string.IsNullOrEmpty(title) || item.Title == title) return;
            item.Title = title;
            try { await DatabaseService.Database.UpdateAsync(item); } catch { }
        }

        public static async Task ClearAsync()
        {
            await DatabaseService.Database.DeleteAllAsync<HistoryItem>();
            lock (_collSync) History.Clear();
        }

        public static async Task RemoveAsync(HistoryItem item)
        {
            lock (_collSync) History.Remove(item);
            await DatabaseService.Database.DeleteAsync(item);
        }

        public static async Task RemoveManyAsync(List<int> ids)
        {
            if (ids == null || ids.Count == 0) return;
            foreach (var id in ids)
            {
                HistoryItem item;
                lock (_collSync)
                {
                    item = History.FirstOrDefault(h => h.Id == id);
                    if (item != null) History.Remove(item);
                }
                await DatabaseService.Database.DeleteAsync<HistoryItem>(id);
            }
        }
    }
}