using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;

namespace EdgeRebuild.Core
{
    public class FavoritesManager
    {
        private static readonly object _collSync = new object();   // 真多视图：集合变更跨线程串行化
        public static FavoritesManager Instance = new FavoritesManager();
        public ObservableCollection<FavoriteItem> Favorites { get; } = new ObservableCollection<FavoriteItem>();

        private FavoritesManager() { }

        public async Task LoadAsync()
        {
            var items = await Services.DatabaseService.Database.Table<FavoriteItem>().ToListAsync();
            Favorites.Clear();
            foreach (var item in items) Favorites.Add(item);
        }

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            try
            {
                var uri = new Uri(url);
                string normalized = uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
                if (normalized.EndsWith("/") && !normalized.EndsWith("//"))
                    normalized = normalized.Substring(0, normalized.Length - 1);
                return normalized;
            }
            catch { return url; }
        }

        public bool ContainsUrl(string url)
        {
            var normalized = NormalizeUrl(url);
            return Favorites.Any(f => NormalizeUrl(f.Url) == normalized);
        }

        public async Task AddAsync(string title, string url)
        {
            var item = new FavoriteItem { Title = title, Url = url, AddedDate = DateTime.Now };
            await Services.DatabaseService.Database.InsertAsync(item);
            lock (_collSync)
            {
                Favorites.Add(item);
            }
        }

        public async Task RemoveAsync(FavoriteItem item)
        {
            lock (_collSync)
            {
                Favorites.Remove(item);
            }
            await Services.DatabaseService.Database.DeleteAsync(item);
        }

        public async Task RenameAsync(FavoriteItem item, string newTitle)
        {
            item.Title = newTitle;
            await Services.DatabaseService.Database.UpdateAsync(item);
        }

        public static async Task ExportToHtmlAsync(StorageFile file)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
            sb.AppendLine("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">");
            sb.AppendLine("<TITLE>Bookmarks</TITLE>");
            sb.AppendLine("<H1>Bookmarks</H1>");
            sb.AppendLine("<DL><p>");
            foreach (var fav in Instance.Favorites)
            {
                string title = System.Net.WebUtility.HtmlEncode(fav.Title ?? fav.Url);
                string url = System.Net.WebUtility.HtmlEncode(fav.Url);
                long addDate = new DateTimeOffset(fav.AddedDate).ToUnixTimeSeconds();
                sb.AppendLine($"<DT><A HREF=\"{url}\" ADD_DATE=\"{addDate}\">{title}</A>");
            }
            sb.AppendLine("</DL><p>");
            await FileIO.WriteTextAsync(file, sb.ToString());
        }

        public static async Task<int> ImportFromHtmlAsync(StorageFile file)
        {
            string html = await FileIO.ReadTextAsync(file);
            int count = 0;
            var regex = new Regex(@"<a\s+[^>]*href\s*=\s*[""']([^""']*)[""'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match match in regex.Matches(html))
            {
                string url = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
                string title = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value.Trim());
                if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _)) continue;
                if (!Instance.ContainsUrl(url)) { await Instance.AddAsync(title, url); count++; }
            }
            return count;
        }
    }
}