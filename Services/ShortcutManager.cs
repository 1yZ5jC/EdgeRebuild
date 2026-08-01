using System;
using System.Collections.Generic;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace EdgeRebuild.Services
{
    public static class ShortcutManager
    {
        [Flags]
        public enum VirtualKeyModifiers
        {
            None = 0,
            Control = 1,
            Shift = 2,
            Alt = 4,
            ControlShift = Control | Shift,
            ControlAlt = Control | Alt
        }

        private static readonly Dictionary<(VirtualKeyModifiers modifiers, VirtualKey key), Action> _shortcuts = new();

        static ShortcutManager()
        {
            // 注册默认快捷键
            Register(VirtualKeyModifiers.Control, VirtualKey.T, () => OnNewTab?.Invoke());
            Register(VirtualKeyModifiers.Control, VirtualKey.W, () => OnCloseTab?.Invoke());
            Register(VirtualKeyModifiers.ControlShift, VirtualKey.T, () => OnReopenTab?.Invoke());
            Register(VirtualKeyModifiers.Control, VirtualKey.L, () => OnFocusAddressBar?.Invoke());
            Register(VirtualKeyModifiers.Control, VirtualKey.R, () => OnRefresh?.Invoke());
            Register(VirtualKeyModifiers.Control, VirtualKey.D, () => OnAddFavorite?.Invoke());
            Register(VirtualKeyModifiers.Control, VirtualKey.H, () => OnShowHistory?.Invoke());
            Register(VirtualKeyModifiers.Control, VirtualKey.J, () => OnShowDownloads?.Invoke());
            Register(VirtualKeyModifiers.Control, VirtualKey.F, () => OnFind?.Invoke());
            Register(VirtualKeyModifiers.Control, VirtualKey.P, () => OnPrint?.Invoke());
            Register(VirtualKeyModifiers.Control, VirtualKey.Tab, () => OnNextTab?.Invoke());
            Register(VirtualKeyModifiers.ControlShift, VirtualKey.Tab, () => OnPreviousTab?.Invoke());
            Register(VirtualKeyModifiers.Control, VirtualKey.Number1, () => OnSwitchToTab?.Invoke(0));
            Register(VirtualKeyModifiers.Control, VirtualKey.Number2, () => OnSwitchToTab?.Invoke(1));
            Register(VirtualKeyModifiers.Control, VirtualKey.Number3, () => OnSwitchToTab?.Invoke(2));
            Register(VirtualKeyModifiers.Control, VirtualKey.Number4, () => OnSwitchToTab?.Invoke(3));
            Register(VirtualKeyModifiers.Control, VirtualKey.Number5, () => OnSwitchToTab?.Invoke(4));
            Register(VirtualKeyModifiers.Control, VirtualKey.Number6, () => OnSwitchToTab?.Invoke(5));
            Register(VirtualKeyModifiers.Control, VirtualKey.Number7, () => OnSwitchToTab?.Invoke(6));
            Register(VirtualKeyModifiers.Control, VirtualKey.Number8, () => OnSwitchToTab?.Invoke(7));
            Register(VirtualKeyModifiers.Control, VirtualKey.Number9, () => OnSwitchToTab?.Invoke(8));
        }

        public static event Action OnNewTab;
        public static event Action OnCloseTab;
        public static event Action OnReopenTab;
        public static event Action OnFocusAddressBar;
        public static event Action OnRefresh;
        public static event Action OnAddFavorite;
        public static event Action OnShowHistory;
        public static event Action OnShowDownloads;
        public static event Action OnFind;
        public static event Action OnPrint;
        public static event Action OnNextTab;
        public static event Action OnPreviousTab;
        public static event Action<int> OnSwitchToTab;

        public static void Register(VirtualKeyModifiers modifiers, VirtualKey key, Action action)
        {
            var combined = (modifiers, key);
            if (_shortcuts.ContainsKey(combined))
                _shortcuts[combined] = action;
            else
                _shortcuts.Add(combined, action);
        }

        private static int _enableCount;

        public static void Enable()
        {
            _enableCount++;
            if (_enableCount == 1)
                Window.Current.CoreWindow.KeyDown += OnKeyDown;
        }

        public static void Disable()
        {
            _enableCount = Math.Max(0, _enableCount - 1);
            if (_enableCount == 0)
                Window.Current.CoreWindow.KeyDown -= OnKeyDown;
        }

        private static void OnKeyDown(CoreWindow sender, KeyEventArgs args)
        {
            args.Handled = TryHandle(args.VirtualKey);
        }

        /// <summary>
        /// 处理一个按键：匹配修饰键+快捷键表并触发事件，返回是否已处理。
        /// 主窗口由 CoreWindow.KeyDown 调用；AppWindow 子窗口的键盘输入不经过
        /// CoreWindow，由各窗口 XAML 根的 KeyDown 调用本方法。
        /// </summary>
        public static bool TryHandle(VirtualKey key)
        {
            var modifiers = GetModifiers();

            // 数字键 Ctrl+1~9 单独处理
            if (modifiers.HasFlag(VirtualKeyModifiers.Control) && key >= VirtualKey.Number1 && key <= VirtualKey.Number9)
            {
                if (_shortcuts.TryGetValue((VirtualKeyModifiers.Control, key), out var action))
                    action?.Invoke();
                return true;
            }

            var combined = (modifiers, key);
            if (_shortcuts.TryGetValue(combined, out var matchedAction))
            {
                matchedAction?.Invoke();
                return true;
            }

            return false;
        }

        private static VirtualKeyModifiers GetModifiers()
        {
            var modifiers = VirtualKeyModifiers.None;
            var ctrlState = CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Control);
            var shiftState = CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Shift);
            var altState = CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Menu);

            if (ctrlState == CoreVirtualKeyStates.Down) modifiers |= VirtualKeyModifiers.Control;
            if (shiftState == CoreVirtualKeyStates.Down) modifiers |= VirtualKeyModifiers.Shift;
            if (altState == CoreVirtualKeyStates.Down) modifiers |= VirtualKeyModifiers.Alt;

            return modifiers;
        }
    }
}