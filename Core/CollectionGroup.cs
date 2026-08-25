using SQLite;
using System;

namespace EdgeRebuild.Core
{
    [Table("CollectionGroups")]
    public class CollectionGroup
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public int SortOrder { get; set; } = 0;
    }
}
