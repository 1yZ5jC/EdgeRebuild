using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace EdgeRebuild.Core
{
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

        event Action<string>? TitleChanged;
        event Action<string>? UrlChanged;
        event Action<bool>? CanGoBackChanged;
        event Action<bool>? CanGoForwardChanged;
        event Action<string>? FaviconChanged;
        event Action<string>? NewWindowRequested;
        event Action<bool>? LoadingChanged;   // 新增：加载状态 true=开始，false=结束
        // 注：PageShortcutKeyPressed / PageMarkActionRequested / TtsTextRequested /
        // NtpSearchRequested / ContextMenuRequested 仅 WebView2Tab 经消息通道回传，
        // EdgeHTML 走宿主 marker 轮询（PollPageKeyAsync 等），不进接口——
        // 需要时对 WebView2Tab 条件订阅。
    }
}