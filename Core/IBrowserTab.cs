using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace EdgeRebuild.Core
{
    // 焦点在网页内容内时键击被引擎吞掉、宿主收不到，
    // 由页面内注入的监听脚本经消息通道回传的快捷键。
    public enum PageShortcutKey
    {
        Find, FindNext, FindPrevious, CloseFind, HighlightMark, WebCapture,
        NewTab, CloseTab, FocusUrl, ToggleFullscreen   // 浏览器级快捷键（Metro/主窗口共用桥）
    }

    /// <summary>
    /// 快捷键 marker（PageKeyBridgeJs 回传的 "ct"/"cw"/… 文本）→ PageShortcutKey。
    /// WebView2 消息解析、EdgeHTML/WebView2 宿主轮询共用。
    /// </summary>
    public static class PageKeyMarkers
    {
        public static PageShortcutKey? Parse(string marker)
        {
            switch (marker)
            {
                case "cf": return PageShortcutKey.Find;
                case "f3":
                case "cg": return PageShortcutKey.FindNext;
                case "sf3":
                case "scg": return PageShortcutKey.FindPrevious;
                case "esc": return PageShortcutKey.CloseFind;
                case "hm": return PageShortcutKey.HighlightMark;
                case "cs": return PageShortcutKey.WebCapture;
                case "ct": return PageShortcutKey.NewTab;              // Ctrl+T
                case "cw": return PageShortcutKey.CloseTab;            // Ctrl+W
                case "cl": return PageShortcutKey.FocusUrl;            // Ctrl+L
                case "f11": return PageShortcutKey.ToggleFullscreen;   // F11
                default: return null;
            }
        }
    }

    public interface IBrowserTab
    {
        string Id { get; }
        FrameworkElement ViewElement { get; }
        bool CanGoBack { get; }
        bool CanGoForward { get; }
        string CurrentUrl { get; }
        EngineType Engine { get; }
        string EngineIcon { get; }
        string Title { get; }
        string FaviconUri { get; }

        bool IsSuspended { get; }
        Task SuspendAsync();
        Task ResumeAsync();

        Task NavigateAsync(string url);
        Task NavigateToHtmlAsync(string html);
        Task<string> ExecuteScriptAsync(string script);
        Task GoBackAsync();
        Task GoForwardAsync();
        Task RefreshAsync();
        Task StopAsync();
        void SetMetroHome();   // 新标签页改用 Metro 磁贴主页（仅 Metro IE 窗口）
        void SetInPrivate();   // InPrivate 模式：新标签页为隐身专用主页，不写历史
        void Dispose();

        event Action<string> TitleChanged;
        event Action<string> UrlChanged;
        event Action<bool> CanGoBackChanged;
        event Action<bool> CanGoForwardChanged;
        event Action<string> FaviconChanged;
        event Action<string> NewWindowRequested;
        event Action<bool> LoadingChanged;   // 新增：加载状态 true=开始，false=结束
        // 注：PageShortcutKeyPressed / PageMarkActionRequested / TtsTextRequested /
        // NtpSearchRequested / ContextMenuRequested 仅 WebView2Tab 经消息通道回传，
        // EdgeHTML 走宿主 marker 轮询（PollPageKeyAsync 等），不进接口——
        // 需要时对 WebView2Tab 条件订阅。
    }
}