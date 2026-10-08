using System;
using System.Threading.Tasks;

namespace EdgeRebuild.Core
{
    public class NewWindowArgs
    {
        public EngineType Engine { get; set; }
        public string Url { get; set; }
        public Windows.UI.WindowManagement.AppWindow AppWindow { get; set; }
        public bool IsMetro { get; set; }
        public bool IsInPrivate { get; set; }

        /// <summary>
        /// 窗口呈现方案（由 NativeWindow.Create*Async 在 Navigate 之前写入，供页面显示在调试条）：
        /// "NativeView" = CoreApplication.CreateNewView 独立 UI 线程；
        /// "AppWindow"  = AppWindow + ElementCompositionPreview 同线程；
        /// 主窗口不走 NewWindowArgs，保持 null → 页面显示"主窗口"。
        /// </summary>
        public string Presentation { get; set; }

        /// <summary>本窗口的会话槽位（1..n；主窗口恒 0）。</summary>
        public int SessionSlot { get; set; }

        /// <summary>
        /// >=1 时：本窗口是启动恢复流程重建的第 N 槽副窗口，
        /// OnLoaded 从 SessionTabs 读该槽记录，不走 pendingUrl/主页逻辑。
        /// </summary>
        public int RestoreSlot { get; set; } = -1;
    }

    /// <summary>
    /// 多窗口管理（原生多窗口原型）。
    ///
    /// 重构：公共 API 与旧版完全一致，内部委托给 NativeWindowManager。
    /// NativeWindowManager 优先尝试 CoreApplication.CreateNewView（独立 CoreWindow
    /// + 独立 UI 线程 = 真正的原生多窗口），失败时回退到 AppWindow 同线程模型。
    /// </summary>
    public static class WindowManager
    {
        /// <summary>激活槽位变化广播。</summary>
        public static int ActiveSlot => NativeWindowManager.ActiveSlot;
        public static event Action<int> SlotActivated
        {
            add => NativeWindowManager.SlotActivated += value;
            remove => NativeWindowManager.SlotActivated -= value;
        }

        /// <summary>页面根元素 GotFocus 时调用（AppWindow 回退模式的近似焦点上报）。</summary>
        public static void ReportSlotFocus(int slot) => NativeWindowManager.ReportSlotFocus(slot);

        public static Task<bool> OpenNewWindowAsync(EngineType engine, string url)
            => NativeWindowManager.OpenNewWindowAsync(engine, url);

        public static Task<bool> OpenInPrivateWindowAsync(EngineType engine, string url)
            => NativeWindowManager.OpenInPrivateWindowAsync(engine, url);

        public static Task<bool> OpenMetroWindowAsync(EngineType engine, string url)
            => NativeWindowManager.OpenMetroWindowAsync(engine, url);

        /// <summary>多窗口 Demo：开一个新的 DemoPage 独立窗口。</summary>
        public static Task<bool> OpenDemoWindowAsync()
            => NativeWindowManager.OpenDemoWindowAsync();

        /// <summary>已注册的副窗口数（Demo 界面诊断用）。</summary>
        public static int WindowCount => NativeWindowManager.WindowCount;

        public static Task RestoreSecondaryWindowsAsync()
            => NativeWindowManager.RestoreSecondaryWindowsAsync();
    }
}
