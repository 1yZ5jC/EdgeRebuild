using System;
using System.Collections.ObjectModel;

namespace EdgeRebuild.Services
{
    public class DownloadItem
    {
        public string Url { get; set; }
        public string FileName { get; set; }
        public string Status { get; set; } = "下载中";
        public double Progress { get; set; } = 0; // 0~100
        public DateTime StartTime { get; set; } = DateTime.Now;
        public string FullPath { get; set; }
    }

    public static class DownloadManager
    {
        public static ObservableCollection<DownloadItem> Downloads { get; } = new ObservableCollection<DownloadItem>();

        public static DownloadItem Add(string url, string fullPath, string fileName)
        {
            var item = new DownloadItem
            {
                Url = url,
                FullPath = fullPath,
                FileName = fileName,
                Status = "下载中",
                Progress = 0,
                StartTime = DateTime.Now
            };
            Downloads.Insert(0, item);
            return item;
        }

        public static void ClearCompleted()
        {
            for (int i = Downloads.Count - 1; i >= 0; i--)
            {
                if (Downloads[i].Status == "已完成" || Downloads[i].Status == "已取消")
                    Downloads.RemoveAt(i);
            }
        }
    }
}
