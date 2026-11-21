using EdgeRebuild.Core;
using EdgeRebuild.Services;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation.Metadata;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace EdgeRebuild
{
    sealed partial class App : Application
    {
        // 应用已运行时，跳转菜单可能同时经 OnLaunched / OnActivated 到达；
        // 用目标 + 时间窗去重，避免同一个跳转项被执行两次。
        private static string? _lastJumpListTarget;
        private static long _lastJumpListTicks;
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

            // Application.UnhandledException 不覆盖所有线程（尤其非 UI 线程 / 线程池），
            // 原生多视图下崩溃常发生在副窗口线程，这里兜底
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Services.Diagnostics.Log(
                    $"[AppDomain] UnhandledException (线程 {Environment.CurrentManagedThreadId}, " +
                    $"IsTerminating={e.IsTerminating}): {e.ExceptionObject}");
            };
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            // 窗口兼容模式诊断：强制 17763 时需要从日志确认实际走了哪条路径
            Services.Diagnostics.Log(
                $"[启动] 窗口模式={(PlatformCompat.ForceLegacyWindowMode ? "17763兼容" : "标准")} " +
                $"HasXamlRoot={PlatformCompat.HasXamlRoot} HasAppWindow={PlatformCompat.HasAppWindow} " +
                $"调试开关={DebugOptions.ForceLegacyWindowMode} OS={Environment.OSVersion.VersionString}");

            // 启动阶段计时：先量出 DB/设置/数据/主题/导航各占多少，再决定是否上
            // “会话标签懒加载”这类大改动。日志保留在 crash.log，便于 Release 对比。
            var startupSw = System.Diagnostics.Stopwatch.StartNew();

            // 任务栏/开始菜单跳转菜单：订阅 TabManager 的关闭标签记录，
            // 并刷新“最近关闭的标签页 / InPrivate”任务。失败不影响启动。
            try { _ = JumpListService.InitializeAsync(); }
            catch (Exception ex) { Services.Diagnostics.Log($"[JumpList] 初始化失败: {ex.Message}"); }

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

            Frame? rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                bool suppressMainActivation = false;
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
                    Services.Diagnostics.Log($"[启动计时] DB初始化={startupSw.ElapsedMilliseconds}ms");
                    startupSw.Restart();

                    // 设置一次性读入内存：后续 SettingsPane/Toolbar/标签 DoNotTrack 查询
                    // 全部走缓存，不再逐项访问 SQLite。
                    try { await SettingsManager.InitializeAsync(); }
                    catch (Exception ex) { Services.Diagnostics.Log($"[启动] 设置缓存加载失败（已降级）: {ex.Message}"); }
                    Services.Diagnostics.Log($"[启动计时] 设置缓存={startupSw.ElapsedMilliseconds}ms");
                    startupSw.Restart();

                    // 下载目录设置必须在首次 LoadDownloadsAsync 之前生效，否则启动时会
                    // 先按默认目录探测一次，MainPage 再按自定义目录探测一次。
                    try
                    {
                        string? downloadPath = SettingsManager.GetCached("DownloadFolder");
                        if (!string.IsNullOrEmpty(downloadPath))
                            DownloadManager.SetCustomDownloadPath(downloadPath);
                    }
                    catch { }

                    // ---- Phase 2：恢复数据源加载（收藏/稍后读/历史/下载/集锦）----
                    // MainPage 未参与启动，但 Demo 页的标签条右键、后续收藏夹栏都要读这些表；
                    // 每项单独 try/catch，单项失败只降级不阻断启动。
                    foreach (var (name, load) in new (string, Func<Task>)[]
                    {
                        ("收藏夹", () => FavoritesManager.Instance.LoadAsync()),
                        ("稍后读", () => ReadingListManager.Instance.LoadAsync()),
                        ("历史记录", () => HistoryManager.LoadAsync()),
                        ("下载记录", () => DownloadManager.LoadDownloadsAsync()),
                        ("集锦", () => CollectionManager.Instance.LoadGroupsAsync())
                    })
                    {
                        try { await load(); }
                        catch (Exception ex) { Services.Diagnostics.Log($"[启动] {name}加载失败（已降级）: {ex.Message}"); }
                    }
                    Services.Diagnostics.Log($"[启动计时] 数据加载={startupSw.ElapsedMilliseconds}ms");
                    startupSw.Restart();

                    ThemeManager.Initialize();
                    Services.Diagnostics.Log($"[启动计时] 主题={startupSw.ElapsedMilliseconds}ms");
                    startupSw.Restart();

                    // 启动页 = 浏览器主窗 MainPage（slot=0）：
                    // OnNavigatedTo 里走主窗口分支 → 恢复 slot=0 会话 →
                    // RestoreSecondaryWindowsAsync 按持久化槽位重建副窗口。
                    // 多窗口测试页 DemoPage 由菜单「打开多窗口测试页」经 WindowManager 进入。
                    // 跳转菜单参数：InPrivate 任务走特殊标记，最近关闭项按短 ID 取回 URL
                    string launchArguments = e.Arguments ?? "";
                    object navParameter = launchArguments;
                    string? jumpTarget = ResolveJumpListTarget(launchArguments);
                    if (jumpTarget != null)
                    {
                        navParameter = jumpTarget;
                        MarkJumpListHandled(jumpTarget);
                        Services.Diagnostics.Log($"[JumpList] 冷启动 target={jumpTarget}");
                    }
                    else if (launchArguments.StartsWith(JumpListService.ArgumentPrefix, StringComparison.Ordinal))
                    {
                        // 最近关闭项已失效：按普通启动走（恢复会话/主页）
                        navParameter = "";
                    }

                    rootFrame.Navigate(typeof(MainPage), navParameter);
                    Services.Diagnostics.Log($"[启动计时] MainPage导航={startupSw.ElapsedMilliseconds}ms");
                    startupSw.Restart();
                }
                else
                {
                    // 应用已在运行：系统只会激活现有窗口，不会再次走 Navigate。
                    // 这里把跳转菜单目标转发给当前活动窗口。
                    string? runningTarget = ResolveJumpListTarget(e.Arguments);
                    if (runningTarget != null && TryBeginJumpListHandling(runningTarget))
                        suppressMainActivation = await HandleJumpListActivationAsync(runningTarget);
                }
                // 主窗口（slot=0）不经 NativeWindowManager.RegisterWindow，
                // 关闭时不会有任何 [NativeWM] 窗口关闭 日志——这里补一条，
                // 用来区分"主窗口被关掉（进程正常退出）"和"进程崩溃"
                Window.Current.Closed += (s, a) =>
                    Services.Diagnostics.Log("[App] 主窗口 Closed（slot=0 未注册，进程随后退出属正常）");
                if (!suppressMainActivation)
                    Window.Current.Activate();
                Services.Diagnostics.Log($"[启动计时] 激活完成={startupSw.ElapsedMilliseconds}ms");
            }
        }

        /// <summary>
        /// 应用运行中收到跳转菜单激活：部分状态下 OnLaunched 不会再次触发，
        /// 由 OnActivated 兜底。冷启动仍由 OnLaunched 的 Navigate 处理。
        /// </summary>
        protected override async void OnActivated(IActivatedEventArgs e)
        {
            base.OnActivated(e);

            if (e.Kind != ActivationKind.Launch) return;
            if (!(e is ILaunchActivatedEventArgs launch)) return;

            string? target = ResolveJumpListTarget(launch.Arguments);
            if (target == null) return;
            if (!TryBeginJumpListHandling(target)) return;

            await HandleJumpListActivationAsync(target);
        }

        /// <summary>解析跳转菜单参数；非跳转参数返回 null。</summary>
        private static string? ResolveJumpListTarget(string? arguments)
        {
            if (string.IsNullOrEmpty(arguments) ||
                !arguments!.StartsWith(JumpListService.ArgumentPrefix, StringComparison.Ordinal))
                return null;

            string rest = arguments.Substring(JumpListService.ArgumentPrefix.Length);
            if (rest == "inprivate")
                return "inprivate:";

            if (rest.StartsWith("reopen|", StringComparison.Ordinal))
            {
                string? url = JumpListService.ResolveClosedTab(rest.Substring("reopen|".Length));
                return string.IsNullOrEmpty(url) ? null : url;
            }

            // “新建窗口”任务：运行中开新窗口，冷启动按普通启动处理
            return "";
        }

        private static bool TryBeginJumpListHandling(string target)
        {
            long now = Environment.TickCount64;
            if (string.Equals(target, _lastJumpListTarget, StringComparison.Ordinal) &&
                now - _lastJumpListTicks < 1500)
            {
                return false;
            }

            _lastJumpListTarget = target;
            _lastJumpListTicks = now;
            return true;
        }

        private static void MarkJumpListHandled(string target)
        {
            _lastJumpListTarget = target;
            _lastJumpListTicks = Environment.TickCount64;
        }

        private static async Task<bool> HandleJumpListActivationAsync(string target)
        {
            try
            {
                var rootFrame = Window.Current?.Content as Frame;
                if (rootFrame?.Content == null)
                    return false;   // 冷启动由 OnLaunched 处理

                bool handledBySecondary = await WindowManager.TryHandleJumpListActivationAsync(target);
                if (handledBySecondary)
                    return true;   // 副窗口已由 WindowManager 带到前台

                if (rootFrame.Content is MainPage mainPage)
                {
                    await mainPage.HandleJumpListActivationAsync(target);

                    bool opensNewWindow = string.IsNullOrEmpty(target) ||
                        string.Equals(target, "inprivate:", StringComparison.OrdinalIgnoreCase);
                    if (opensNewWindow)
                        return true;   // 新窗口自己上屏，不要把主窗口拉到前面

                    Window.Current?.Activate();   // URL 在主窗口新标签打开
                }

                Services.Diagnostics.Log($"[JumpList] 运行中激活 target={target}");
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Log($"[JumpList] 运行中激活失败: {ex}");
            }
            return false;
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