using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using System.Text;
using System.Text.RegularExpressions;

namespace EdgeRebuild.Core
{
    public class FavoritesManager
    {
        public static FavoritesManager Instance = new FavoritesManager();
        public ObservableCollection<FavoriteItem> Favorites { get; } = new ObservableCollection<FavoriteItem>();

        private FavoritesManager() { }

        public async Task LoadAsync()
        {
            var items = await Services.DatabaseService.Database.Table<FavoriteItem>().ToListAsync();
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
            await Services.DatabaseService.Database.InsertAsync(item);
            Favorites.Add(item);
        }

        public async Task RemoveAsync(FavoriteItem item)
        {
            await Services.DatabaseService.Database.DeleteAsync(item);
            Favorites.Remove(item);
        }

        public void Add(string title, string url) => _ = AddAsync(title, url);
        public void Remove(FavoriteItem item) => _ = RemoveAsync(item);

        // ========== HTML 导出 ==========
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

        // ========== HTML 导入 ==========
        public static async Task<int> ImportFromHtmlAsync(StorageFile file)
        {
            string html = await FileIO.ReadTextAsync(file);
            int count = 0;

            // 匹配所有 <a> 标签的 href 和内部文本
            var regex = new Regex(@"<a\s+[^>]*href\s*=\s*[""']([^""']*)[""'][^>]*>(.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var matches = regex.Matches(html);
            foreach (Match match in matches)
            {
                string url = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
                string title = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value.Trim());

                if (string.IsNullOrWhiteSpace(url)) continue;
                if (!Uri.TryCreate(url, UriKind.Absolute, out _)) continue;

                // 去重
                if (!Instance.ContainsUrl(url))
                {
                    Instance.Add(title, url);
                    count++;
                }
            }
            return count;
        }
    }
}