using EdgeRebuild.Core;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace EdgeRebuild.Services
{
    public class FavoritesManager
    {
        public static FavoritesManager Instance = new FavoritesManager();
        public ObservableCollection<FavoriteItem> Favorites { get; } = new ObservableCollection<FavoriteItem>();

        private FavoritesManager() { }

        // 从数据库加载
        public async Task LoadAsync()
        {
            var items = await DatabaseService.Database.Table<FavoriteItem>().ToListAsync();
            Favorites.Clear();
            foreach (var item in items)
                Favorites.Add(item);
        }

        public bool ContainsUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return Favorites.Any(f => f.Url == url);
        }

        public async Task AddAsync(string title, string url)
        {
            var item = new FavoriteItem { Title = title, Url = url, AddedDate = DateTime.Now };
            await DatabaseService.Database.InsertAsync(item);
            Favorites.Add(item);
        }

        public async Task RemoveAsync(FavoriteItem item)
        {
            await DatabaseService.Database.DeleteAsync(item);
            Favorites.Remove(item);
        }

        // 兼容之前的同步方法（供 UI 调用，内部启动异步任务）
        public void Add(string title, string url)
        {
            _ = AddAsync(title, url);
        }

        public void Remove(FavoriteItem item)
        {
            _ = RemoveAsync(item);
        }
    }
}