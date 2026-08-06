using System;
using System.Globalization;
using System.Linq;

namespace EdgeRebuild.Services
{
    /// <summary>
    /// 地址栏输入解析（网页打开 / 搜索二合一 + 中文域名 Punycode）：
    /// - 带协议（http/https/about/edge）→ 原样作网址
    /// - 含空格 → 搜索
    /// - 含点号（全角。．也识别）→ 转 Punycode 尝试作网址，失败才搜索
    /// - 无点号单词 / 中文 → 搜索；localhost、host:port 例外按网址处理
    /// 主窗口 ToolbarControl 与 Metro 地址栏共用。
    /// </summary>
    public static class UrlInputResolver
    {
        private static readonly IdnMapping _idn = new IdnMapping();

        public static string Resolve(string raw, string searchEngine)
        {
            raw = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(raw)) return null;

            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("edge:", StringComparison.OrdinalIgnoreCase))
                return raw;

            // 全角中文标点当点号：中文域名“例.cn”里的全角点也能当网址
            raw = raw.Replace('\u3002', '.').Replace('\uFF0E', '.');

            if (raw.Contains(" "))
                return BuildSearchUrl(raw, searchEngine);

            bool hasDot = raw.Contains('.');
            if (hasDot)
            {
                string converted = ToPunycode(raw);
                if (!converted.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !converted.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    converted = "https://" + converted;
                if (Uri.TryCreate(converted, UriKind.Absolute, out _))
                    return converted;
                return BuildSearchUrl(raw, searchEngine);
            }

            // 无点号：localhost 与 host:port 例外；其余（单词/中文）一律搜索
            if (raw.IndexOf(':') > 0)
            {
                string candidate = "http://" + raw;
                if (Uri.TryCreate(candidate, UriKind.Absolute, out _))
                    return candidate;
            }
            if (raw.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return "http://localhost";

            return BuildSearchUrl(raw, searchEngine);
        }

        public static string BuildSearchUrl(string query, string searchEngine)
        {
            return searchEngine switch
            {
                "Bing" => $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}",
                "DuckDuckGo" => $"https://duckduckgo.com/?q={Uri.EscapeDataString(query)}",
                _ => $"https://www.google.com/search?q={Uri.EscapeDataString(query)}"
            };
        }

        /// <summary>域名部分含中文 → Punycode（xn--）；整个串安全处理并保留协议/路径/端口。</summary>
        public static string ToPunycode(string url)
        {
            try
            {
                string protocol = "";
                int protoIdx = url.IndexOf("://");
                if (protoIdx >= 0)
                {
                    protocol = url.Substring(0, protoIdx + 3);
                    url = url.Substring(protoIdx + 3);
                }
                int slashIdx = url.IndexOf('/');
                string domain = slashIdx >= 0 ? url.Substring(0, slashIdx) : url;
                string path = slashIdx >= 0 ? url.Substring(slashIdx) : "";
                if (domain.Any(c => c > 127))
                    domain = _idn.GetAscii(domain);
                return protocol + domain + path;
            }
            catch
            {
                return url;
            }
        }
    }
}