using System;
using System.IO;
using System.Threading.Tasks;
using EdgeRebuild.Services;
using Windows.Storage;

namespace EdgeRebuild.Core
{
    public enum DocumentKind
    {
        Pdf,
        Epub
    }

    public sealed class ReaderRestoreTarget
    {
        public DocumentKind Kind { get; set; }
        public string SourceUrl { get; set; }
        public string CacheFileName { get; set; }
    }

    /// <summary>
    /// 本地/网络 PDF、EPUB 的统一打开与阅读器标签恢复。
    /// 阅读器 URL 只是渲染入口，真正的内容在 LocalState\contentcache：
    /// 恢复时必须重建缓存/查看器地址，不能直接 Navigate 旧 URL。
    /// </summary>
    public static class DocumentOpenService
    {
        public const string CacheFolderName = "contentcache";

        public static string CacheFolderPath =>
            Path.Combine(ApplicationData.Current.LocalFolder.Path, CacheFolderName);

        public static string Sha12(string value)
        {
            if (string.IsNullOrEmpty(value)) value = "";
            using var sha = System.Security.Cryptography.SHA256.Create();
            byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 12);
        }

        /// <summary>
        /// 把阅读器 file 参数（contentcache.local URL / ms-appdata 路径 / 文件名）
        /// 归一为 contentcache 下的物理路径。
        /// </summary>
        public static string GetCachePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            string name = fileName.Trim();

            const string cachePrefix = "https://contentcache.local/";
            if (name.StartsWith(cachePrefix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(cachePrefix.Length);

            const string appDataPrefix = "ms-appdata:///local/contentcache/";
            if (name.StartsWith(appDataPrefix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(appDataPrefix.Length);

            name = Uri.UnescapeDataString(name);
            name = Path.GetFileName(name);
            if (string.IsNullOrEmpty(name)) return null;
            return Path.Combine(CacheFolderPath, name);
        }

        private static bool IsWindowsPath(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (value.StartsWith(@"\\", StringComparison.Ordinal)) return true;
            return value.Length >= 3
                && char.IsLetter(value[0])
                && value[1] == ':'
                && (value[2] == '\\' || value[2] == '/');
        }

        /// <summary>识别网络/本地 PDF、EPUB 文档地址（不含阅读器页面地址）。</summary>
        public static bool TryGetDocumentKind(string source, out DocumentKind kind)
        {
            kind = default;
            if (string.IsNullOrWhiteSpace(source)) return false;

            string path = source;
            if (source.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(source, UriKind.Absolute, out var fileUri)) return false;
                path = fileUri.LocalPath;
            }
            else if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
                     && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                path = uri.AbsolutePath;
            }
            else if (!IsWindowsPath(source))
            {
                // 纯文件名（StorageFile.Name，例如 "book.epub"）也识别；
                // 带协议/路径分隔符但不是文件协议的串（ms-local-stream、ntp.local 等）不识别。
                if (source.Contains("://") || source.Contains("/") || source.Contains("\\"))
                    return false;
            }

            if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                kind = DocumentKind.Pdf;
                return true;
            }
            if (path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
            {
                kind = DocumentKind.Epub;
                return true;
            }
            return false;
        }

        public static bool IsLocalDocumentSource(string source)
        {
            if (!TryGetDocumentKind(source, out _)) return false;
            if (source.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
            if (IsWindowsPath(source)) return true;
            return Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile;
        }

        /// <summary>下载网络文档到 contentcache，文件名带来源哈希，避免不同书互相覆盖。</summary>
        public static async Task<string> DownloadAsync(DocumentKind kind, string url)
        {
            using var client = new Windows.Web.Http.HttpClient();
            var response = await client.GetAsync(new Uri(url));
            response.EnsureSuccessStatusCode();
            var buffer = await response.Content.ReadAsBufferAsync();
            byte[] bytes = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);

            string ext = kind == DocumentKind.Pdf ? ".pdf" : ".epub";
            string fileName = $"{kind.ToString().ToLowerInvariant()}_{Sha12(url)}{ext}";
            Directory.CreateDirectory(CacheFolderPath);
            string filePath = Path.Combine(CacheFolderPath, fileName);
            await File.WriteAllBytesAsync(filePath, bytes);
            Diagnostics.Log($"[文档缓存] {kind} 已缓存: {fileName} ({bytes.Length} 字节)");
            return filePath;
        }

        public static async Task<StorageFile> ResolveLocalFileAsync(string source)
        {
            if (source.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(source);
                return await StorageFile.GetFileFromPathAsync(uri.LocalPath);
            }
            return await StorageFile.GetFileFromPathAsync(source);
        }

        /// <summary>把用户选择的本地文件复制到 contentcache（按原路径哈希命名，批注/进度可复用）。</summary>
        public static async Task<string> ImportLocalFileAsync(StorageFile file)
        {
            if (file == null) return null;
            string ext = Path.GetExtension(file.Name).ToLowerInvariant();
            if (ext != ".pdf" && ext != ".epub") return null;

            string key = string.IsNullOrEmpty(file.Path) ? file.Name : file.Path;
            string fileName = $"local_{Sha12(key)}{ext}";
            Directory.CreateDirectory(CacheFolderPath);
            string destPath = Path.Combine(CacheFolderPath, fileName);

            if (string.Equals(file.Path, destPath, StringComparison.OrdinalIgnoreCase))
                return destPath;

            var folder = await StorageFolder.GetFolderFromPathAsync(CacheFolderPath);
            await file.CopyAsync(folder, fileName, NameCollisionOption.ReplaceExisting);
            Diagnostics.Log($"[文档缓存] 本地文件已导入: {file.Name} -> {fileName}");
            return destPath;
        }

        public static async Task<string> ImportLocalSourceAsync(string source)
        {
            try
            {
                var file = await ResolveLocalFileAsync(source);
                return await ImportLocalFileAsync(file);
            }
            catch (UnauthorizedAccessException)
            {
                // 未授予 broadFileSystemAccess（系统设置→隐私→文件系统）时的兜底：
                // 桌面扩展/全信任环境可直接读；AppContainer 仍会失败，由调用方提示改用文件选择器。
                string path = source.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(source).LocalPath
                    : source;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".pdf" && ext != ".epub") throw;

                Directory.CreateDirectory(CacheFolderPath);
                string fileName = $"local_{Sha12(path)}{ext}";
                string destPath = Path.Combine(CacheFolderPath, fileName);
                File.Copy(path, destPath, true);
                Diagnostics.Log($"[文档缓存] 本地文件已直接复制: {path} -> {fileName}");
                return destPath;
            }
        }

        public static async Task<bool> OpenDocumentAsync(IBrowserTab tab, string source)
        {
            if (tab == null || !TryGetDocumentKind(source, out var kind)) return false;
            try
            {
                string cachePath = IsLocalDocumentSource(source)
                    ? await ImportLocalSourceAsync(source)
                    : await DownloadAsync(kind, source);
                if (string.IsNullOrEmpty(cachePath)) return false;
                return await OpenCachedDocumentAsync(tab, kind, cachePath, source);
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[文档打开] {source} 失败: {ex}");
                return false;
            }
        }

        public static async Task<bool> OpenLocalFileAsync(IBrowserTab tab, StorageFile file)
        {
            if (tab == null || file == null) return false;
            try
            {
                DocumentKind kind;
                string ext = Path.GetExtension(file.Name);
                if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    kind = DocumentKind.Pdf;
                else if (ext.Equals(".epub", StringComparison.OrdinalIgnoreCase))
                    kind = DocumentKind.Epub;
                else
                    return false;

                string cachePath = await ImportLocalFileAsync(file);
                if (string.IsNullOrEmpty(cachePath)) return false;
                string source = string.IsNullOrEmpty(file.Path) ? file.Name : file.Path;
                return await OpenCachedDocumentAsync(tab, kind, cachePath, source);
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[文档打开] 本地文件 {file?.Name} 失败: {ex}");
                return false;
            }
        }

        public static async Task<bool> OpenCachedDocumentAsync(IBrowserTab tab, DocumentKind kind, string cachePath, string sourceUrl)
        {
            if (tab is EdgeHtmlTab edge)
            {
                if (kind != DocumentKind.Epub) return false;
                await edge.NavigateToEpubReaderAsync(cachePath, sourceUrl, Path.GetFileName(cachePath));
                return true;
            }
            if (tab is WebView2Tab wv2)
            {
                await wv2.OpenLocalDocumentAsync(kind, cachePath, sourceUrl);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 解析阅读器页面 URL，取出原始来源与缓存文件名：
        /// - ms-local-stream://.../reader.html?file=book.epub&amp;src=...&amp;cache=...
        /// - https://epubreader.local/reader.html?file=https://contentcache.local/...&amp;src=...
        /// - https://pdfviewer.local/viewer.html?file=https://contentcache.local/...&amp;src=...
        /// </summary>
        public static bool TryParseReaderUrl(string url, out ReaderRestoreTarget target)
        {
            target = null;
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

            string host = uri.Host ?? "";
            bool isEpubReader = url.IndexOf("reader.html", StringComparison.OrdinalIgnoreCase) >= 0
                && (uri.Scheme == "ms-local-stream"
                    || host.EndsWith("epubreader.local", StringComparison.OrdinalIgnoreCase)
                    || uri.Scheme == "ms-appx-web");
            bool isPdfViewer = url.IndexOf("viewer.html", StringComparison.OrdinalIgnoreCase) >= 0
                && host.EndsWith("pdfviewer.local", StringComparison.OrdinalIgnoreCase);
            if (!isEpubReader && !isPdfViewer) return false;

            string src = GetQueryParam(url, "src");
            string file = GetQueryParam(url, "file");
            string cache = GetQueryParam(url, "cache");
            string cacheName = cache;
            if (string.IsNullOrEmpty(cacheName) && !string.IsNullOrEmpty(file))
            {
                if (file.StartsWith("https://contentcache.local/", StringComparison.OrdinalIgnoreCase)
                    || file.StartsWith("ms-appdata:///local/contentcache/", StringComparison.OrdinalIgnoreCase))
                    cacheName = file;
                else if (file.Equals(ReaderStreamResolver.VirtualEpubName, StringComparison.OrdinalIgnoreCase))
                    cacheName = "doc_reader.epub";
            }

            target = new ReaderRestoreTarget
            {
                Kind = isPdfViewer ? DocumentKind.Pdf : DocumentKind.Epub,
                SourceUrl = src,
                CacheFileName = cacheName
            };
            return true;
        }

        /// <summary>恢复阅读器标签：优先用缓存，缓存缺失时按来源重新下载/导入。</summary>
        public static async Task<bool> RestoreReaderAsync(IBrowserTab tab, string readerUrl)
        {
            if (tab == null || !TryParseReaderUrl(readerUrl, out var target)) return false;

            string cachePath = GetCachePath(target.CacheFileName);
            if (!string.IsNullOrEmpty(cachePath) && File.Exists(cachePath))
            {
                if (await OpenCachedDocumentAsync(tab, target.Kind, cachePath, target.SourceUrl))
                    return true;
            }

            if (!string.IsNullOrEmpty(target.SourceUrl) && TryGetDocumentKind(target.SourceUrl, out _))
            {
                if (await OpenDocumentAsync(tab, target.SourceUrl))
                    return true;
            }

            // 兼容旧版 ms-local-stream 阅读器 URL（file=book.epub，没有 cache 参数）
            if (target.Kind == DocumentKind.Epub)
            {
                cachePath = GetCachePath("doc_reader.epub");
                if (File.Exists(cachePath)
                    && await OpenCachedDocumentAsync(tab, DocumentKind.Epub, cachePath, target.SourceUrl))
                    return true;
            }
            return false;
        }

        /// <summary>标签导航统一入口：文档/阅读器 URL 返回 true，普通网址返回 false。</summary>
        public static async Task<bool> TryNavigateAsync(IBrowserTab tab, string url)
        {
            if (tab == null || string.IsNullOrWhiteSpace(url)) return false;

            if (TryParseReaderUrl(url, out _))
            {
                if (await RestoreReaderAsync(tab, url)) return true;
                NotificationService.ShowToast("无法恢复阅读器", "缓存文件与来源地址均不可用");
                await tab.NavigateAsync("about:blank");
                return true;
            }

            if (TryGetDocumentKind(url, out _))
            {
                if (await OpenDocumentAsync(tab, url)) return true;
                NotificationService.ShowToast("无法打开文件", url);
                await tab.NavigateAsync("about:blank");
                return true;
            }
            return false;
        }

        private static string GetQueryParam(string url, string name)
        {
            int q = url.IndexOf('?');
            if (q < 0 || q == url.Length - 1) return null;
            string query = url.Substring(q + 1);
            int hash = query.IndexOf('#');
            if (hash >= 0) query = query.Substring(0, hash);
            foreach (string part in query.Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                if (!part.Substring(0, eq).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                return Uri.UnescapeDataString(part.Substring(eq + 1));
            }
            return null;
        }
    }
}
