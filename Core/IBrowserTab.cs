using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace EdgeRebuild.Core
{
    // 焦点在网页内容内时键击被引擎吞掉、宿主收不到，
    // 由页面内注入的监听脚本经消息通道回传的快捷键。
    public enum PageShortcutKey { Find, FindNext, FindPrevious, CloseFind, HighlightMark, WebCapture }

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
        event Action<TabContextMenuEventArgs> ContextMenuRequested;
        event Action<string> NewWindowRequested;
        event Action<bool> LoadingChanged;   // 新增：加载状态 true=开始，false=结束
        event Action<PageShortcutKey> PageShortcutKeyPressed;   // 页面内快捷键回传
        event Action<string> PageMarkActionRequested;   // 页面标记动作回传（如 "remove:3"）
        event Action<string> TtsTextRequested;   // 大声朗读文本回传（如 EPUB 章节）
        event Action<string> NtpSearchRequested;   // 新标签页搜索词回传
    }
}