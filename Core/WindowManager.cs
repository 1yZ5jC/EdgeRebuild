using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.UI.WindowManagement;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using EdgeRebuild.Metro;
using EdgeRebuild.Services;

namespace EdgeRebuild.Core
{
    public class NewWindowArgs
    {
        public EngineType Engine { get; set; }
        public string Url { get; set; }
        public Windows.UI.WindowManagement.AppWindow AppWindow { get; set; }
        public bool IsMetro { get; set; }
        public bool IsInPrivate { get; set; }

        /// <summary>本窗口的会话槽位（1..n；主窗口恒 0）。</summary>
        public int SessionSlot { get; set; }

        /// <summary>
        /// >=1 时：本窗口是启动恢复流程重建的第 N 槽副窗口，
        /// OnLoaded 从 SessionTabs 读该槽记录，不走 pendingUrl/主页逻辑。
        /// </summary>
        public int RestoreSlot { get; set; } = -1;
    }

    /// <summary>
    /// 多窗口管理（AppWindow 同线程模型，规避 CreateNewView 的 RPC_E_WRONG_THREAD 崩溃）。
    /// 维护窗口注册表：会话槽位分配/回收、激活槽位广播（AppWindow 无 Activated 事件，
    /// 以 Changed.DidZOrderChange 置顶近似激活）、关闭时清理持久化残留。
    /// </summary>
    public static class WindowManager
    {
        private sealed class WindowEntry
        {
            public AppWindow Window;
            public int Slot;
            public bool IsMainPage;   // MainPage 可参与会话；MetroPage 不参与
        }

        private static readonly List<WindowEntry> _windows = new List<WindowEntry>();
        private static bool _restoreSpawned;   // 启动恢复只执行一次

        /// <summary>当前激活窗口的槽位（0=主窗口）。以"槽位内最近获得 XAML 焦点"近似判定。</summary>
        public static int ActiveSlot { get; private set; }

        /// <summary>激活槽位变化（某窗口抢到焦点）。</summary>
        public static event Action<int> SlotActivated;

        /// <summary>
        /// 由各页面在根元素 GotFocus 时调用：AppWindow 多窗口共享主 UI 层且
        /// UWP 契约无 AppWindow.Activated / DidZOrderChange，用焦点到达哪个
        /// 槽位来近似窗口激活，供快捷键守卫（IsFocusedWindow）使用。
        /// </summary>
        public static void ReportSlotFocus(int slot)
        {
            if (slot < 0 || slot == ActiveSlot) return;
            ActiveSlot = slot;
            SlotActivated?.Invoke(slot);
        }

        public static async Task<bool> OpenNewWindowAsync(EngineType engine, string url)
        {
            return await OpenWindowCoreAsync(engine, url, typeof(MainPage), false, false, restoreSlot: -1);
        }

        /// <summary>
        /// 打开 InPrivate 窗口：不写历史、不存会话，新标签页为隐身专属主页。
        /// </summary>
        public static async Task<bool> OpenInPrivateWindowAsync(EngineType engine, string url)
        {
            return await OpenWindowCoreAsync(engine, url, typeof(MainPage), false, true, restoreSlot: -1);
        }

        /// <summary>
        /// 打开 Metro IE 风格窗口（MetroPage，无标签条 + 全屏缩略图网格 + 磁贴主页）。
        /// </summary>
        public static async Task<bool> OpenMetroWindowAsync(EngineType engine, string url)
        {
            return await OpenWindowCoreAsync(engine, url, typeof(MetroPage), true, false, restoreSlot: -1);
        }

        private static async Task<bool> OpenWindowCoreAsync(
            EngineType engine, string url, Type pageType, bool isMetro, bool isInPrivate, int restoreSlot)
        {
            try
            {
                Services.Diagnostics.Log($"[NewWindow] 开始创建窗口 page={pageType.Name} url={url} restoreSlot={restoreSlot}");

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

                // 会话槽位：仅 MainPage 参与（MetroPage ParticipatesInSession=false）；
                // 恢复重建的窗口沿用其原有槽位号
                bool participates = pageType == typeof(MainPage) && !isInPrivate;                var entry = new WindowEntry
                {
                    Window = appWindow,
                    Slot = (restoreSlot >= 1) ? restoreSlot : AllocateSlot(participates),
                    IsMainPage = participates
                };

                var frame = new Frame();
                frame.Navigate(pageType, new NewWindowArgs
                {
                    Engine = engine,
                    Url = url,
                    AppWindow = appWindow,
                    IsMetro = isMetro,
                    IsInPrivate = isInPrivate,
                    SessionSlot = entry.Slot,
                    RestoreSlot = restoreSlot
                });

                ElementCompositionPreview.SetAppWindowContent(appWindow, frame);
                Services.Diagnostics.Log($"[NewWindow] 视图创建完成 uiContext={frame.UIContext} slot={entry.Slot}");

                // 激活广播：UWP AppWindow 契约无 Activated 事件，由页面 GotFocus
                // 调 ReportSlotFocus 完成（见上）

                // 窗口关闭：释放注册表项 + 清理该槽位持久化残留（防幽灵窗口恢复）+
                // 释放 XAML 内容引用
                appWindow.Closed += delegate
                {
                    Services.Diagnostics.Log($"[NewWindow] 窗口已关闭 slot={entry.Slot}");
                    lock (_windows) _windows.Remove(entry);
                    if (participates)
                        _ = SessionManager.RemoveSlotAsync(entry.Slot);
                    frame.Content = null;
                };

                lock (_windows) _windows.Add(entry);

                bool result = await appWindow.TryShowAsync();
                if (result)
                {
                    ActiveSlot = entry.Slot;
                    SlotActivated?.Invoke(entry.Slot);   // 新窗口置顶即激活
                }
                Services.Diagnostics.Log($"[NewWindow] TryShowAsync 结果={result}");
                return result;
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Log($"[NewWindow] 打开新窗口失败: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 启动恢复：由主窗口在自身标签恢复完成后调用一次，把上次退出时的
        /// 副主窗口按槽位重建（RestoreSession 开启时）。
        /// </summary>
        public static async Task RestoreSecondaryWindowsAsync()
        {
            if (_restoreSpawned) return;
            _restoreSpawned = true;
            try
            {
                string eng = await Services.SettingsManager.GetAsync("DefaultEngine") ?? "EdgeHtml";
                var engine = eng == "WebView2" ? EngineType.WebView2 : EngineType.EdgeHtml;
                var slots = await SessionManager.LoadSlotsAsync();
                foreach (var slot in slots)
                {
                    Services.Diagnostics.Log($"[Restore] 重建副窗口 slot={slot}");
                    await OpenWindowCoreAsync(engine, "", typeof(MainPage), false, false, restoreSlot: slot);
                }
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Log($"[Restore] 副窗口重建失败: {ex.Message}");
            }
        }

        private static int AllocateSlot(bool participates)
        {
            if (!participates) return -1;   // Metro/InPrivate 不占会话槽位
            lock (_windows)
            {
                int candidate = 1;
                while (_windows.Any(w => w.Slot == candidate)) candidate++;
                return candidate;
            }
        }
    }
}
