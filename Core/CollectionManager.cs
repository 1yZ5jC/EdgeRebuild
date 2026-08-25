using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace EdgeRebuild.Core
{
    public class CollectionManager
    {
        private static readonly object _collSync = new object();
        public static CollectionManager Instance = new CollectionManager();

        public ObservableCollection<CollectionGroup> Groups { get; } = new ObservableCollection<CollectionGroup>();
        public ObservableCollection<CollectionItem> Items { get; } = new ObservableCollection<CollectionItem>();

        private CollectionManager() { }

        // ---- 加载 ----

        public async Task LoadGroupsAsync()
        {
            var groups = await Services.DatabaseService.Database
                .Table<CollectionGroup>().OrderBy(g => g.SortOrder).ToListAsync();
            lock (_collSync)
            {
                Groups.Clear();
                foreach (var g in groups) Groups.Add(g);
            }
        }

        public async Task LoadItemsAsync()
        {
            var items = await Services.DatabaseService.Database
                .Table<CollectionItem>().OrderByDescending(i => i.AddedDate).ToListAsync();
            lock (_collSync)
            {
                Items.Clear();
                foreach (var i in items) Items.Add(i);
            }
        }

        public async Task<List<CollectionItem>> GetItemsForGroupAsync(int groupId)
        {
            return await Services.DatabaseService.Database
                .Table<CollectionItem>()
                .Where(i => i.GroupId == groupId)
                .OrderByDescending(i => i.AddedDate)
                .ToListAsync();
        }

        // ---- 分组 CRUD ----

        public async Task<CollectionGroup> AddGroupAsync(string name)
        {
            int maxOrder = 0;
            lock (_collSync)
            {
                if (Groups.Count > 0)
                    maxOrder = Groups.Max(g => g.SortOrder) + 1;
            }
            var group = new CollectionGroup
            {
                Name = name ?? "未命名分组",
                CreatedDate = DateTime.Now,
                SortOrder = maxOrder
            };
            await Services.DatabaseService.Database.InsertAsync(group);
            lock (_collSync) { Groups.Add(group); }
            return group;
        }

        public async Task RenameGroupAsync(CollectionGroup group, string newName)
        {
            group.Name = newName ?? group.Name;
            await Services.DatabaseService.Database.UpdateAsync(group);
        }

        public async Task DeleteGroupAsync(CollectionGroup group)
        {
            // 删除分组下所有条目
            var items = await GetItemsForGroupAsync(group.Id);
            foreach (var item in items)
                await Services.DatabaseService.Database.DeleteAsync(item);
            lock (_collSync)
            {
                var toRemove = Items.Where(i => i.GroupId == group.Id).ToList();
                foreach (var i in toRemove) Items.Remove(i);
                Groups.Remove(group);
            }
            await Services.DatabaseService.Database.DeleteAsync(group);
        }

        // ---- 条目 CRUD ----

        public async Task<CollectionItem> AddItemAsync(
            string title, string content, CollectionItemType type,
            int groupId, string snippet = null)
        {
            var item = new CollectionItem
            {
                GroupId = groupId,
                ItemType = type,
                Title = title ?? content ?? "",
                Content = content ?? "",
                Snippet = snippet,
                AddedDate = DateTime.Now
            };
            await Services.DatabaseService.Database.InsertAsync(item);
            lock (_collSync) { Items.Insert(0, item); }
            return item;
        }

        public async Task RemoveItemAsync(CollectionItem item)
        {
            lock (_collSync) { Items.Remove(item); }
            await Services.DatabaseService.Database.DeleteAsync(item);
        }

        public async Task MoveItemAsync(CollectionItem item, int newGroupId)
        {
            item.GroupId = newGroupId;
            await Services.DatabaseService.Database.UpdateAsync(item);
        }

        // ---- 导出 Markdown ----

        public static async Task ExportToMarkdownAsync(CollectionGroup group, StorageFile file)
        {
            var items = await Instance.GetItemsForGroupAsync(group.Id);
            var sb = new StringBuilder();
            sb.AppendLine($"# {group.Name}");
            sb.AppendLine();
            sb.AppendLine($"*导出时间: {DateTime.Now:yyyy-MM-dd HH:mm}*");
            sb.AppendLine();

            foreach (var item in items)
            {
                switch (item.ItemType)
                {
                    case CollectionItemType.Link:
                        sb.AppendLine($"- [{item.Title}]({item.Content})");
                        if (!string.IsNullOrEmpty(item.Snippet))
                            sb.AppendLine($"  > {item.Snippet}");
                        break;
                    case CollectionItemType.Text:
                        sb.AppendLine($"- **{item.Title}**");
                        sb.AppendLine($"  {item.Content}");
                        break;
                    case CollectionItemType.Image:
                        sb.AppendLine($"- ![{item.Title}]({item.Content})");
                        break;
                    case CollectionItemType.Note:
                        sb.AppendLine($"- {item.Content}");
                        break;
                }
                sb.AppendLine();
            }
            await FileIO.WriteTextAsync(file, sb.ToString());
        }

        // ---- 导出 HTML ----

        public static async Task ExportToHtmlAsync(CollectionGroup group, StorageFile file)
        {
            var items = await Instance.GetItemsForGroupAsync(group.Id);
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset=\"UTF-8\">");
            sb.AppendLine($"<title>{System.Net.WebUtility.HtmlEncode(group.Name)}</title>");
            sb.AppendLine("<style>body{font-family:Segoe UI,sans-serif;max-width:800px;margin:40px auto;padding:0 20px;}");
            sb.AppendLine(".item{margin:12px 0;padding:8px;border-bottom:1px solid #eee;}");
            sb.AppendLine(".link{color:#0066cc;text-decoration:none;}");
            sb.AppendLine(".text{color:#333;}.note{color:#666;font-style:italic;}");
            sb.AppendLine("img{max-width:100%;border-radius:4px;}</style></head><body>");
            sb.AppendLine($"<h1>{System.Net.WebUtility.HtmlEncode(group.Name)}</h1>");
            sb.AppendLine($"<p style=\"color:#999\">导出时间: {DateTime.Now:yyyy-MM-dd HH:mm}</p>");

            foreach (var item in items)
            {
                sb.AppendLine("<div class=\"item\">");
                switch (item.ItemType)
                {
                    case CollectionItemType.Link:
                        sb.AppendLine($"  <a class=\"link\" href=\"{System.Net.WebUtility.HtmlEncode(item.Content)}\">");
                        sb.AppendLine($"    {System.Net.WebUtility.HtmlEncode(item.Title)}</a>");
                        if (!string.IsNullOrEmpty(item.Snippet))
                            sb.AppendLine($"  <p style=\"color:#666;margin:4px 0 0\">{System.Net.WebUtility.HtmlEncode(item.Snippet)}</p>");
                        break;
                    case CollectionItemType.Text:
                        sb.AppendLine($"  <div class=\"text\"><strong>{System.Net.WebUtility.HtmlEncode(item.Title)}</strong></div>");
                        sb.AppendLine($"  <div class=\"text\">{System.Net.WebUtility.HtmlEncode(item.Content)}</div>");
                        break;
                    case CollectionItemType.Image:
                        sb.AppendLine($"  <div>{System.Net.WebUtility.HtmlEncode(item.Title)}</div>");
                        sb.AppendLine($"  <img src=\"{System.Net.WebUtility.HtmlEncode(item.Content)}\" alt=\"{System.Net.WebUtility.HtmlEncode(item.Title)}\">");
                        break;
                    case CollectionItemType.Note:
                        sb.AppendLine($"  <div class=\"note\">{System.Net.WebUtility.HtmlEncode(item.Content)}</div>");
                        break;
                }
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</body></html>");
            await FileIO.WriteTextAsync(file, sb.ToString());
        }
    }
}
