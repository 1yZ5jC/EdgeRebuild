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
        /// maxPixelWidth > 0 时把结果缩到指定宽度，供标签预览等小尺寸用途，避免全尺寸位图占用内存。
        /// </summary>
        public static async Task<byte[]> CapturePagePngAsync(IBrowserTab tab, int maxPixelWidth = 0)
        {
            try
            {
                if (tab is WebView2Tab wv2 && wv2.CoreWebView2 != null)
                {
                    var ras = new InMemoryRandomAccessStream();
                    await wv2.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ras);
                    using var ms = new MemoryStream();
                    await ras.AsStreamForRead().CopyToAsync(ms);
                    byte[] png = ms.ToArray();
                    return maxPixelWidth > 0 ? await ResizePngAsync(png, maxPixelWidth) : png;
                }

                if (tab is EdgeHtmlTab edge && edge.ViewElement != null)
                {
                    // EdgeHTML WebView 优先用控件自带的预览捕获；RenderTargetBitmap 对
                    // WebView 气泡区内容并不可靠，仅作回退。
                    // 预览缩略图（maxPixelWidth>0）直接走 RenderTargetBitmap 缩放渲染；
                    // EdgeHTML 的 CapturePreviewToStreamAsync 在后台标签上会等待数秒后失败，
                    // 逐个标签串行等待会明显卡住 UI。
                    if (maxPixelWidth <= 0 && edge.ViewElement is Windows.UI.Xaml.Controls.WebView webView)
                    {
                        try
                        {
                            var preview = new InMemoryRandomAccessStream();
                            await webView.CapturePreviewToStreamAsync(preview);
                            if (preview.Size > 0)
                            {
                                preview.Seek(0);
                                using var previewMs = new MemoryStream();
                                await preview.AsStreamForRead().CopyToAsync(previewMs);
                                byte[] png = previewMs.ToArray();
                                return maxPixelWidth > 0 ? await ResizePngAsync(png, maxPixelWidth) : png;
                            }
                        }
                        catch (Exception ex)
                        {
                            Diagnostics.Log($"[网页捕获] WebView 预览捕获失败，回退 RenderTargetBitmap: {ex.Message}");
                        }
                    }

                    var rtb = new Windows.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                    double scale = 1.0;
                    try { scale = PlatformCompat.GetRasterizationScale(edge.ViewElement); } catch { }
                    int pixelWidth = (int)(edge.ViewElement.ActualWidth * scale);
                    int pixelHeight = (int)(edge.ViewElement.ActualHeight * scale);
                    if (maxPixelWidth > 0 && pixelWidth > maxPixelWidth)
                    {
                        pixelHeight = Math.Max(1, (int)Math.Round(pixelHeight * (double)maxPixelWidth / pixelWidth));
                        pixelWidth = maxPixelWidth;
                    }
                    await rtb.RenderAsync(edge.ViewElement, pixelWidth, pixelHeight);
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

        /// <summary>把 PNG 等比缩到指定最大宽度；失败时原样返回。</summary>
        private static async Task<byte[]> ResizePngAsync(byte[] png, int maxWidth)
        {
            if (png == null || png.Length == 0 || maxWidth <= 0) return png;
            try
            {
                using var input = new InMemoryRandomAccessStream();
                await input.WriteAsync(png.AsBuffer());
                input.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(input);
                if (decoder.PixelWidth <= maxWidth) return png;

                uint width = (uint)maxWidth;
                uint height = Math.Max(1, (uint)Math.Round(
                    decoder.PixelHeight * (double)maxWidth / decoder.PixelWidth));

                var transform = new BitmapTransform
                {
                    ScaledWidth = width,
                    ScaledHeight = height,
                    InterpolationMode = BitmapInterpolationMode.Fant
                };
                var pixels = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                var output = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    width, height, 96, 96,
                    pixels.DetachPixelData());
                await encoder.FlushAsync();

                using var ms = new MemoryStream();
                await output.AsStreamForRead().CopyToAsync(ms);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[网页捕获] 缩放失败，使用原图: {ex.Message}");
                return png;
            }
        }
    }
}
