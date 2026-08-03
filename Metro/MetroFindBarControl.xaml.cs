using System;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace EdgeRebuild.Metro
{
    /// <summary>
    /// 页内查找栏：输入防抖后发 FindRequested(query, "new")，上/下一个即时发 prev/next，
    /// 关闭发 CloseRequested。实际查找由 MetroPage 经 FindService 注入页面。
    /// </summary>
    public sealed partial class MetroFindBarControl : UserControl
    {
        public event Action<string, string> FindRequested;   // query, mode(new/prev/next)
        public event Action CloseRequested;

        private readonly DispatcherTimer _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };

        public MetroFindBarControl()
        {
            InitializeComponent();
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                FindRequested?.Invoke(FindBox.Text, "new");
            };
        }

        public void Show()
        {
            Visibility = Visibility.Visible;
            FindBox.Focus(FocusState.Programmatic);
            FindBox.SelectAll();
            if (!string.IsNullOrEmpty(FindBox.Text))
                FindRequested?.Invoke(FindBox.Text, "new");
        }

        public void SetCount(int index, int count)
            => CountText.Text = (count > 0) ? $"{index + 1}/{count}" : "0/0";

        public void ClearState()
        {
            FindBox.Text = "";
            CountText.Text = "0/0";
        }

        private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void FindBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                _debounceTimer.Stop();
                FindRequested?.Invoke(FindBox.Text, "next");
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                CloseRequested?.Invoke();
                e.Handled = true;
            }
        }

        private void PrevBtn_Click(object sender, RoutedEventArgs e)
        {
            _debounceTimer.Stop();
            FindRequested?.Invoke(FindBox.Text, "prev");
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            _debounceTimer.Stop();
            FindRequested?.Invoke(FindBox.Text, "next");
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    }
}
