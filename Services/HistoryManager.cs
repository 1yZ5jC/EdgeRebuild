using System;
using System.Collections.ObjectModel;

namespace EdgeRebuild.Services
{
    public class HistoryItem
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public DateTime VisitTime { get; set; }
    }

    public static class HistoryManager
    {
        public static ObservableCollection<HistoryItem> History { get; } = new ObservableCollection<HistoryItem>();

        public static void Add(string title, string url)
        {
            History.Insert(0, new HistoryItem
            {
                Title = title ?? url,
                Url = url,
                VisitTime = DateTime.Now
            });

            // 最多保留 1000 条
            while (History.Count > 1000)
                History.RemoveAt(History.Count - 1);
        }

        public static void Clear()
        {
            History.Clear();
        }
    }
}