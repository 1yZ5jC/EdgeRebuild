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
        void Dispose();

        event Action<string> TitleChanged;
        event Action<string> UrlChanged;
        event Action<bool> CanGoBackChanged;
        event Action<bool> CanGoForwardChanged;
        event Action<string> FaviconChanged;
        event Action<TabContextMenuEventArgs> ContextMenuRequested;
        event Action<string> NewWindowRequested;
        event Action<bool> LoadingChanged;   // 新增：加载状态 true=开始，false=结束
    }
}