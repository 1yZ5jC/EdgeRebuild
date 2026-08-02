using EdgeRebuild.Core;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace EdgeRebuild.Services
{
    /// <summary>
    /// 网页文本高亮标记：选中文字 → 注入 mark 元素 → 锚点存库 → 重开页面自动恢复。
    /// 双引擎共用（WebView2 / EdgeHTML，经 ExecuteScriptAsync 注入 Assets/MarkScript.js）。
    /// </summary>
    public static class PageMarkManager
    {
        public const string DefaultColor = "#FFD75E";

        // 右键菜单可选颜色
        public static readonly (string Name, string Color)[] MarkColors =
        {
            ("黄色", "#FFD75E"),
            ("绿色", "#A8E6A3"),
            ("蓝色", "#A3D0F3"),
            ("粉色", "#F3B3C8"),
        };

        private static string _markScript;

        private static async Task<string> LoadScriptAsync()
        {
            if (_markScript != null) return _markScript;
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/MarkScript.js"));
            _markScript = await FileIO.ReadTextAsync(file);
            return _markScript;
        }

        public static string NormalizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            try
            {
                var uri = new Uri(url);
                string s = uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
                int hash = s.IndexOf('#');
                if (hash >= 0) s = s.Substring(0, hash);
                if (s.EndsWith("/") && !s.EndsWith("//"))
                    s = s.Substring(0, s.Length - 1);
                return s;
            }
            catch { return url; }
        }

        public static bool CanMarkUrl(string url)
        {
            return !string.IsNullOrEmpty(url)
                && url.Contains("://")
                && !url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("ms-appx", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
        }

        // 将字符串嵌入单引号 JS 字符串字面量
        private static string JsQuote(string s)
        {
            if (s == null) return "''";
            return "'" + s.Replace("\\", "\\\\").Replace("'", "\\'")
                .Replace("\r", "\\r").Replace("\n", "\\n") + "'";
        }

        private static string Unquote(string s) => s?.Trim().Trim('"') ?? "";

        /// <summary>
        /// 标记当前选中文本。成功返回标记记录，无选中/无法标记返回 null。
        /// </summary>
        public static async Task<PageMark> MarkSelectionAsync(IBrowserTab tab, string color)
        {
            try
            {
                string url = NormalizeUrl(tab.CurrentUrl);
                if (!CanMarkUrl(url) || tab.IsSuspended) return null;

                string js = await LoadScriptAsync();
                string raw = Unquote(await tab.ExecuteScriptAsync(js + ";window.EdgMarks.getSelectionMark();"));
                if (raw == "null" || raw.Length == 0) return null;

                using (var doc = JsonDocument.Parse(raw))
                {
                    var root = doc.RootElement;
                    string xpath = root.GetProperty("xpath").GetString();
                    int offset = root.GetProperty("offset").GetInt32();
                    string text = root.GetProperty("text").GetString();
                    if (string.IsNullOrEmpty(xpath) || string.IsNullOrEmpty(text)) return null;

                    var mark = new PageMark
                    {
                        Url = url,
                        XPath = xpath,
                        Offset = offset,
                        Text = text,
                        Color = color ?? DefaultColor
                    };
                    await DatabaseService.Database.InsertAsync(mark);

                    await tab.ExecuteScriptAsync(js + ";window.EdgMarks.initClick();window.EdgMarks.applyMark("
                        + mark.Id + "," + JsQuote(xpath) + "," + offset + ","
                        + JsQuote(text) + "," + JsQuote(mark.Color) + ");");
                    Diagnostics.Log($"[标记] 新增 id={mark.Id} url={url} 文本长度={text.Length}");
                    return mark;
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[标记] 标记选中异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 导航完成后恢复当前页面的全部标记。
        /// </summary>
        public static async Task RestoreAsync(IBrowserTab tab)
        {
            try
            {
                if (tab == null || tab.IsSuspended) return;
                string url = NormalizeUrl(tab.CurrentUrl);
                if (!CanMarkUrl(url)) return;

                var marks = await DatabaseService.Database.Table<PageMark>()
                    .Where(m => m.Url == url).ToListAsync();
                if (marks.Count == 0) return;

                string js = await LoadScriptAsync();
                string json = JsonSerializer.Serialize(marks);
                string result = await tab.ExecuteScriptAsync(js
                    + ";window.EdgMarks.initClick();window.EdgMarks.applyMarks(" + JsQuote(json) + ");");
                Diagnostics.Log($"[标记] 恢复 url={url} 记录={marks.Count} 应用={Unquote(result)}");
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[标记] 恢复异常: {ex.Message}");
            }
        }

        public static async Task RemoveMarkAsync(int id)
        {
            try
            {
                await DatabaseService.Database.ExecuteAsync("DELETE FROM PageMarks WHERE Id = ?", id);
                Diagnostics.Log($"[标记] 已删除 id={id}");
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[标记] 删除异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 清除当前页面的全部标记（DB + DOM）。
        /// </summary>
        public static async Task ClearPageAsync(IBrowserTab tab)
        {
            try
            {
                string url = NormalizeUrl(tab.CurrentUrl);
                if (!CanMarkUrl(url)) return;
                await DatabaseService.Database.ExecuteAsync("DELETE FROM PageMarks WHERE Url = ?", url);
                string js = await LoadScriptAsync();
                await tab.ExecuteScriptAsync(js + ";window.EdgMarks.clearMarks();");
                Diagnostics.Log($"[标记] 已清除页面标记 url={url}");
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[标记] 清除异常: {ex.Message}");
            }
        }
    }
}
