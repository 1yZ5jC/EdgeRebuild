using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EdgeRebuild.Services
{
    public static class SettingsManager
    {
        public static event Action<string, string>? SettingChanged;

        // 启动时一次性读入全部设置，避免 MainPage/SettingsPane/Toolbar/每个标签
        // 各查一次 SQLite（启动链路上原本有 30+ 次逐项查询）。
        private static Dictionary<string, string>? _cache;

        public static async Task InitializeAsync()
        {
            var items = await DatabaseService.Database.Table<SettingItem>().ToListAsync();
            var cache = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.Key))
                    cache[item.Key] = item.Value;
            }
            _cache = cache;
        }

        /// <summary>
        /// 同步读取已缓存的设置；缓存未就绪时返回 fallback。
        /// 供主题初始化等首帧路径使用，避免 UI 线程阻塞等待 SQLite。
        /// </summary>
        public static string? GetCached(string key, string? fallback = null)
            => _cache != null && _cache.TryGetValue(key, out var value) ? value : fallback;

        public static async Task SetAsync(string key, string value)
        {
            var item = new SettingItem { Key = key, Value = value };
            await DatabaseService.Database.InsertOrReplaceAsync(item);
            if (_cache != null) _cache[key] = value;
            SettingChanged?.Invoke(key, value);
        }

        public static async Task<string?> GetAsync(string key)
        {
            if (_cache != null)
                return _cache.TryGetValue(key, out var value) ? value : null;

            // 缓存尚未初始化（例如数据层单测直接调用）时保持原行为。
            var item = await DatabaseService.Database.Table<SettingItem>()
                .FirstOrDefaultAsync(s => s.Key == key);
            return item?.Value;
        }
    }
}
