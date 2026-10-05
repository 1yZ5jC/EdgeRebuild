using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using EdgeRebuild.Services;

namespace EdgeRebuild.Core
{
    /// <summary>
    /// 原生多窗口原型：一个独立的窗口实例。
    /// 优先使用 CoreApplication.CreateNewView（独立 CoreWindow + 独立 UI 线程 = 真正的原生多窗口），
    /// 创建失败时回退到 AppWindow 同线程模型。
    ///
    /// 每个 NativeWindow 拥有：
    ///   - 独立 CoreWindow（真实 per-window 激活事件）
    ///   - 独立 XamlRoot（真实焦点隔离）
    ///   - 独立 UI 线程（真·多线程）
    /// </summary>
    public sealed class NativeWindow : IDisposable
    {
        public enum WindowMode { NativeView, AppWindow }

        // ---- 属性 ----
        public WindowMode Mode { get; private set; }
        public int Slot { get; internal set; } = -1;
        public bool IsActive { get; private set; }
        public bool IsClosed { get; private set; }
        public Frame Frame { get; internal set; }
        public string Title { get; private set; } = "EdgeRebuild";

        /// <summary>独立视图模式：CoreApplicationView 句柄。</summary>
        public CoreApplicationView CoreView { get; private set; }

        /// <summary>AppWindow 回退模式：AppWindow 句柄。</summary>
        public Windows.UI.WindowManagement.AppWindow AppWindowRef { get; private set; }

        // ---- 事件 ----
        public event Action<NativeWindow> Activated;
        public event Action<NativeWindow> Deactivated;
        public event Action<NativeWindow> Closed;

        // ---- 内部 ----
        private int _viewId = -1;

        private NativeWindow(WindowMode mode) { Mode = mode; }

        // ================================================================
        //  创建：原生独立视图
        // ================================================================

        public static async Task<NativeWindow> CreateNativeViewAsync(Type pageType, object navigationArg, int slot)
        {
            var win = new NativeWindow(WindowMode.NativeView) { Slot = slot };
            try
            {
                Diagnostics.Log($"[NativeWindow] CreateNewView slot={slot}");

                var view = CoreApplication.CreateNewView();
                win.CoreView = view;

                var tcs = new TaskCompletionSource<bool>();
                await view.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        win.Frame = new Frame();
                        win.Frame.Navigate(pageType, navigationArg);
                        Window.Current.Content = win.Frame;
                        Window.Current.Activate();

                        // 原生 per-window 激活事件
                        var cw = Window.Current.CoreWindow;
                        cw.Activated += (s, e) =>
                        {
                            if (e.WindowActivationState == CoreWindowActivationState.Deactivated)
                            {
                                win.IsActive = false;
                                win.Deactivated?.Invoke(win);
                            }
                            else
                            {
                                win.IsActive = true;
                                win.Activated?.Invoke(win);
                            }
                        };

                        win._viewId = ApplicationView.GetForCurrentView().Id;
                        Diagnostics.Log($"[NativeWindow] 视图线程就绪 viewId={win._viewId} slot={slot}");
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Log($"[NativeWindow] 视图线程构建失败: {ex}");
                        tcs.SetResult(false);
                    }
                });

                bool built = await tcs.Task;
                if (!built) return null;
                return win;
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[NativeWindow] CreateNativeView 失败: {ex.Message}");
                return null;
            }
        }

        // ================================================================
        //  创建：AppWindow 回退
        // ================================================================

        public static async Task<NativeWindow> CreateAppWindowAsync(Type pageType, object navigationArg, int slot)
        {
            var win = new NativeWindow(WindowMode.AppWindow) { Slot = slot };
            try
            {
                var appWindow = await Windows.UI.WindowManagement.AppWindow.TryCreateAsync();
                if (appWindow == null)
                {
                    Diagnostics.Log("[NativeWindow] AppWindow.TryCreateAsync 返回 null");
                    return null;
                }
                win.AppWindowRef = appWindow;

                // 必须在 Navigate 之前设置：MainPage.OnNavigatedTo 读取 args.AppWindow
                if (navigationArg is NewWindowArgs nwa)
                    nwa.AppWindow = appWindow;

                win.Frame = new Frame();
                win.Frame.Navigate(pageType, navigationArg);
                Windows.UI.Xaml.Hosting.ElementCompositionPreview.SetAppWindowContent(appWindow, win.Frame);

                // UWP AppWindow 没有 Activated 事件（那是 WinUI API）；
                // 回退模式的焦点上报仍由页面 GotFocus → ReportSlotFocus 承担。
                appWindow.Closed += (s, e) =>
                {
                    win.IsClosed = true;
                    win.Closed?.Invoke(win);
                };

                Diagnostics.Log($"[NativeWindow] AppWindow 就绪 slot={slot}");
                return win;
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[NativeWindow] CreateAppWindow 失败: {ex.Message}");
                return null;
            }
        }

        // ================================================================
        //  显示
        // ================================================================

        public async Task<bool> ShowAsync(int timeoutMs = 3000)
        {
            if (IsClosed) return false;

            switch (Mode)
            {
                case WindowMode.NativeView:
                    return await ShowNativeViewAsync(timeoutMs);
                case WindowMode.AppWindow:
                    try { return await AppWindowRef.TryShowAsync(); }
                    catch (Exception ex)
                    {
                        Diagnostics.Log($"[NativeWindow] AppWindow.TryShowAsync 失败: {ex.Message}");
                        return false;
                    }
                default:
                    return false;
            }
        }

        private async Task<bool> ShowNativeViewAsync(int timeoutMs)
        {
            if (_viewId < 0) return false;

            // 策略 1：TryShowAsStandaloneAsync（独立窗口展示）
            Diagnostics.Log($"[NativeWindow] 尝试 TryShowAsStandaloneAsync viewId={_viewId}");
            if (await RunWithTimeout(
                ApplicationViewSwitcher.TryShowAsStandaloneAsync(_viewId).AsTask(), timeoutMs))
            {
                Diagnostics.Log("[NativeWindow] ✅ TryShowAsStandaloneAsync 成功");
                return true;
            }

            // 策略 2：SwitchAsync（切换前台视图；需两个 viewId）
            Diagnostics.Log($"[NativeWindow] 尝试 SwitchAsync viewId={_viewId}");
            if (await RunWithTimeout(
                ApplicationViewSwitcher.SwitchAsync(_viewId, _viewId).AsTask(), timeoutMs))
            {
                Diagnostics.Log("[NativeWindow] ✅ SwitchAsync 成功");
                return true;
            }

            Diagnostics.Log("[NativeWindow] ❌ 所有展示策略均失败");
            return false;
        }

        private static async Task<bool> RunWithTimeout(Task task, int timeoutMs)
        {
            try
            {
                var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
                if (completed == task)
                {
                    await task;
                    if (task is Task<bool> tb) return tb.Result;
                    return true;
                }
                Diagnostics.Log($"[NativeWindow] 操作超时 ({timeoutMs}ms)");
                return false;
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[NativeWindow] 操作异常: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> RunWithTimeout(Task<bool> task, int timeoutMs)
        {
            try
            {
                var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
                if (completed == task) return await task;
                Diagnostics.Log($"[NativeWindow] 操作超时 ({timeoutMs}ms)");
                return false;
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[NativeWindow] 操作异常: {ex.Message}");
                return false;
            }
        }

        // ================================================================
        //  标题 / 关闭
        // ================================================================

        public void SetTitle(string title)
        {
            Title = title;
            try
            {
                if (Mode == WindowMode.AppWindow)
                    AppWindowRef.Title = title;
                else if (_viewId >= 0)
                    ApplicationView.GetForCurrentView().Title = title;
            }
            catch { }
        }

        public async Task CloseAsync()
        {
            if (IsClosed) return;
            IsClosed = true;

            try
            {
                if (Mode == WindowMode.NativeView && CoreView != null)
                {
                    // 独立视图：在视图线程上关闭 CoreWindow
                    await CoreView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        try { Window.Current.CoreWindow?.Close(); } catch { }
                    });
                }
                // AppWindow 回退模式：UWP AppWindow 无 Close() 方法；
                // 窗口由系统（标题栏 X）或 Frame 卸载关闭。
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[NativeWindow] CloseAsync 异常: {ex.Message}");
            }

            Closed?.Invoke(this);
            Dispose();
        }

        public void Dispose()
        {
            Frame = null;
            CoreView = null;
            AppWindowRef = null;
        }
    }
}
