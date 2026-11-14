using SQLite;
using System;
using System.Collections.Generic;

namespace EdgeRebuild.Core
{
    /// <summary>
    /// 一次“搁置”操作产生的组。与会话表分离，重启后不会当普通标签恢复。
    /// </summary>
    [Table("SweptTabGroups")]
    public class SweptTabGroup
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>自动生成，如“3 个标签页”；MVP 不支持重命名。</summary>
        public string Title { get; set; }

        /// <summary>来源窗口槽位，仅用于诊断；搁置组本身是全局抽屉。</summary>
        public int OriginSlot { get; set; }

        [Ignore]
        public List<SweptTab> Tabs { get; set; } = new List<SweptTab>();
    }
}
