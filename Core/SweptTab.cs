using SQLite;

namespace EdgeRebuild.Core
{
    /// <summary>搁置组内的单个标签页快照。</summary>
    [Table("SweptTabs")]
    public class SweptTab
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int GroupId { get; set; }

        public string Url { get; set; }
        public string Title { get; set; }
        public string Engine { get; set; }
        public int Position { get; set; }
        public bool IsPinned { get; set; }
    }
}
