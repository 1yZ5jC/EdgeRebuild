using EdgeRebuild.Core;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace EdgeRebuild.Services
{
    /// <summary>
    /// 网页截图：WebView2 用 CapturePreviewAsync（可靠），EdgeHTML 用 RenderTargetBitmap。
    /// </summary>
    public static class WebCaptureService
    {
        /// <summary>
        /// 捕获当前可见区域为 PNG 字节。失败返回 null。
        /// </summary>
        public static async Task<byte[]> CapturePagePngAsync(IBrowserTab tab)
        {
            try
            {
                if (tab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                {
                    var ras = new InMemoryRandomAccessStream();
                    await wv2.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ras);
                    using var ms = new MemoryStream();
                    await ras.AsStreamForRead().CopyToAsync(ms);
                    return ms.ToArray();
                }

                if (tab is EdgeHtmlTab edge && edge.ViewElement != null)
                {
                    var rtb = new Windows.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                    double scale = 1.0;
                    try { scale = PlatformCompat.GetRasterizationScale(edge.ViewElement); } catch { }
                    await rtb.RenderAsync(edge.ViewElement,
                        (int)(edge.ViewElement.ActualWidth * scale),
                        (int)(edge.ViewElement.ActualHeight * scale));
                    var pixels = await rtb.GetPixelsAsync();

                    var ras = new InMemoryRandomAccessStream();
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras);
                    encoder.SetPixelData(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Straight,
                        (uint)rtb.PixelWidth,
                        (uint)rtb.PixelHeight,
                        96, 96,
                        pixels.ToArray());
                    await encoder.FlushAsync();

                    using var ms = new MemoryStream();
                    await ras.AsStreamForRead().CopyToAsync(ms);
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[网页捕获] 截图异常: {ex.Message}");
            }
            return null;
        }
    }
}
