using SQLite;
using System;

namespace EdgeRebuild.Core
{
    [Table("Favorites")]
    public class FavoriteItem
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public DateTime AddedDate { get; set; } = DateTime.Now;

        /// <summary>所属文件夹；null 表示根目录。</summary>
        public int? FolderId { get; set; }
        public int SortOrder { get; set; }
    }
}