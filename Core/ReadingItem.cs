using SQLite;
using System;

namespace EdgeRebuild.Core
{
    /// <summary>稍后读（旧版 Edge 阅读列表）：一次性阅读队列，与长期收藏分离。</summary>
    [Table("ReadingList")]
    public class ReadingItem
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public DateTime AddedDate { get; set; } = DateTime.Now;
    }
}
