using System;
using System.IO;
using Windows.Storage;

namespace EdgeRebuild.Services
{
    /// <summary>
    /// 诊断日志：追加写入 LocalFolder 的 crash.log，线程安全。
    /// </summary>
    public static class Diagnostics
    {
        private static readonly object Lock = new object();

        public static void Log(string message)
        {
            try
            {
                lock (Lock)
                {
                    string logPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "crash.log");
                    File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}\n");
                }
            }
            catch { }
        }
    }
}
