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
            _ = DatabaseService.Database.InsertAsync(item);
            History.Insert(0, item);

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

        // 新增：删除单条历史记录
        public static async void Remove(HistoryItem item)
        {
            History.Remove(item);
            await DatabaseService.Database.DeleteAsync(item);
        }
    }
}