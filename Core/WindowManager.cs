using System;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.UI.WindowManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using EdgeRebuild.Metro;

namespace EdgeRebuild.Core
{
    public class NewWindowArgs
    {
        public EngineType Engine { get; set; }
        public string Url { get; set; }
        public Windows.UI.WindowManagement.AppWindow AppWindow { get; set; }
        public bool IsMetro { get; set; }
    }

    /// <summary>
    /// 多窗口：使用 AppWindow 实现，所有窗口共享主 UI 线程，
    /// 彻底避免 CreateNewView 每窗口独立线程导致的 RPC_E_WRONG_THREAD 崩溃。
    /// </summary>
    public static class WindowManager
    {
        public static async Task<bool> OpenNewWindowAsync(EngineType engine, string url)
        {
            return await OpenWindowCoreAsync(engine, url, typeof(MainPage), false);
        }

        /// <summary>
        /// 打开 Metro IE 风格窗口（MetroPage，无标签条 + 全屏缩略图网格 + 磁贴主页）。
        /// </summary>
        public static async Task<bool> OpenMetroWindowAsync(EngineType engine, string url)
        {
            return await OpenWindowCoreAsync(engine, url, typeof(MetroPage), true);
        }

        private static async Task<bool> OpenWindowCoreAsync(EngineType engine, string url, Type pageType, bool isMetro)
        {
            try
            {
                Services.Diagnostics.Log($"[NewWindow] 开始创建窗口 page={pageType.Name} url={url}");

                if (!ApiInformation.IsTypePresent("Windows.UI.WindowManagement.AppWindow"))
                {
                    Services.Diagnostics.Log("[NewWindow] 当前系统不支持 AppWindow（需要 Windows 10 1903+）");
                    return false;
                }

                var appWindow = await AppWindow.TryCreateAsync();
                if (appWindow == null)
                {
                    Services.Diagnostics.Log("[NewWindow] AppWindow.TryCreateAsync 返回 null");
                    return false;
                }

                var frame = new Frame();
                frame.Navigate(pageType, new NewWindowArgs { Engine = engine, Url = url, AppWindow = appWindow, IsMetro = isMetro });

                ElementCompositionPreview.SetAppWindowContent(appWindow, frame);
                Services.Diagnostics.Log($"[NewWindow] 视图创建完成 uiContext={frame.UIContext}");

                // 窗口关闭时释放 XAML 内容与引用
                appWindow.Closed += delegate
                {
                    Services.Diagnostics.Log("[NewWindow] 窗口已关闭");
                    frame.Content = null;
                };

                bool result = await appWindow.TryShowAsync();
                Services.Diagnostics.Log($"[NewWindow] TryShowAsync 结果={result}");
                return result;
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Log($"[NewWindow] 打开新窗口失败: {ex}");
                return false;
            }
        }
    }
}
