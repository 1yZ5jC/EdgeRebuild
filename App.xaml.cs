using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using System.Threading;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace EdgeRebuild
{
    sealed partial class App : Application
    {
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;

            this.UnhandledException += (s, e) =>
            {
                Services.Diagnostics.Log(
                    $"Unhandled Exception (线程 {Environment.CurrentManagedThreadId}): {e.Exception}\n" +
                    $"当前调用栈: {Environment.StackTrace}");
                // 不再强制标记 Handled，让严重异常正常传播
            };
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

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    try
                    {
                        await DatabaseService.InitializeAsync();
                        await FavoritesManager.Instance.LoadAsync();
                        await HistoryManager.LoadAsync();
                        await DownloadManager.LoadDownloadsAsync();
                        ThemeManager.Initialize();
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

                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }
                Window.Current.Activate();
            }
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        private async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                await SessionManager.FlushAsync();
                await DownloadManager.SaveAllDownloadsAsync();
            }
            catch { }
            finally { deferral.Complete(); }
        }
    }
}