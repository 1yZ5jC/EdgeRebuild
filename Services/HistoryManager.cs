using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace EdgeRebuild.Services
{
    public static class HistoryManager
    {
        public static ObservableCollection<HistoryItem> History { get; } = new ObservableCollection<HistoryItem>();

        public static async Task LoadAsync()
        {
            var items = await DatabaseService.Database.Table<HistoryItem>().ToListAsync();
            History.Clear();
            foreach (var item in items)
                History.Add(item);
        }

        public static void Add(string title, string url)
        {
            var item = new HistoryItem
            {
                Title = title ?? url,
                Url = url,
                VisitTime = DateTime.Now
            };
            // 异步写入数据库
            _ = DatabaseService.Database.InsertAsync(item);
            History.Insert(0, item);

            // 最多保留 1000 条，同时清理数据库
            while (History.Count > 1000)
            {
                var last = History.Last();
                _ = DatabaseService.Database.DeleteAsync(last);
                History.Remove(last);
            }
        }

        public static async Task ClearAsync()
        {
            await DatabaseService.Database.DeleteAllAsync<HistoryItem>();
            History.Clear();
        }

        public static void Clear()
        {
            _ = ClearAsync();
        }
    }
}