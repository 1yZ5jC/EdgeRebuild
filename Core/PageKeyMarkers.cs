namespace EdgeRebuild.Core
{
    // 焦点在网页内容内时键击被引擎吞掉、宿主收不到，
    // 由页面内注入的监听脚本经消息通道回传的快捷键。
    public enum PageShortcutKey
    {
        Find, FindNext, FindPrevious, CloseFind, HighlightMark, WebCapture,
        NewTab, CloseTab, FocusUrl, ToggleFullscreen, QuickSwitch,
        ReaderMode,       // Ctrl+Shift+R（Metro/主窗口共用桥）
        RestoreClosedTab, // Ctrl+Shift+T（Metro/主窗口共用桥）
        SwipeBack,        // 页面内触摸右滑（后退）
        SwipeForward      // 页面内触摸左滑（前进）
    }

    /// <summary>
    /// 快捷键 marker（PageKeyBridgeJs 回传的 "ct"/"cw"/… 文本）→ PageShortcutKey。
    /// WebView2 消息解析、EdgeHTML/WebView2 宿主轮询共用。
    /// 本文件不依赖 WinRT，可被单元测试项目直接链接。
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
                case "qa": return PageShortcutKey.QuickSwitch;          // Ctrl+Shift+A
                case "cr": return PageShortcutKey.ReaderMode;           // Ctrl+Shift+R
                case "cst": return PageShortcutKey.RestoreClosedTab;    // Ctrl+Shift+T
                case "swb": return PageShortcutKey.SwipeBack;           // 触摸右滑
                case "swf": return PageShortcutKey.SwipeForward;        // 触摸左滑
                case "f11": return PageShortcutKey.ToggleFullscreen;   // F11
                default: return null;
            }
        }
    }
}
