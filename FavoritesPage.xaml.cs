using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using EdgeRebuild.Core;

namespace EdgeRebuild
{
    public sealed partial class FavoritesPage : Page
    {
        public FavoritesPage()
        {
            this.InitializeComponent();
            PopulateFavorites();
        }

        private void PopulateFavorites()
        {
            FavListView.Items.Clear();
            foreach (var fav in FavoritesManager.Instance.Favorites)
            {
                var stack = new StackPanel
                {
                    Margin = new Thickness(4, 8, 4, 8)
                };
                stack.Children.Add(new TextBlock
                {
                    Text = fav.Title,
                    FontWeight = Windows.UI.Text.FontWeights.Bold
                });
                stack.Children.Add(new TextBlock
                {
                    Text = fav.Url,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Windows.UI.Colors.Gray)
                });
                FavListView.Items.Add(stack);
            }
        }

        private void FavListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FavListView.SelectedItem is StackPanel panel &&
                panel.Children.Count >= 2 &&
                panel.Children[0] is TextBlock titleBlock &&
                panel.Children[1] is TextBlock urlBlock)
            {
                string url = urlBlock.Text;
                MainPage.NavigateToUrl?.Invoke(url);
                if (Frame.CanGoBack)
                    Frame.GoBack();
            }
        }
    }
}