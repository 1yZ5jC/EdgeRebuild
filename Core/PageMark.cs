using SQLite;
using System;

namespace EdgeRebuild.Core
{
    [Table("PageMarks")]
    public class PageMark
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string Url { get; set; }        // 规范化 URL（去 #fragment）
        public string XPath { get; set; }      // 锚点：起始文本节点 XPath
        public int Offset { get; set; }        // 节点内字符偏移
        public string Text { get; set; }       // 高亮文本原文（恢复时校验）
        public string Color { get; set; }      // 高亮颜色（如 #FFD75E）
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
