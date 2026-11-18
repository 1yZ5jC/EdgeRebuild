using SQLite;
using System;

namespace EdgeRebuild.Core
{
    [Table("Favorites")]
    public class FavoriteItem
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public DateTime AddedDate { get; set; } = DateTime.Now;

        /// <summary>所属文件夹；null 表示根目录。</summary>
        public int? FolderId { get; set; }
        public int SortOrder { get; set; }

        /// <summary>站点图标地址；为空时 UI 回退为星形图标。</summary>
        public string? FaviconUrl { get; set; }
    }
}