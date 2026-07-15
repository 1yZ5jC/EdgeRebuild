using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace EdgeRebuild.Core
{
    public interface IBrowserTab
    {
        // 唯一标识
        string Id { get; }

        // 获取用于显示的可视元素（WebView 控件）
        FrameworkElement ViewElement { get; }

        // 基础导航操作
        void Navigate(string url);
        void GoBack();
        void GoForward();
        void Refresh();
        void Stop();

        // 状态查询
        bool CanGoBack { get; }
        bool CanGoForward { get; }
        string CurrentUrl { get; }

        // 事件
        event Action<string> TitleChanged;
        event Action<string> UrlChanged;
        event Action<bool> CanGoBackChanged;
        event Action<bool> CanGoForwardChanged;

        // 清理资源
        void Dispose();
    }
}
