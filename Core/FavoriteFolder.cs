using SQLite;
using System;

namespace EdgeRebuild.Core
{
    /// <summary>
    /// 收藏夹文件夹。ParentId 为 null 表示根目录，可无限层级嵌套。
    /// </summary>
    [Table("FavoriteFolders")]
    public class FavoriteFolder
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string Name { get; set; }
        public int? ParentId { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}
