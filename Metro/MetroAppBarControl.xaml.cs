using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Metro
{
    public sealed partial class MetroAppBarControl : UserControl
    {
        public event Action TabsClicked;
        public event Action NewTabClicked;
        public event Action SettingsClicked;

        public MetroAppBarControl()
        {
            InitializeComponent();
        }

        public void UpdateTabCount(int count)
        {
            TabCountText.Text = count.ToString();
        }

        private void TabsBtn_Click(object sender, RoutedEventArgs e) => TabsClicked?.Invoke();
        private void NewTabBtn_Click(object sender, RoutedEventArgs e) => NewTabClicked?.Invoke();
        private void SettingsBtn_Click(object sender, RoutedEventArgs e) => SettingsClicked?.Invoke();
    }
}
