using System;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Graphics.Display;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace EdgeRebuild.Services
{
    /// <summary>
    /// 18362+ API 的运行时兼容层（最低支持 17763）。
    ///
    /// 17763（1809）没有 XamlRoot，但每个原生视图有自己的 Window.Current：
    ///   - 原生主窗口/副窗口：Window.Current.Bounds / SizeChanged / Content 就是本窗口作用域
    ///   - ContentDialog / Popup 不设置 XamlRoot 时自动挂到当前视图
    ///   - AppWindow 同线程模式才需要 XamlRoot（该模式本身要求 18362+）
    ///
    /// 所有 XamlRoot 代码都放在独立的 18362+ 辅助方法里并禁止内联：
    /// 17763 的 JIT 不需要解析这些方法的类型，只需执行 17763 分支。
    /// [SupportedOSPlatformGuard] 让 CA1416 能识别 HasXamlRoot / HasAppWindow 守卫，
    /// 调用点无需 #pragma warning disable。
    /// </summary>
    public static class PlatformCompat
    {
        /// <summary>
        /// 调试开关：即使系统支持 18362+，也强制走 17763 兼容路径
        /// （用于在 Win10/11 上验证原生视图回退）。
        ///
        /// 优先级：
        ///   1. 环境变量 EDGEREBUILD_FORCE_LEGACY_WINDOW=1（便于自动化/无 UI 调试）
        ///   2. 设置 → 调试 → “强制 17763 兼容窗口”
        ///
        /// 该值在类型首次使用时读取一次；切换后需重启应用。
        /// </summary>
        public static bool ForceLegacyWindowMode { get; } = ReadForceLegacyWindowMode();

        private static bool ReadForceLegacyWindowMode()
        {
            if (string.Equals(
                    Environment.GetEnvironmentVariable("EDGEREBUILD_FORCE_LEGACY_WINDOW"),
                    "1",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return DebugOptions.ForceLegacyWindowMode;
        }

        [SupportedOSPlatformGuard("windows10.0.18362.0")]
        public static bool HasXamlRoot { get; } =
            !ForceLegacyWindowMode && IsTypePresent("Windows.UI.Xaml.XamlRoot");

        [SupportedOSPlatformGuard("windows10.0.18362.0")]
        public static bool HasAppWindow { get; } =
            !ForceLegacyWindowMode && IsTypePresent("Windows.UI.WindowManagement.AppWindow");

        /// <summary>
        /// 类型探测。显式做 Windows 版本守卫，避免 CA1416 把静态初始化
        /// 当成"任意平台可调用"；失败时一律走 17763 兼容路径。
        /// </summary>
        private static bool IsTypePresent(string typeName)
            => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240)
                && ApiInformation.IsTypePresent(typeName);

        // ================================================================
        //  窗口尺寸 / DPI
        // ================================================================

        /// <summary>本窗口的客户区尺寸（DIP）。</summary>
        public static Size GetWindowSize(UIElement element)
        {
            if (HasXamlRoot)
                return GetXamlRootSize(element);

            var bounds = Window.Current.Bounds;
            return new Size(bounds.Width, bounds.Height);
        }

        [SupportedOSPlatform("windows10.0.18362.0")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Size GetXamlRootSize(UIElement element)
        {
            var root = element.XamlRoot;
            if (root != null) return root.Size;

            var bounds = Window.Current.Bounds;
            return new Size(bounds.Width, bounds.Height);
        }

        /// <summary>本窗口的 rasterization scale；17763 回退到 DisplayInformation。</summary>
        public static double GetRasterizationScale(UIElement element)
        {
            if (HasXamlRoot)
                return GetXamlRootRasterizationScale(element);

            try { return DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel; }
            catch { return 1.0; }
        }

        [SupportedOSPlatform("windows10.0.18362.0")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double GetXamlRootRasterizationScale(UIElement element)
        {
            var root = element.XamlRoot;
            if (root != null) return root.RasterizationScale;

            try { return DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel; }
            catch { return 1.0; }
        }

        // ================================================================
        //  焦点 / 页面 Frame
        // ================================================================

        /// <summary>本窗口的焦点元素；17763 使用当前 CoreWindow 作用域。</summary>
        public static object GetFocusedElement(UIElement element)
        {
            if (HasXamlRoot)
                return GetXamlRootFocusedElement(element);

            return Windows.UI.Xaml.Input.FocusManager.GetFocusedElement();
        }

        [SupportedOSPlatform("windows10.0.18362.0")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object GetXamlRootFocusedElement(UIElement element)
        {
            var root = element.XamlRoot;
            if (root != null)
                return Windows.UI.Xaml.Input.FocusManager.GetFocusedElement(root);

            return Windows.UI.Xaml.Input.FocusManager.GetFocusedElement();
        }

        /// <summary>本窗口的根 Frame；17763 使用 Window.Current.Content。</summary>
        public static Frame GetPageFrame(UIElement element)
        {
            if (HasXamlRoot)
                return GetXamlRootPageFrame(element);

            return Window.Current.Content as Frame;
        }

        [SupportedOSPlatform("windows10.0.18362.0")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Frame GetXamlRootPageFrame(UIElement element)
        {
            var root = element.XamlRoot;
            if (root?.Content is Frame frame) return frame;

            return Window.Current.Content as Frame;
        }

        // ================================================================
        //  窗口尺寸事件
        // ================================================================

        /// <summary>
        /// 订阅本窗口的尺寸变化。18362+ 用 XamlRoot.Changed（含 AppWindow 副窗），
        /// 17763 用 Window.Current.SizeChanged。返回的 IDisposable 负责退订。
        /// </summary>
        public static IDisposable SubscribeSizeChanged(UIElement element, Action onChanged)
        {
            if (HasXamlRoot)
                return SubscribeXamlRootSizeChanged(element, onChanged);

            return SubscribeWindowSizeChanged(onChanged);
        }

        [SupportedOSPlatform("windows10.0.18362.0")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IDisposable SubscribeXamlRootSizeChanged(UIElement element, Action onChanged)
        {
            var root = element.XamlRoot;
            if (root == null) return new ActionDisposable(null);

            TypedEventHandler<XamlRoot, XamlRootChangedEventArgs> handler = (_, __) => onChanged();
            root.Changed += handler;
            return new ActionDisposable(() => { try { root.Changed -= handler; } catch { } });
        }

        private static IDisposable SubscribeWindowSizeChanged(Action onChanged)
        {
            var window = Window.Current;
            if (window == null) return new ActionDisposable(null);

            WindowSizeChangedEventHandler handler = (_, __) => onChanged();
            window.SizeChanged += handler;
            return new ActionDisposable(() => { try { window.SizeChanged -= handler; } catch { } });
        }

        private sealed class ActionDisposable : IDisposable
        {
            private Action? _dispose;
            public ActionDisposable(Action? dispose) { _dispose = dispose; }
            public void Dispose()
            {
                var dispose = _dispose;
                _dispose = null;
                dispose?.Invoke();
            }
        }

        // ================================================================
        //  对话框 / Popup 的 XamlRoot
        // ================================================================

        /// <summary>18362+ 设置 ContentDialog.XamlRoot；17763 留空即自动挂到当前视图。</summary>
        public static void SetDialogRoot(ContentDialog dialog, UIElement element)
        {
            if (!HasXamlRoot || dialog == null || element == null) return;
            SetXamlRootDialog(dialog, element);
        }

        [SupportedOSPlatform("windows10.0.18362.0")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetXamlRootDialog(ContentDialog dialog, UIElement element)
        {
            var root = element.XamlRoot;
            if (root != null) dialog.XamlRoot = root;
        }

        /// <summary>18362+ 设置 Popup.XamlRoot；17763 留空即自动挂到当前视图。</summary>
        public static void SetPopupRoot(Popup popup, UIElement element)
        {
            if (!HasXamlRoot || popup == null || element == null) return;
            SetXamlRootPopup(popup, element);
        }

        [SupportedOSPlatform("windows10.0.18362.0")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetXamlRootPopup(Popup popup, UIElement element)
        {
            var root = element.XamlRoot;
            if (root != null) popup.XamlRoot = root;
        }
    }
}
