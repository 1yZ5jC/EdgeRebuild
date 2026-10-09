using EdgeRebuild.Services;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace EdgeRebuild.Core
{
    /// <summary>
    /// 磁贴主页数据源联动（P1-7）：设置「NtpTilesSource=Favorites」时，
    /// 磁贴主页（ntpmetro）每次加载后由宿主注入收藏夹前 N 项覆盖磁贴。
    /// 页面内编辑仍可用，但下次加载会被收藏夹重新覆盖（镜像语义，v1 取舍）。
    /// </summary>
    public static class NtpTileSeeder
    {
        private const string LsTiles = "ntp_tiles_metro_v5";   // 与 Assets/NtpMetro/index.html 的 LS_TILES 保持一致

        public static async Task<bool> ShouldSeedAsync()
        {
            try { return (await Services.SettingsManager.GetAsync("NtpTilesSource")) == "Favorites"; }
            catch { return false; }
        }

        public static string BuildSeedScript(int max = 8)
        {
            string entries = string.Join(",", FavoritesManager.Instance.Favorites
                .Where(f => !string.IsNullOrEmpty(f.Url))
                .Take(max)
                .Select(f =>
                {
                    string name = JsonSerializer.Serialize(string.IsNullOrEmpty(f.Title) ? f.Url : f.Title, AppJsonContext.Default.String);
                    string url = JsonSerializer.Serialize(f.Url, AppJsonContext.Default.String);
                    return $"{{name:{name},url:{url},wide:false}}";
                }));

            return $@"(function(){{
  var tiles = [{entries}];
  try {{ localStorage.setItem('{LsTiles}', JSON.stringify(tiles)); }} catch(e){{}}
  try {{
    tilesData = tiles;
    if (typeof render === 'function') render();
  }} catch(e){{}}
}})();";
        }
    }
}
