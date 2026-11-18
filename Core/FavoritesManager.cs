using System;
using System.Collections.Generic;
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
        public ObservableCollection<FavoriteFolder> Folders { get; } = new ObservableCollection<FavoriteFolder>();

        /// <summary>收藏夹结构（书签或文件夹）发生任何变更时触发，供收藏栏/星标刷新。</summary>
        public event EventHandler? Changed;

        private FavoritesManager() { }

        public async Task LoadAsync()
        {
            var items = await Services.DatabaseService.Database.Table<FavoriteItem>().ToListAsync();
            var folders = await Services.DatabaseService.Database.Table<FavoriteFolder>().ToListAsync();
            lock (_collSync)
            {
                Favorites.Clear();
                foreach (var item in items) Favorites.Add(item);
                Folders.Clear();
                foreach (var folder in folders) Folders.Add(folder);
            }
            RaiseChanged();
        }

        private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

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

        public bool ContainsUrl(string url) => FindByUrl(url) != null;

        /// <summary>按规范化 URL 查找收藏项；未收藏返回 null。</summary>
        public FavoriteItem? FindByUrl(string url)
        {
            var normalized = NormalizeUrl(url);
            return Favorites.FirstOrDefault(f => NormalizeUrl(f.Url) == normalized);
        }

        // ---- 文件夹查询 ----

        /// <summary>取某个文件夹的直接子文件夹；parentId 为 null 表示根目录。</summary>
        public IEnumerable<FavoriteFolder> GetFolders(int? parentId)
            => Folders.Where(f => f.ParentId == parentId)
                      .OrderBy(f => f.SortOrder)
                      .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                      .ToList();

        /// <summary>取某个文件夹的直接书签；folderId 为 null 表示根目录。</summary>
        public IEnumerable<FavoriteItem> GetItems(int? folderId)
            => Favorites.Where(f => f.FolderId == folderId)
                        .OrderBy(f => f.SortOrder)
                        .ThenBy(f => f.AddedDate)
                        .ToList();

        /// <summary>递归取文件夹子树内的全部书签（先子文件夹、后当前文件夹，与树形顺序一致）。</summary>
        public List<FavoriteItem> GetItemsRecursive(int? folderId)
        {
            var result = new List<FavoriteItem>();
            CollectItemsRecursive(folderId, result);
            return result;
        }

        private void CollectItemsRecursive(int? folderId, List<FavoriteItem> result)
        {
            foreach (var folder in GetFolders(folderId))
                CollectItemsRecursive(folder.Id, result);
            result.AddRange(GetItems(folderId));
        }

        private List<FavoriteItem> GetOrderedItems(int? folderId)
            => Favorites.Where(f => f.FolderId == folderId)
                        .OrderBy(f => f.SortOrder)
                        .ThenBy(f => f.AddedDate)
                        .ToList();

        private List<FavoriteFolder> GetOrderedFolders(int? parentId)
            => Folders.Where(f => f.ParentId == parentId)
                      .OrderBy(f => f.SortOrder)
                      .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                      .ToList();

        public string GetFolderPath(int? folderId)
            => FavoriteFolderTree.GetPath(BuildNodes(), folderId);

        /// <summary>移动到 newParentId 是否合法（不能移入自身或后代）。</summary>
        public bool CanMoveFolder(FavoriteFolder folder, int? newParentId)
            => folder != null && !FavoriteFolderTree.WouldCreateCycle(BuildNodes(), folder.Id, newParentId);

        public HashSet<int> GetSubtreeFolderIds(int folderId)
            => FavoriteFolderTree.CollectSubtreeIds(BuildNodes(), folderId);

        private List<FavoriteFolderNode> BuildNodes()
            => Folders.Select(f => new FavoriteFolderNode
            {
                Id = f.Id,
                ParentId = f.ParentId,
                Name = f.Name
            }).ToList();

        private int NextFolderSortOrder(int? parentId)
        {
            var siblings = Folders.Where(f => f.ParentId == parentId).Select(f => f.SortOrder).ToList();
            return siblings.Count == 0 ? 0 : siblings.Max() + 1;
        }

        private int NextItemSortOrder(int? folderId)
        {
            var siblings = Favorites.Where(f => f.FolderId == folderId).Select(f => f.SortOrder).ToList();
            return siblings.Count == 0 ? 0 : siblings.Max() + 1;
        }

        // ---- 书签 CRUD ----

        public async Task AddAsync(string title, string url, int? folderId = null, string? faviconUrl = null)
        {
            await AddItemInternalAsync(title, url, folderId, faviconUrl);
            RaiseChanged();
        }

        private async Task<FavoriteItem> AddItemInternalAsync(string title, string url, int? folderId, string? faviconUrl = null)
        {
            if (folderId.HasValue && !Folders.Any(f => f.Id == folderId.Value)) folderId = null;
            var item = new FavoriteItem
            {
                Title = title,
                Url = url,
                AddedDate = DateTime.Now,
                FolderId = folderId,
                SortOrder = NextItemSortOrder(folderId),
                FaviconUrl = faviconUrl
            };
            await Services.DatabaseService.Database.InsertAsync(item);
            lock (_collSync)
            {
                Favorites.Add(item);
            }
            return item;
        }

        public async Task RemoveAsync(FavoriteItem item)
        {
            lock (_collSync)
            {
                Favorites.Remove(item);
            }
            await Services.DatabaseService.Database.DeleteAsync(item);
            RaiseChanged();
        }

        public async Task RenameAsync(FavoriteItem item, string newTitle)
        {
            item.Title = newTitle;
            await Services.DatabaseService.Database.UpdateAsync(item);
            RaiseChanged();
        }

        /// <summary>把书签移动到指定文件夹；folderId 为 null 表示根目录。</summary>
        public async Task<bool> MoveItemAsync(FavoriteItem item, int? folderId)
        {
            if (item == null) return false;
            if (folderId.HasValue && !Folders.Any(f => f.Id == folderId.Value)) return false;
            if (item.FolderId == folderId) return true;

            item.FolderId = folderId;
            item.SortOrder = NextItemSortOrder(folderId);
            await Services.DatabaseService.Database.UpdateAsync(item);
            RaiseChanged();
            return true;
        }

        /// <summary>批量把书签移动到指定文件夹，返回实际移动数。</summary>
        public async Task<int> MoveItemsAsync(IEnumerable<FavoriteItem> items, int? folderId)
        {
            if (folderId.HasValue && !Folders.Any(f => f.Id == folderId.Value)) return 0;

            int moved = 0;
            foreach (var item in items?.Distinct().ToList() ?? new List<FavoriteItem>())
            {
                if (item == null || item.FolderId == folderId) continue;
                item.FolderId = folderId;
                item.SortOrder = NextItemSortOrder(folderId);
                await Services.DatabaseService.Database.UpdateAsync(item);
                moved++;
            }
            if (moved > 0) RaiseChanged();
            return moved;
        }

        /// <summary>更新书签的站点图标；仅在图标实际变化时触发 Changed。</summary>
        public async Task UpdateFaviconAsync(FavoriteItem item, string faviconUrl)
        {
            if (item == null || string.IsNullOrWhiteSpace(faviconUrl)) return;
            if (string.Equals(item.FaviconUrl, faviconUrl, StringComparison.Ordinal)) return;
            item.FaviconUrl = faviconUrl;
            await Services.DatabaseService.Database.UpdateAsync(item);
            RaiseChanged();
        }

        /// <summary>同级书签能否上移（delta=-1）或下移（delta=1）。</summary>
        public bool CanReorderItem(FavoriteItem item, int delta)
        {
            if (item == null || (delta != -1 && delta != 1)) return false;
            var siblings = GetOrderedItems(item.FolderId);
            int index = siblings.IndexOf(item);
            if (index < 0) return false;
            int target = index + delta;
            return target >= 0 && target < siblings.Count;
        }

        /// <summary>同级书签上移/下移一格，并顺带把同级 SortOrder 规范化为 0..n-1。</summary>
        public async Task<bool> ReorderItemAsync(FavoriteItem item, int delta)
        {
            if (!CanReorderItem(item, delta)) return false;

            var siblings = GetOrderedItems(item.FolderId);
            int index = siblings.IndexOf(item);
            int target = index + delta;
            var other = siblings[target];
            siblings[index] = other;
            siblings[target] = item;

            for (int i = 0; i < siblings.Count; i++)
            {
                if (siblings[i].SortOrder == i) continue;
                siblings[i].SortOrder = i;
                await Services.DatabaseService.Database.UpdateAsync(siblings[i]);
            }
            RaiseChanged();
            return true;
        }

        // ---- 文件夹 CRUD ----

        public async Task<FavoriteFolder> AddFolderAsync(string name, int? parentId = null)
        {
            var folder = await CreateFolderInternalAsync(name, parentId);
            RaiseChanged();
            return folder;
        }

        private async Task<FavoriteFolder> CreateFolderInternalAsync(string name, int? parentId)
        {
            if (parentId.HasValue && !Folders.Any(f => f.Id == parentId.Value)) parentId = null;
            var folder = new FavoriteFolder
            {
                Name = string.IsNullOrWhiteSpace(name) ? "新建文件夹" : name.Trim(),
                ParentId = parentId,
                SortOrder = NextFolderSortOrder(parentId),
                CreatedDate = DateTime.Now
            };
            await Services.DatabaseService.Database.InsertAsync(folder);
            lock (_collSync)
            {
                Folders.Add(folder);
            }
            return folder;
        }

        public async Task RenameFolderAsync(FavoriteFolder folder, string newName)
        {
            if (folder == null || string.IsNullOrWhiteSpace(newName)) return;
            folder.Name = newName.Trim();
            await Services.DatabaseService.Database.UpdateAsync(folder);
            RaiseChanged();
        }

        /// <summary>移动文件夹；目标为自身或后代时返回 false。</summary>
        public async Task<bool> MoveFolderAsync(FavoriteFolder folder, int? newParentId)
        {
            if (folder == null) return false;
            if (newParentId.HasValue && !Folders.Any(f => f.Id == newParentId.Value)) return false;
            if (!CanMoveFolder(folder, newParentId)) return false;
            if (folder.ParentId == newParentId) return true;

            folder.ParentId = newParentId;
            folder.SortOrder = NextFolderSortOrder(newParentId);
            await Services.DatabaseService.Database.UpdateAsync(folder);
            RaiseChanged();
            return true;
        }

        /// <summary>批量移动文件夹；目标位于任一被移动文件夹子树内时跳过该项。</summary>
        public async Task<int> MoveFoldersAsync(IEnumerable<FavoriteFolder> folders, int? newParentId)
        {
            if (newParentId.HasValue && !Folders.Any(f => f.Id == newParentId.Value)) return 0;

            int moved = 0;
            foreach (var folder in folders?.Distinct().ToList() ?? new List<FavoriteFolder>())
            {
                if (folder == null || folder.ParentId == newParentId) continue;
                if (!CanMoveFolder(folder, newParentId)) continue;
                folder.ParentId = newParentId;
                folder.SortOrder = NextFolderSortOrder(newParentId);
                await Services.DatabaseService.Database.UpdateAsync(folder);
                moved++;
            }
            if (moved > 0) RaiseChanged();
            return moved;
        }

        /// <summary>同级文件夹能否上移（delta=-1）或下移（delta=1）。</summary>
        public bool CanReorderFolder(FavoriteFolder folder, int delta)
        {
            if (folder == null || (delta != -1 && delta != 1)) return false;
            var siblings = GetOrderedFolders(folder.ParentId);
            int index = siblings.IndexOf(folder);
            if (index < 0) return false;
            int target = index + delta;
            return target >= 0 && target < siblings.Count;
        }

        /// <summary>同级文件夹上移/下移一格，并顺带把同级 SortOrder 规范化为 0..n-1。</summary>
        public async Task<bool> ReorderFolderAsync(FavoriteFolder folder, int delta)
        {
            if (!CanReorderFolder(folder, delta)) return false;

            var siblings = GetOrderedFolders(folder.ParentId);
            int index = siblings.IndexOf(folder);
            int target = index + delta;
            var other = siblings[target];
            siblings[index] = other;
            siblings[target] = folder;

            for (int i = 0; i < siblings.Count; i++)
            {
                if (siblings[i].SortOrder == i) continue;
                siblings[i].SortOrder = i;
                await Services.DatabaseService.Database.UpdateAsync(siblings[i]);
            }
            RaiseChanged();
            return true;
        }

        /// <summary>递归删除文件夹及其子文件夹和书签，返回删除的书签数。</summary>
        public async Task<int> DeleteFolderAsync(FavoriteFolder folder)
        {
            if (folder == null) return 0;

            var folderIds = GetSubtreeFolderIds(folder.Id);
            var itemsToRemove = Favorites
                .Where(f => f.FolderId.HasValue && folderIds.Contains(f.FolderId.Value))
                .ToList();
            var foldersToRemove = Folders.Where(f => folderIds.Contains(f.Id)).ToList();

            foreach (var item in itemsToRemove)
                await Services.DatabaseService.Database.DeleteAsync(item);
            foreach (var child in foldersToRemove)
                await Services.DatabaseService.Database.DeleteAsync(child);

            lock (_collSync)
            {
                foreach (var item in itemsToRemove) Favorites.Remove(item);
                foreach (var child in foldersToRemove) Folders.Remove(child);
            }
            RaiseChanged();
            return itemsToRemove.Count;
        }

        private FavoriteFolder? FindFolder(string name, int? parentId)
            => Folders.FirstOrDefault(f => f.ParentId == parentId &&
                string.Equals(f.Name, name, StringComparison.CurrentCultureIgnoreCase));

        // ---- 导入 / 导出（Netscape 书签格式，保留文件夹层级）----

        private static readonly Regex BookmarkTokenRegex = new Regex(
            @"<DL\b[^>]*>|</DL\s*>|<H3\b[^>]*>(?<h3>.*?)</H3\s*>|<A\b[^>]*\bhref\s*=\s*(?<quote>[""'])(?<href>.*?)\k<quote>[^>]*>(?<title>.*?)</A\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        public static async Task ExportToHtmlAsync(StorageFile file)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
            sb.AppendLine("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">");
            sb.AppendLine("<TITLE>Bookmarks</TITLE>");
            sb.AppendLine("<H1>Bookmarks</H1>");
            sb.AppendLine("<DL><p>");
            AppendFolderContents(sb, null, 1);
            sb.AppendLine("</DL><p>");
            await FileIO.WriteTextAsync(file, sb.ToString());
        }

        private static void AppendFolderContents(StringBuilder sb, int? parentId, int depth)
        {
            var manager = Instance;
            string pad = new string(' ', depth * 4);

            foreach (var folder in manager.GetFolders(parentId))
            {
                string name = System.Net.WebUtility.HtmlEncode(folder.Name ?? "新建文件夹");
                sb.AppendLine($"{pad}<DT><H3 ADD_DATE=\"{ToUnixSeconds(folder.CreatedDate)}\">{name}</H3>");
                sb.AppendLine($"{pad}<DL><p>");
                AppendFolderContents(sb, folder.Id, depth + 1);
                sb.AppendLine($"{pad}</DL><p>");
            }

            foreach (var fav in manager.GetItems(parentId))
            {
                string title = System.Net.WebUtility.HtmlEncode(fav.Title ?? fav.Url);
                string url = System.Net.WebUtility.HtmlEncode(fav.Url);
                sb.AppendLine($"{pad}<DT><A HREF=\"{url}\" ADD_DATE=\"{ToUnixSeconds(fav.AddedDate)}\">{title}</A>");
            }
        }

        private static long ToUnixSeconds(DateTime date)
        {
            if (date == default(DateTime)) return 0;
            try { return new DateTimeOffset(date).ToUnixTimeSeconds(); }
            catch { return 0; }
        }

        public static async Task<int> ImportFromHtmlAsync(StorageFile file)
        {
            string html = await FileIO.ReadTextAsync(file);
            var manager = Instance;
            int count = 0;
            bool structureChanged = false;

            var seenUrls = new HashSet<string>(
                manager.Favorites.Select(f => NormalizeUrl(f.Url)),
                StringComparer.OrdinalIgnoreCase);

            // 栈顶为当前文件夹；根目录用 null 表示。
            var folderStack = new Stack<int?>();
            folderStack.Push(null);
            string? pendingFolderName = null;

            foreach (Match match in BookmarkTokenRegex.Matches(html))
            {
                string token = match.Value;

                if (token.StartsWith("<DL", StringComparison.OrdinalIgnoreCase))
                {
                    if (pendingFolderName != null)
                    {
                        int? parentId = folderStack.Count > 0 ? folderStack.Peek() : (int?)null;
                        var existing = manager.FindFolder(pendingFolderName, parentId);
                        if (existing != null)
                        {
                            folderStack.Push(existing.Id);
                        }
                        else
                        {
                            var folder = await manager.CreateFolderInternalAsync(pendingFolderName, parentId);
                            folderStack.Push(folder.Id);
                            structureChanged = true;
                        }
                        pendingFolderName = null;
                    }
                    else
                    {
                        folderStack.Push(folderStack.Count > 0 ? folderStack.Peek() : (int?)null);
                    }
                }
                else if (token.StartsWith("</DL", StringComparison.OrdinalIgnoreCase))
                {
                    if (folderStack.Count > 1) folderStack.Pop();
                }
                else if (match.Groups["h3"].Success)
                {
                    string name = CleanHtmlText(match.Groups["h3"].Value);
                    if (!string.IsNullOrWhiteSpace(name)) pendingFolderName = name;
                }
                else if (match.Groups["href"].Success)
                {
                    string url = System.Net.WebUtility.HtmlDecode(match.Groups["href"].Value.Trim());
                    string title = CleanHtmlText(match.Groups["title"].Value);
                    if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _)) continue;

                    string normalized = NormalizeUrl(url);
                    if (!seenUrls.Add(normalized)) continue;

                    int? folderId = folderStack.Count > 0 ? folderStack.Peek() : (int?)null;
                    await manager.AddItemInternalAsync(
                        string.IsNullOrWhiteSpace(title) ? url : title, url, folderId);
                    count++;
                    structureChanged = true;
                }
            }

            if (structureChanged) manager.RaiseChanged();
            return count;
        }

        private static string CleanHtmlText(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string text = Regex.Replace(html, "<.*?>", " ", RegexOptions.Singleline);
            text = System.Net.WebUtility.HtmlDecode(text);
            return Regex.Replace(text, @"\s+", " ").Trim();
        }
    }
}
