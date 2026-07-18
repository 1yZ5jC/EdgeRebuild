using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using EdgeRebuild.Services;
using EdgeRebuild.Core;

namespace EdgeRebuild
{
    sealed partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;
            }

            // 如果已暂停后恢复，直接激活即可
            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    // 初始化数据库及数据（加异常保护）
                    try
                    {
                        await DatabaseService.InitializeAsync();
                        await FavoritesManager.Instance.LoadAsync();
                        await HistoryManager.LoadAsync();
                    }
                    catch (Exception ex)
                    {
                        var errorDialog = new ContentDialog
                        {
                            Title = "启动失败",
                            Content = $"数据库初始化错误：{ex.Message}",
                            CloseButtonText = "退出"
                        };
                        await errorDialog.ShowAsync();
                        Application.Current.Exit();
                        return;
                    }

                    // ★ 关键：导航到主页面
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }
                Window.Current.Activate();
            }
        }

        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception($"Failed to load page {e.SourcePageType.FullName}");
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            deferral.Complete();
        }
    }
}