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
            this.InitializeComponent();
            this.Suspending += OnSuspending;
            this.UnhandledException += OnUnhandledException;
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
                await DownloadManager.SaveAllDownloadsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Suspending save error: {ex.Message}");
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void OnUnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"=== Unhandled Exception ===");
            System.Diagnostics.Debug.WriteLine($"Message: {e.Exception?.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack: {e.Exception?.StackTrace}");

#if DEBUG
            e.Handled = true;
#endif
        }
    }
}