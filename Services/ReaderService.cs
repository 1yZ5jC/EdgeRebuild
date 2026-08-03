using EdgeRebuild.Core;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace EdgeRebuild.Services
{
    public class ArticleResult
    {
        public string Title { get; set; }
        public string Byline { get; set; }
        public string Content { get; set; }
        public string Text { get; set; }
    }

    /// <summary>
    /// 阅读模式：注入 Readability.js 提取正文，生成简洁的阅读视图 HTML。
    /// 双引擎共用（WebView2 / EdgeHTML）。
    /// </summary>
    public static class ReaderService
    {
        private static string _readabilityJs;

        private static async Task<string> LoadReadabilityJsAsync()
        {
            if (_readabilityJs != null) return _readabilityJs;
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Readability.js"));
            _readabilityJs = await FileIO.ReadTextAsync(file);
            return _readabilityJs;
        }

        /// <summary>
        /// 尝试进入阅读模式。成功返回 true（页面已切换到阅读视图），否则返回 false。
        /// </summary>
        public static async Task<bool> EnterReaderModeAsync(IBrowserTab tab)
        {
            try
            {
                string js = await LoadReadabilityJsAsync();
                Diagnostics.Log($"[阅读模式] Readability.js 加载成功 长度={js.Length} engine={tab.Engine} url={tab.CurrentUrl}");
                var article = await ExtractAsync(tab, js);
                if (article == null)
                {
                    Diagnostics.Log("[阅读模式] 提取结果为空（脚本返回 null 或解析失败）");
                    return false;
                }
                if (string.IsNullOrWhiteSpace(article.Content))
                {
                    Diagnostics.Log($"[阅读模式] 提取到标题但没有正文 title=\"{article.Title}\"");
                    return false;
                }

                string html = BuildReaderHtml(article);
                Diagnostics.Log($"[阅读模式] 提取成功 title=\"{article.Title}\" 正文长度={article.Content.Length}，切换阅读视图");
                await tab.NavigateToHtmlAsync(html);
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[阅读模式] 异常: {ex}");
                return false;
            }
        }

        private static async Task<ArticleResult> ExtractAsync(IBrowserTab tab, string readabilityJs)
        {
            const string extractScript = @"
;(function(){
    if (typeof Readability === 'undefined') return 'null';
    try {
        var baseUri = document.baseURI;
        var clone = document.cloneNode(true);
        var article = new Readability(clone).parse();
        if (!article) return 'null';

        var doc = new DOMParser().parseFromString(article.content, 'text/html');
        var imgs = doc.querySelectorAll('img');
        for (var i = 0; i < imgs.length; i++) {
            var src = imgs[i].getAttribute('src') || imgs[i].getAttribute('data-src') || '';
            if (src && !/^(data:|blob:)/.test(src)) {
                try { imgs[i].setAttribute('src', new URL(src, baseUri).href); } catch (e) {}
            }
        }
        return JSON.stringify({
            title: article.title || document.title || '',
            byline: article.byline || '',
            content: doc.body.innerHTML
        });
    } catch (e) {
        return 'null';
    }
})()";

            string result = await tab.ExecuteScriptAsync(readabilityJs + "\n" + extractScript);
            if (string.IsNullOrEmpty(result))
                Diagnostics.Log("[阅读模式] ExecuteScriptAsync 返回空（脚本未执行，可能被引擎拒绝或页面不可注入）");
            else
                Diagnostics.Log($"[阅读模式] 脚本原始返回（截断200字符）: {result.Substring(0, Math.Min(200, result.Length))}");
            return ParseResult(result);
        }

        /// <summary>
        /// 大声朗读用：提取文章纯文本（不切换页面，原页面保持不动）。
        /// </summary>
        public static async Task<ArticleResult> ExtractTextForTtsAsync(IBrowserTab tab)
        {
            try
            {
                string js = await LoadReadabilityJsAsync();
                string result = await tab.ExecuteScriptAsync(js + "\n" + TtsExtractScript);
                if (string.IsNullOrEmpty(result))
                {
                    Diagnostics.Log("[朗读] ExecuteScriptAsync 返回空");
                    return null;
                }
                return ParseResult(result);
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[朗读] 提取异常: {ex}");
                return null;
            }
        }

        private const string TtsExtractScript = @"
;(function(){
    if (typeof Readability === 'undefined') return 'null';
    try {
        var article = new Readability(document.cloneNode(true)).parse();
        if (!article) return 'null';
        var doc = new DOMParser().parseFromString(article.content, 'text/html');
        return JSON.stringify({
            title: article.title || document.title || '',
            byline: article.byline || '',
            content: doc.body.innerHTML,
            text: (doc.body.textContent || '').replace(/\s+/g, ' ').trim()
        });
    } catch (e) {
        return 'null';
    }
})()";

        private static ArticleResult ParseResult(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            string json = raw.Trim();
            if (json.StartsWith("\""))
            {
                try { json = JsonSerializer.Deserialize(json, AppJsonContext.Default.String); }
                catch (Exception ex)
                {
                    Diagnostics.Log($"[阅读模式] 外层字符串解析失败 原始长度={raw.Length}: {ex.Message}");
                    return null;
                }
            }
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                return JsonSerializer.Deserialize(json, AppJsonContext.Default.ArticleResult);
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[阅读模式] 文章 JSON 解析失败 长度={json.Length}: {ex.Message}");
                return null;
            }
        }

        private static string BuildReaderHtml(ArticleResult article)
        {
            string title = System.Net.WebUtility.HtmlEncode(article.Title ?? "");
            string byline = System.Net.WebUtility.HtmlEncode(article.Byline ?? "");

            return @"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>" + title + @"</title>
<style>
    html, body { margin: 0; padding: 0; background: #f3f1ec; color: #1a1a1a; }
    .reader-container { max-width: 760px; margin: 0 auto; padding: 48px 24px 72px 24px; }
    h1.reader-title { font-family: Georgia, 'Times New Roman', serif; font-size: 32px; line-height: 1.25; font-weight: 400; margin: 0 0 12px 0; }
    .reader-byline { font-family: 'Segoe UI', sans-serif; font-size: 13px; color: #6b6b6b; margin-bottom: 28px; }
    .reader-content { font-family: Georgia, 'Times New Roman', serif; font-size: 17px; line-height: 1.75; }
    .reader-content p { margin: 0 0 1.2em 0; }
    .reader-content img { max-width: 100%; height: auto; }
    .reader-content h1, .reader-content h2, .reader-content h3,
    .reader-content h4 { font-family: 'Segoe UI', sans-serif; line-height: 1.3; margin: 1.4em 0 0.6em 0; }
    .reader-content blockquote { border-left: 3px solid #d0cbbf; margin: 1.2em 0; padding: 0.2em 0 0.2em 1em; color: #555; }
    .reader-content pre { background: #eae7e0; padding: 12px; overflow-x: auto; font-size: 14px; }
    .reader-content a { color: #0067b8; }
    .reader-content table { border-collapse: collapse; }
    .reader-content td, .reader-content th { border: 1px solid #ccc; padding: 6px 10px; }
    .reader-content figure { margin: 1.2em 0; }
    .reader-content figcaption { font-size: 13px; color: #777; text-align: center; }
</style>
</head>
<body>
<div class=""reader-container"">
    <h1 class=""reader-title"">" + title + @"</h1>
    <div class=""reader-byline"">" + byline + @"</div>
    <div class=""reader-content"">" + article.Content + @"</div>
</div>
</body>
</html>";
        }
    }
}
