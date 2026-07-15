using System.Collections.ObjectModel;

namespace EdgeRebuild.Core
{
    public class FavoritesManager
    {
        public static FavoritesManager Instance = new FavoritesManager();
        public ObservableCollection<FavoriteItem> Favorites { get; } = new ObservableCollection<FavoriteItem>();

        public void Add(string title, string url)
        {
            Favorites.Add(new FavoriteItem { Title = title, Url = url });
            System.Diagnostics.Debug.WriteLine($"收藏成功: {title} - {url}");
        }
    }
}