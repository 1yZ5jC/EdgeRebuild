using System;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Web;

namespace EdgeRebuild.Core
{
    /// <summary>
    /// EdgeHTML 阅读器本地流解析器。
    /// EdgeHTML 禁止 ms-appx-web 页面通过 XHR 读取 ms-appdata，因此 EPUB 必须与
    /// reader.html 同源加载：用 ms-local-stream 同时提供 Assets/EpubReader 静态资源
    /// 与 LocalState/contentcache 下的 EPUB（固定虚拟文件名 book.epub）。
    /// </summary>
    public sealed partial class ReaderStreamResolver : IUriToStreamResolver
    {
        public const string VirtualEpubName = "book.epub";
        private readonly string _assetDir;
        private readonly string _epubPath;

        public ReaderStreamResolver(string assetDir, string epubPath)
        {
            _assetDir = assetDir;
            _epubPath = epubPath;
        }

        public IAsyncOperation<IInputStream> UriToStreamAsync(Uri uri)
        {
            return ResolveAsync(uri).AsAsyncOperation();
        }

        private async Task<IInputStream> ResolveAsync(Uri uri)
        {
            string path = uri?.PathAndQuery ?? "";
            int q = path.IndexOf('?');
            if (q >= 0) path = path.Substring(0, q);
            path = Uri.UnescapeDataString(path).TrimStart('/');

            // 只允许虚拟目录下的单层文件名，避免路径穿越
            if (path.Length == 0 || path.IndexOf('/') >= 0 || path.IndexOf('\\') >= 0)
                throw new FileNotFoundException(path);

            string filePath = path.Equals(VirtualEpubName, StringComparison.OrdinalIgnoreCase)
                ? _epubPath
                : Path.Combine(_assetDir, path);

            var file = await StorageFile.GetFileFromPathAsync(filePath);
            var stream = await file.OpenAsync(FileAccessMode.Read);
            return stream.GetInputStreamAt(0);
        }
    }
}
