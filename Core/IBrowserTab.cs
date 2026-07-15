using System;
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

        void Navigate(string url);
        void GoBack();
        void GoForward();
        void Refresh();
        void Stop();
        void Dispose();

        event Action<string> TitleChanged;
        event Action<string> UrlChanged;
        event Action<bool> CanGoBackChanged;
        event Action<bool> CanGoForwardChanged;
        event Action<string> FaviconChanged;
    }
}