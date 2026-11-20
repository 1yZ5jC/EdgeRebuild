using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Metro
{
    /// <summary>
    /// 矩形开关的视觉同步。1809 上自定义 ToggleButton 模板的 CheckStates 视觉状态
    /// 在部分场景不生效（文字变了，但轨道颜色和滑块不动），因此不再依赖 VSM，
    /// 在开/关状态变化后直接改模板内的 Track / Knob。
    /// </summary>
    internal static class MetroSwitchVisuals
    {
        private static readonly Color OnColor = Color.FromArgb(0xFF, 0x00, 0x78, 0xD7);
        private static readonly Color OffColor = Color.FromArgb(0xFF, 0xC8, 0xC8, 0xC8);

        public static void Apply(ToggleButton? toggle)
        {
            if (toggle == null) return;

            // Loaded 之前模板可能还没展开；ApplyTemplate 后再找不到就静默跳过，
            // 后续 Loaded 事件会再同步一次。
            toggle.ApplyTemplate();
            if (VisualTreeHelper.GetChildrenCount(toggle) == 0) return;

            var root = VisualTreeHelper.GetChild(toggle, 0);
            var track = FindByName(root, "Track") as Border;
            var knob = FindByName(root, "Knob") as Border;

            bool on = toggle.IsChecked == true;

            if (track != null)
            {
                var brush = new SolidColorBrush(on ? OnColor : OffColor);
                track.Background = brush;
                track.BorderBrush = brush;
            }

            if (knob?.RenderTransform is TranslateTransform transform)
                transform.X = on ? 20 : 0;
        }

        private static DependencyObject? FindByName(DependencyObject? root, string name)
        {
            if (root == null) return null;
            if (root is FrameworkElement element && element.Name == name)
                return root;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindByName(VisualTreeHelper.GetChild(root, i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
