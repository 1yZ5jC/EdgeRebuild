using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
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
                    }
                    catch (Exception ex)
                    {
                        // 数据库打不开属致命错误：先落日志，再提示；此时窗口尚未 Activate，
                        // ContentDialog 无 XamlRoot 可挂会抛"Only a single ContentDialog"，改用 MessageDialog
                        Services.Diagnostics.Log($"[启动失败] {ex}");
                        try
                        {
                            var dialog = new Windows.UI.Popups.MessageDialog(
                                $"数据库初始化失败：{ex.Message}", "EdgeRebuild 启动失败");
                            await dialog.ShowAsync();
                        }
                        catch (Exception dialogEx)
                        {
                            Services.Diagnostics.Log($"[启动失败] 错误对话框显示失败: {dialogEx.Message}");
                        }
                        Application.Current.Exit();
                        return;
                    }

                    // 各数据源加载互相独立：单项失败只降级该功能，不阻断启动
                    foreach (var (name, load) in new (string, Func<Task>)[]
                    {
                        ("收藏夹", () => FavoritesManager.Instance.LoadAsync()),
                        ("稍后读", () => ReadingListManager.Instance.LoadAsync()),
                        ("历史记录", () => HistoryManager.LoadAsync()),
                        ("下载记录", () => DownloadManager.LoadDownloadsAsync())
                    })
                    {
                        try { await load(); }
                        catch (Exception ex) { Services.Diagnostics.Log($"[启动] {name}加载失败（已降级）: {ex.Message}"); }
                    }
                    ThemeManager.Initialize();

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