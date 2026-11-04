using EdgeRebuild.Metro;
using EdgeRebuild.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EdgeRebuild.Core
{
    /// <summary>
    /// 原生多窗口管理器（原型）。
    ///
    /// 架构：
    ///   1. 优先尝试 CoreApplication.CreateNewView — 每个窗口独立 CoreWindow + 独立 UI 线程。
    ///      这是 UWP 原生多窗口的正途：真实 per-window 激活、真实焦点隔离。
    ///   2. 创建或展示失败时自动回退到 AppWindow 同线程模型（现有可用方案）。
    ///   3. 两种模式统一暴露 NativeWindow 抽象，上层代码无需感知底层差异。
    ///
    /// 与旧 WindowManager 的区别：
    ///   - 旧：直接操作 AppWindow，焦点靠 GotFocus 上报槽位 + XamlRoot 双重判定（近似）
    ///   - 新：原生模式有真实 CoreWindow.Activated；回退模式沿用 AppWindow.Activated
    /// </summary>
    public static class NativeWindowManager
    {
        /// <summary>
        /// 是否优先尝试原生独立视图（CoreApplication.CreateNewView）。
        /// true = 每个新窗口先走独立 CoreWindow + 独立 UI 线程，失败自动回退 AppWindow。
        /// 注意：原生模式下 SlotActivated 会在发起线程触发，订阅方必须自行 marshal 回自己的 UI 线程。
        /// </summary>
        public static bool PreferNativeView = true;

        // ---- 窗口注册表 ----
        private sealed class Entry
        {
            public NativeWindow Window;
            public int Slot;
            public bool ParticipatesInSession;
            public Type PageType;
            public bool IsInPrivate;
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static bool _restoreSpawned;

        // ---- 激活槽位广播 ----
        public static int ActiveSlot { get; private set; }
        public static event Action<int> SlotActivated;

        /// <summary>页面根元素 GotFocus 时调用（AppWindow 回退模式的近似焦点上报）。</summary>
        public static void ReportSlotFocus(int slot)
        {
            if (slot < 0 || slot == ActiveSlot) return;
            ActiveSlot = slot;
            SlotActivated?.Invoke(slot);
        }

        /// <summary>已注册窗口数（诊断用）。</summary>
        public static int WindowCount { get { lock (_entries) return _entries.Count; } }

        // ================================================================
        //  公共入口（与旧 WindowManager API 一致）
        // ================================================================

        public static async Task<bool> OpenNewWindowAsync(EngineType engine, string url)
            => await OpenCoreAsync(engine, url, typeof(MainPage), false, false);

        public static async Task<bool> OpenInPrivateWindowAsync(EngineType engine, string url)
            => await OpenCoreAsync(engine, url, typeof(MainPage), false, true);

        public static async Task<bool> OpenMetroWindowAsync(EngineType engine, string url)
            => await OpenCoreAsync(engine, url, typeof(MetroPage), true, false);

        /// <summary>多窗口 Demo：开一个新的 DemoPage 独立窗口（不写会话）。</summary>
        public static async Task<bool> OpenDemoWindowAsync()
            => await OpenCoreAsync(EngineType.EdgeHtml, "", typeof(DemoPage), false, false);

        // ================================================================
        //  核心开窗流程
        // ================================================================

        private static async Task<bool> OpenCoreAsync(
            EngineType engine, string url, Type pageType, bool isMetro, bool isInPrivate, int restoreSlot = -1)
        {
            try
            {
                Diagnostics.Log($"[NativeWM] 开窗 page={pageType.Name} url={url} restoreSlot={restoreSlot}");

                bool participates = pageType == typeof(MainPage) && !isInPrivate;
                // Demo 窗口同样分配槽位（便于界面区分），但不写会话
                bool needSlot = participates || pageType == typeof(DemoPage);
                int slot = (restoreSlot >= 1) ? restoreSlot : AllocateSlot(needSlot);

                var args = new NewWindowArgs
                {
                    Engine = engine,
                    Url = url,
                    IsMetro = isMetro,
                    IsInPrivate = isInPrivate,
                    SessionSlot = slot,
                    RestoreSlot = restoreSlot
                };

                // ---- 策略 1：原生独立视图（CreateNewView，独立 CoreWindow + 独立 UI 线程）----
                // 曾经的"展示失败"其实是 _viewId 哨兵值 bug：ApplicationView.Id 是负数，
                // 被 `if (_viewId < 0) return false` 拦在 TryShowAsStandaloneAsync 之前。
                // 修好哨兵后重新启用；展示仍带 2.5s 超时，失败自动回退 AppWindow。
                if (PreferNativeView)
                {
                    var nativeWin = await NativeWindow.CreateNativeViewAsync(pageType, args, slot);
                    if (nativeWin != null)
                    {
                        bool shown = await nativeWin.ShowAsync(timeoutMs: 2500);
                        if (shown)
                        {
                            RegisterWindow(nativeWin, slot, participates, pageType, isInPrivate);
                            // 与 AppWindow 路径对齐：开窗即视为激活，否则新窗口
                            // 的 ActiveText 会一直显示上一个槽位
                            ActiveSlot = slot;
                            SlotActivated?.Invoke(slot);
                            Diagnostics.Log($"[NativeWM] ✅ 原生视图窗口成功 slot={slot}");
                            return true;
                        }
                        Diagnostics.Log($"[NativeWM] 原生视图展示失败，回退 AppWindow slot={slot}");
                        // 不 await：视图从未上屏时其线程可能不再泵消息，
                        // Dispatcher.RunAsync 会永久挂起，把后续开窗一起堵死
                        _ = CloseQuietlyAsync(nativeWin);
                    }
                }

                // ---- 策略 2：AppWindow（18362+ 才存在）----
                if (!PlatformCompat.HasAppWindow)
                {
                    Diagnostics.Log("[NativeWM] 当前系统没有 AppWindow（<18362），不进入回退");
                    return false;
                }

                Diagnostics.Log($"[NativeWM] 进入 AppWindow 路径 slot={slot}");
                var fallback = await NativeWindow.CreateAppWindowAsync(pageType, args, slot);
                if (fallback == null)
                {
                    Diagnostics.Log("[NativeWM] ❌ AppWindow 回退也失败");
                    return false;
                }

                RegisterWindow(fallback, slot, participates, pageType, isInPrivate);

                bool shownOk = await fallback.ShowAsync();
                if (shownOk)
                {
                    ActiveSlot = slot;
                    SlotActivated?.Invoke(slot);
                    Diagnostics.Log($"[NativeWM] ✅ AppWindow 回退成功 slot={slot}");
                }
                return shownOk;
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[NativeWM] 开窗异常: {ex}");
                return false;
            }
        }

        // ================================================================
        //  注册与事件挂钩
        // ================================================================

        private static async Task CloseQuietlyAsync(NativeWindow win)
        {
            try { await win.CloseAsync(); }
            catch (Exception ex) { Diagnostics.Log($"[NativeWM] 清理原生视图失败: {ex.Message}"); }
        }

        private static void RegisterWindow(NativeWindow win, int slot, bool participates, Type pageType, bool isInPrivate)
        {
            var entry = new Entry
            {
                Window = win,
                Slot = slot,
                ParticipatesInSession = participates,
                PageType = pageType,
                IsInPrivate = isInPrivate
            };

            // 统一激活事件 → 槽位广播
            win.Activated += w =>
            {
                ActiveSlot = w.Slot;
                SlotActivated?.Invoke(w.Slot);
            };

            // 统一关闭事件 → 清理
            win.Closed += w =>
            {
                Diagnostics.Log($"[NativeWM] 窗口关闭 slot={w.Slot} mode={w.Mode}");
                lock (_entries) _entries.Remove(entry);
                if (participates)
                    _ = SessionManager.RemoveSlotAsync(w.Slot);
                // 页面清理由 Frame 承担
                (w.Frame?.Content as MainPage)?.Shutdown();
                (w.Frame?.Content as MetroPage)?.Shutdown();
                (w.Frame?.Content as DemoPage)?.Shutdown();
                w.Frame = null;
            };

            lock (_entries) _entries.Add(entry);
        }

        // ================================================================
        //  启动恢复
        // ================================================================

        public static async Task RestoreSecondaryWindowsAsync()
        {
            if (_restoreSpawned) return;
            _restoreSpawned = true;
            try
            {
                string eng = await SettingsManager.GetAsync("DefaultEngine") ?? "EdgeHtml";
                var engine = eng == "WebView2" ? EngineType.WebView2 : EngineType.EdgeHtml;
                var slots = await SessionManager.LoadSlotsAsync();
                foreach (var slot in slots)
                {
                    Diagnostics.Log($"[NativeWM] 恢复副窗口 slot={slot}");
                    await OpenCoreAsync(engine, "", typeof(MainPage), false, false, restoreSlot: slot);
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"[NativeWM] 副窗口恢复失败: {ex.Message}");
            }
        }

        // ================================================================
        //  槽位分配（与旧版一致）
        // ================================================================

        private static int AllocateSlot(bool participates)
        {
            if (!participates) return -1;
            lock (_entries)
            {
                int candidate = 1;
                while (_entries.Any(e => e.Slot == candidate)) candidate++;
                return candidate;
            }
        }
    }
}
