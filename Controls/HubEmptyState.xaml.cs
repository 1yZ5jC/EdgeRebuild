using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace EdgeRebuild.Controls
{
    /// <summary>
    /// Hub 分区的可复用空状态：线框图标 + 标题 + 一句说明 + 一个主行动。
    /// 只给一个行动，避免把空状态做成第二个工具栏。
    /// </summary>
    public sealed partial class HubEmptyState : UserControl
    {
        public event EventHandler ActionInvoked;

        public HubEmptyState()
        {
            this.InitializeComponent();
            this.Loaded += (_, __) => ApplyVisuals();
        }

        private string _glyph = "\uE7C3";
        public string Glyph
        {
            get => _glyph;
            set { _glyph = value; ApplyVisuals(); }
        }

        private string _title = "";
        public string Title
        {
            get => _title;
            set { _title = value; ApplyVisuals(); }
        }

        private string _description = "";
        public string Description
        {
            get => _description;
            set { _description = value; ApplyVisuals(); }
        }

        private string _actionText = "";
        public string ActionText
        {
            get => _actionText;
            set { _actionText = value; ApplyVisuals(); }
        }

        /// <summary>主行动的无障碍名称；为空时回退到 ActionText。</summary>
        public string ActionAutomationName { get; set; }

        public void ApplyTheme(SolidColorBrush foreground, SolidColorBrush muted)
        {
            EmptyTitle.Foreground = foreground;
            EmptyDescription.Foreground = muted;
            EmptyIcon.Foreground = muted;
        }

        private void ApplyVisuals()
        {
            if (EmptyIcon == null) return;

            EmptyIcon.Glyph = _glyph;
            EmptyTitle.Text = _title ?? "";
            EmptyDescription.Text = _description ?? "";

            bool hasAction = !string.IsNullOrWhiteSpace(_actionText);
            EmptyAction.Content = _actionText ?? "";
            EmptyAction.Visibility = hasAction ? Visibility.Visible : Visibility.Collapsed;
            if (hasAction)
            {
                AutomationProperties.SetName(EmptyAction, ActionAutomationName ?? _actionText);
            }

            AutomationProperties.SetName(this, _title ?? "");
        }

        private void EmptyAction_Click(object sender, RoutedEventArgs e)
            => ActionInvoked?.Invoke(this, EventArgs.Empty);
    }
}
