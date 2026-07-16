using System;
using Windows.Foundation;

namespace EdgeRebuild.Core
{
    public enum ContextMenuType
    {
        Page,
        Link,
        Image,
        Selection
    }

    public class TabContextMenuEventArgs : EventArgs
    {
        public ContextMenuType MenuType { get; set; }
        public string LinkUrl { get; set; }
        public string ImageUrl { get; set; }
        public Point Location { get; set; }
        public bool CanGoBack { get; set; }
        public bool CanGoForward { get; set; }

        // 文本操作相关
        public bool HasSelection { get; set; } = false;
        public string SelectionText { get; set; } = "";
        public bool IsEditable { get; set; } = false;
    }
}