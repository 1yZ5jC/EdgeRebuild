using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation.Metadata;
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

            // 真·多视图：fire-and-forget 任务（如窗口展示、后台探测）的静默故障
            // 在终结期浮出——落日志，避免"窗口没出来但没有任何报错"的盲区
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Services.Diagnostics.Log($"[UnobservedTask] {e.Exception}");
                e.SetObserved();   // 防止进程级击穿
            };
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            // 真·多视图前提：禁用系统视图激活策略。打包应用默认由系统接管视图激活，
            // 此时 TryShowAsStandaloneAsync 的展示请求不会真正上屏（实测：调用挂起、
            // 副视图永不显示）。必须在任何窗口创建之前调用。
            try
            {
                if (ApiInformation.IsMethodPresent(
                        "Windows.UI.ViewManagement.ApplicationViewSwitcher",
                        nameof(Windows.UI.ViewManagement.ApplicationViewSwitcher.DisableSystemViewActivationPolicy)))
                {
                    Windows.UI.ViewManagement.ApplicationViewSwitcher.DisableSystemViewActivationPolicy();
                    Services.Diagnostics.Log("[多窗口] DisableSystemViewActivationPolicy 已执行");
                }
                else
                {
                    Services.Diagnostics.Log("[多窗口] DisableSystemViewActivationPolicy 契约中不存在");
                }
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Log($"[多窗口] DisableSystemViewActivationPolicy 失败: {ex.Message}");
            }

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
            // 真·多视图（CreateNewView）实验结论：TryShowAsStandaloneAsync 的展示请求
            // 在本机环境不完成（挂起），SwitchAsync 亦无法上屏，DisableSystemViewActivationPolicy
            // 无效——已回退 AppWindow 同线程模型。保留本调用无害；若未来迁移 WinUI3，
            // 该开关是标准前置。详见 Docs/ROADMAP.md「多窗口」节。
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

                    // ---- Demo 阶段：数据源加载全部停用（MainPage 未参与启动）----
                    // 恢复方法：去掉下面整段注释。
                    //foreach (var (name, load) in new (string, Func<Task>)[]
                    //{
                    //    ("收藏夹", () => FavoritesManager.Instance.LoadAsync()),
                    //    ("稍后读", () => ReadingListManager.Instance.LoadAsync()),
                    //    ("历史记录", () => HistoryManager.LoadAsync()),
                    //    ("下载记录", () => DownloadManager.LoadDownloadsAsync()),
                    //    ("集锦", () => CollectionManager.Instance.LoadGroupsAsync())
                    //})
                    //{
                    //    try { await load(); }
                    //    catch (Exception ex) { Services.Diagnostics.Log($"[启动] {name}加载失败（已降级）: {ex.Message}"); }
                    //}
                    ThemeManager.Initialize();

                    // Demo：主窗口只挂一个极简 DemoPage，浏览器主窗（MainPage）
                    // 与会话恢复暂不参与启动。副窗口由 DemoPage 按钮经 WindowManager 打开。
                    rootFrame.Navigate(typeof(DemoPage), e.Arguments);
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