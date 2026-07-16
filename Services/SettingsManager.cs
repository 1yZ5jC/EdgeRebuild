using System.Threading.Tasks;

namespace EdgeRebuild.Services
{
    public static class SettingsManager
    {
        public static async Task SetAsync(string key, string value)
        {
            var item = new SettingItem { Key = key, Value = value };
            await DatabaseService.Database.InsertOrReplaceAsync(item);
        }

        public static async Task<string> GetAsync(string key)
        {
            var item = await DatabaseService.Database.Table<SettingItem>()
                .FirstOrDefaultAsync(s => s.Key == key);
            return item?.Value;
        }
    }
}
