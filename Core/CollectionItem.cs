using SQLite;
using System;

namespace EdgeRebuild.Core
{
    public enum CollectionItemType { Link = 0, Text = 1, Image = 2, Note = 3 }

    [Table("CollectionItems")]
    public class CollectionItem
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public int GroupId { get; set; }
        public CollectionItemType ItemType { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }    // URL / 纯文本 / 图片地址
        public string Snippet { get; set; }    // 预览摘录（可空）
        public DateTime AddedDate { get; set; } = DateTime.Now;
    }
}
