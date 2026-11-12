using EdgeRebuild.Services;
using Xunit;

namespace EdgeRebuild.CoreTests
{
    /// <summary>
    /// 地址栏输入解析（网页/搜索二合一 + 中文域名 Punycode）全矩阵回归。
    /// 规则矩阵见 Services/UrlInputResolver.cs 头注释。
    /// </summary>
    public class UrlInputResolverTests
    {
        private const string Google = "Google";
        private const string Bing = "Bing";
        private const string Duck = "DuckDuckGo";

        // ---- 带协议：原样透传 ----
        [Theory]
        [InlineData("http://a.b/c")]
        [InlineData("https://a.b/c?x=1")]
        [InlineData("about:blank")]
        [InlineData("edge:favorites")]
        public void Resolve_Scheme_PassesThrough(string input)
        {
            Assert.Equal(input, UrlInputResolver.Resolve(input, Google));
        }

        // ---- 本地文件路径：优先于“含空格→搜索”，地址栏可直接打开 ----
        [Theory]
        [InlineData(@"C:\Users\Alan\Downloads\book.epub")]
        [InlineData("C:/Users/Alan/Downloads/book.pdf")]
        [InlineData(@"\\server\share\book.epub")]
        [InlineData("file:///C:/Users/Alan/Downloads/book.epub")]
        [InlineData(@"C:\Users\Alan\My Books\book.epub")]
        public void Resolve_LocalPath_PassesThrough(string input)
        {
            Assert.Equal(input, UrlInputResolver.Resolve(input, Google));
        }

        [Fact]
        public void Resolve_QuotedLocalPath_TrimsQuotes()
        {
            Assert.Equal(@"C:\Users\Alan\My Books\book.epub",
                UrlInputResolver.Resolve("\"C:\\Users\\Alan\\My Books\\book.epub\"", Google));
        }

        // ---- 含空格 → 搜索（优先级高于点号判断）----
        [Theory]
        [InlineData("hello world")]
        [InlineData("example.com 是什么")]
        public void Resolve_WithSpace_Searches(string input)
        {
            var url = UrlInputResolver.Resolve(input, Google);
            Assert.StartsWith("https://www.google.com/search?q=", url);
        }

        // ---- 无点号单词/中文 → 搜索（核心修复点：不再误开 https://word）----
        [Theory]
        [InlineData("banana")]
        [InlineData("ab")]
        [InlineData("百度百科")]
        public void Resolve_BareWord_Searches(string input)
        {
            Assert.StartsWith("https://www.google.com/search?q=",
                UrlInputResolver.Resolve(input, Google));
        }

        // ---- localhost / host:port 例外 ----
        [Fact]
        public void Resolve_LocalHost_OpensUrl()
        {
            Assert.Equal("http://localhost", UrlInputResolver.Resolve("localhost", Google));
            Assert.Equal("http://localhost", UrlInputResolver.Resolve("LOCALHOST", Google));
        }

        [Theory]
        [InlineData("localhost:8080")]
        [InlineData("myhost:80")]
        public void Resolve_HostPort_OpensUrl(string input)
        {
            Assert.Equal("http://" + input, UrlInputResolver.Resolve(input, Google));
        }

        // ---- 点号域名 → 网址 ----
        [Fact]
        public void Resolve_Domain_AddsHttps()
        {
            Assert.Equal("https://example.com", UrlInputResolver.Resolve("example.com", Google));
        }

        [Fact]
        public void Resolve_UrlWithPathAndQuery_Preserved()
        {
            Assert.Equal("https://example.com/a/b?q=1",
                UrlInputResolver.Resolve("example.com/a/b?q=1", Google));
        }

        // ---- 中文域名 → Punycode ----
        [Fact]
        public void Resolve_ChineseDomain_Punycode()
        {
            var url = UrlInputResolver.Resolve("百度.com", Google);
            Assert.StartsWith("https://xn--", url);
            Assert.EndsWith(".com", url);
        }

        // ---- 全角句点识别为点号 ----
        [Fact]
        public void Resolve_FullWidthDot_TreatedAsDot()
        {
            var url = UrlInputResolver.Resolve("example。com", Google);
            Assert.Equal("https://example.com", url);
            url = UrlInputResolver.Resolve("example．com", Bing);
            Assert.Equal("https://example.com", url);
        }

        // ---- IP / IP:端口（含点号 → https 网址分支）----
        [Fact]
        public void Resolve_Ip_OpensHttps()
        {
            Assert.Equal("https://192.168.1.1", UrlInputResolver.Resolve("192.168.1.1", Google));
        }

        [Fact]
        public void Resolve_IpWithPort_OpensHttps()
        {
            // 带点号走域名分支，统一补 https；端口保留
            Assert.Equal("https://127.0.0.1:8080", UrlInputResolver.Resolve("127.0.0.1:8080", Google));
        }

        // ---- 搜索引擎路由 ----
        [Theory]
        [InlineData(Google, "https://www.google.com/search?q=")]
        [InlineData(Bing, "https://www.bing.com/search?q=")]
        [InlineData(Duck, "https://duckduckgo.com/?q=")]
        public void BuildSearchUrl_RoutesByEngine(string engine, string expectedPrefix)
        {
            Assert.StartsWith(expectedPrefix,
                UrlInputResolver.BuildSearchUrl("test", engine));
        }

        [Fact]
        public void BuildSearchUrl_EscapesQuery()
        {
            var url = UrlInputResolver.BuildSearchUrl("a b&c=d", Google);
            Assert.DoesNotContain(" ", url);
            Assert.Contains("%20", url);   // EscapeDataString：空格 → %20
            Assert.Contains("%26", url);   // & → %26（不被当作查询分隔符）
        }

        // ---- Punycode 转换细节 ----
        [Fact]
        public void ToPunycode_AsciiDomain_Unchanged()
        {
            Assert.Equal("example.com/a", UrlInputResolver.ToPunycode("example.com/a"));
        }

        [Fact]
        public void ToPunycode_ChineseDomain_Converted()
        {
            var converted = UrlInputResolver.ToPunycode("百度.com");
            Assert.StartsWith("xn--", converted);
        }

        [Fact]
        public void ToPunycode_KeepsProtocolAndPath()
        {
            var converted = UrlInputResolver.ToPunycode("https://中文.example.com/路径");
            Assert.StartsWith("https://", converted);
            Assert.EndsWith("/路径", converted);
        }
    }
}
