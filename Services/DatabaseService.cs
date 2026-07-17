using EdgeRebuild.Core;
using SQLite;
using System;
using System.Threading.Tasks;

namespace EdgeRebuild.Services
{
    public static class DatabaseService
    {
        private static SQLiteAsyncConnection _database;

        public static SQLiteAsyncConnection Database
        {
            get
            {
                if (_database == null)
                    throw new InvalidOperationException("Database not initialized. Call InitializeAsync first.");
                return _database;
            }
        }

        public static async Task InitializeAsync()
        {
            if (_database != null) return;
            // 数据库文件保存在应用本地文件夹
            var dbPath = System.IO.Path.Combine(
                Windows.Storage.ApplicationData.Current.LocalFolder.Path, "edgerebuild.db");
            _database = new SQLiteAsyncConnection(dbPath);

            // 创建表
            await _database.CreateTableAsync<FavoriteItem>();
            await _database.CreateTableAsync<HistoryItem>();
            await _database.CreateTableAsync<SettingItem>();
            await _database.CreateTableAsync<DownloadRecord>();
        }
    }
}