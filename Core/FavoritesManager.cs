using System.Collections.ObjectModel;
using System.Linq;

namespace EdgeRebuild.Core
{
    public class FavoritesManager
    {
        public static FavoritesManager Instance = new FavoritesManager();
        public ObservableCollection<FavoriteItem> Favorites { get; } = new ObservableCollection<FavoriteItem>();

        public bool ContainsUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return Favorites.Any(f => f.Url == url);
        }

        public void Add(string title, string url)
        {
            Favorites.Add(new FavoriteItem { Title = title, Url = url });
        }

        public void Remove(FavoriteItem item)
        {
            Favorites.Remove(item);
        }
    }
}