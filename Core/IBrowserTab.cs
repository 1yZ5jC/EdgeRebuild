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

        // 挂起相关
        bool IsSuspended { get; }
        Task SuspendAsync();
        Task ResumeAsync();

        Task NavigateAsync(string url);
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
    }
}