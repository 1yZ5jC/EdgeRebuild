using SQLite;

namespace EdgeRebuild.Services
{
    [Table("Settings")]
    public class SettingItem
    {
        [PrimaryKey]
        public string Key { get; set; }
        public string Value { get; set; }
    }
}
