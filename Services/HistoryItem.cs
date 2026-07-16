using SQLite;
using System;

namespace EdgeRebuild.Services
{
    [Table("History")]
    public class HistoryItem
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public DateTime VisitTime { get; set; }
    }
}
