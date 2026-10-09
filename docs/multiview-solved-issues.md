# 真·多视图（CoreWindow.CreateNewView）已解决问题清单

分支：`feature/multi-window-rewrite`
用途：反向集成回 `master` 前的 review 依据。每条按 **现象 → 根因 → 修复 → 回归验证** 四段式记录。

> **状态口径**：本文档记录的是「已在本分支验证通过」的问题；文末「已知未结案」章节记录尚未解决的回归。
> 反向集成范围见第 7 节。

---

## A. 让原生多视图「能跑起来」的 5 个致命问题

### A1. `viewId` 哨兵错用 `-1`，`TryShowAsStandaloneAsync` 永不执行

- **现象**：`CreateNewView` 与 `RegisterWindow` 日志齐全，但窗口永不出现；早期误判为「本机 UWP 多视图不可用」。
- **根因**：`NativeWindow._viewId` 用 `-1` 表示「尚未取得」，但本机 `ApplicationView.Id` 是**负数**（实测 `-198299`、`-1639607`）。判空逻辑 `if (_viewId < 0) return false;` 在真实 ID 上提前返回，后续 `TryShowAsStandaloneAsync` 从未被调用。
- **修复**：改用 `public const int NoViewId = int.MinValue;`，判空改为 `if (_viewId == NoViewId)`。
  `Core/NativeWindow.cs:47-50`
- **回归验证**：日志出现 `[NativeWindow] ? TryShowAsStandaloneAsync 成功` + `[NativeWM] ? 原生视图显示成功 slot=N`，且副窗口上屏。

### A2. `CreateNewView` 线程没有 `SynchronizationContext` → `RPC_E_WRONG_THREAD` 静默崩溃

- **现象**：副窗口线程抛 `COMException 0x8001010E (RPC_E_WRONG_THREAD)`（`new AcrylicBrush()` / `TextBox.get_Text()`），随后**进程无任何异常日志直接消失**。
- **根因**：`CoreApplication.CreateNewView` 创建的视图线程默认没有 `SynchronizationContext`，该线程上所有 `await` 的续体掉进线程池执行，碰到 XAML 对象就跨线程访问。主窗口之所以没事，是 WinUI2 启动时装了 `DispatcherQueueSynchronizationContext`。
- **修复**：在视图线程的 dispatcher 回调**最开始**（任何 `await` 之前）`SetSynchronizationContext(new ViewSyncContext(view.Dispatcher))`；`ViewSyncContext.Post` 把续体投回该视图的 `CoreDispatcher`。实测该 context 能**存活到后续 XAML `Loaded` 事件**。
  `Core/NativeWindow.cs:76-82`（注释）、`Core/NativeWindow.cs:330-353`（实现）
- **回归验证**：日志出现 `viewId=... ctx=ViewSyncContext` 与 `ctx=ViewSyncContext hasAccess=True`，进程存活。

### A3. `CoreWindow.Closed` 点系统 X 不触发 → 关闭清理收不到信号

- **现象**：用户点系统标题栏关闭按钮，窗口消失但 `slot=N` 的清理日志缺失，槽位不回收。
- **根因**：UWP 多视图下 `CoreWindow.Closed` 在「点系统 X」路径上不触发；权威关闭信号是 `ApplicationView.Consolidated`。
- **修复**：两个都挂，统一走 `NativeWindow.RaiseClosed()`（带 `IsClosed` 去重），再 `Dispose()`。
  `Core/NativeWindow.cs:91-95`（注释）
- **回归验证**：点系统 X → 日志 `[NativeWM] 窗口关闭 slot=N mode=NativeView` + `[Demo] 关闭清理 slot=N`，只出现一次。

### A4. `Dispatcher.RunAsync` 在未上屏视图上永久挂起

- **现象**：展示请求失败时，后续清理/关闭调用把主流程一起卡死。
- **根因**：视图从未上屏时其线程可能不再泵消息，`Dispatcher.RunAsync` 的任务永不完成。
- **修复**：不 await，或包一层 `RunWithTimeout`（2.5s）超时放弃；失败路径 `_ = CloseQuietlyAsync(...)` fire-and-forget。
  `Core/NativeWindowManager.cs:125-128`（注释）、`Core/NativeWindow.cs`（`RunWithTimeout` 重载带 `what` 标签）
- **回归验证**：人为让 `TryShowAsStandaloneAsync` 失败 → 2.5s 后回退 AppWindow 分支，主流程不卡。

### A5. 不调 `DisableSystemViewActivationPolicy` → 副视图永不显示

- **现象**：`TryShowAsStandaloneAsync` 返回成功但窗口不出现。
- **根因**：打包应用默认由系统接管视图激活策略。
- **修复**：`App.OnLaunched` 中调用 `ApplicationViewSwitcher.DisableSystemViewActivationPolicy()`（含 try/catch 降级日志）。
  `App.xaml.cs:51-61`
- **回归验证**：启动日志 `[启动] DisableSystemViewActivationPolicy 已执行`。

---

## B. 崩溃可观测性

### B1. `Application.UnhandledException` 不覆盖非 UI 线程

- **现象**：A2 类崩溃没有任何日志，无法定位。
- **修复**：补 `AppDomain.CurrentDomain.UnhandledException`（打印线程 ID + `IsTerminating`）与 `TaskScheduler.UnobservedTaskException`。
  `App.xaml.cs:38-46`
- **回归验证**：副窗口线程抛未处理异常 → `crash.log` 出现 `[AppDomain] UnhandledException (线程 N, IsTerminating=...)`。

### B2. 主窗口 slot=0 关闭无日志，无法区分「正常退出」与「崩溃」

- **根因**：主窗口不经 `NativeWindowManager.RegisterWindow`，没有关闭日志。
- **修复**：`Window.Current.Closed` 补一条 `[App] 主窗口 Closed（slot=0 未注册，进程随后退出属正常）`。
  `App.xaml.cs:135-136`
- **回归验证**：正常关闭主窗口 → 日志出现该条；进程崩溃则不出现。

---

## C. 跨线程模型的 4 类坑

### C1. 静态事件广播在「发起线程」触发，订阅方跨线程访问 XAML

- **现象**：副窗口打开后，主窗口改主题/切槽位 → 副窗口 `RPC_E_WRONG_THREAD`。
- **根因**：`ThemeManager.ThemeChanged`、`NativeWindowManager.SlotActivated`、`SettingsManager.SettingChanged` 是静态事件，`Invoke` 跑在**触发者的线程**上。
- **修复**：订阅方自己 marshal —— 拿本页 `Dispatcher.RunAsync` 回本窗口线程再改 UI。
  `DemoPage.xaml.cs`（`OnThemeChanged`、`OnSlotActivated` 两处）、`Core/NativeWindowManager.cs:26-31`（注释）
- **回归验证**：主副窗口同时打开，改主题 → 两窗口都换肤，`crash.log` 无异常。

### C2. XAML 画笔有线程亲和，不能跨窗口共享

- **现象**：副窗口布局时抛 `RPC_E_WRONG_THREAD`。
- **根因**：`Brush` 是 `DependencyObject`，绑定到创建线程。
- **修复**：`ThemeManager.Colors` **每次在调用线程上新建一套**，不缓存跨线程复用。
  `Services/ThemeManager.cs:19-22`（注释）、`Services/ThemeManager.cs:108-109`
- **回归验证**：两窗口同时渲染，无跨线程异常。

### C3. `Window.Current` / `GetForCurrentView` 语义在多视图下易误用

- **现象**：AppWindow 同线程模式下，取到的是**主窗口**的状态而非本窗口。
- **根因**：AppWindow 子窗口与主窗口共享 UI 线程，`Window.Current` 恒指主窗口。
- **修复**：取本窗口尺寸改用 `XamlRoot`；AppWindow 关闭信号用 `ApplicationView.Consolidated` / `AppWindow.Closed`；每个窗口的标题栏设置走 `CoreApplication.GetCurrentView().TitleBar`（按当前线程取视图）。
  `Core/LayoutManager.cs:160-170`（注释）、`MainPage.xaml.cs:65-70`（注释）、`DemoPage.xaml.cs`（`SetupTitleBar`）
- **回归验证**：副窗口独立缩放 → `LayoutManager` 用的是本窗口宽度。

### C4. `DispatcherTimer` 等定时器有线程亲和

- **现象**：跨窗口调度的服务定时器在副窗口失效/抛异常。
- **修复**：改用 `System.Threading.Timer`。
  `Services/SessionManager.cs:36-37`、`Services/HistoryManager.cs:11`、`Core/FavoritesManager.cs:13`、`Core/ReadingListManager.cs:15`
- **回归验证**：会话自动保存在副窗口打开时仍正常触发。

### C5. `DownloadManager.ShowToastOnUIThread` 跨 ASTA 取 `MainView.CoreWindow` → 进程直接崩

- **现象**：拖出标签开第二个窗口（或点"打开新窗口"）约 1 秒后 `Unhandled Exception (线程 1): COMException 0x8000001F (RPC_E_WRONG_THREAD)`，整进程退出。
- **根因**：`ShowToastOnUIThread` 是 `async void`，首句 `await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(...)` 在**副窗口线程**上取 `MainView.CoreWindow` 属于跨 ASTA COM 调用 → 抛异常；`async void` 的异常经 `ViewSyncContext.Post` 重新抛出，绕过了调用方的 `try/catch`，直接打崩进程。触发点：新窗口加载 `DownloadManager` → 系统下载目录不可用 → 提示 toast（`DownloadManager.cs:211`）。
- **修复**：toast 本身不依赖 UI 线程，去掉 dispatcher 投递，直接 `try { NotificationService.ShowToast(...) } catch { }`。
  `Services/DownloadManager.cs:294-300`
- **回归验证**：拖出标签开 MainPage 副窗口 → `[下载目录]` 日志照常、不再出现 0x8000001F。

### C6. `ThemeManager.OnColorValuesChanged` 跨线程取 `MainView.CoreWindow`（同类隐患）

- **现象**：系统主题/配色变化时可能触发与 C5 相同的 0x8000001F（`UISettings.ColorValuesChanged` 不在 UI 线程回调）。
- **修复**：`ThemeManager.Initialize()`（主线程）先把主视图 dispatcher 缓存到 `_mainDispatcher`，回调里只用缓存 + `try/catch`。
  `Services/ThemeManager.cs:26-58`
- **回归验证**：常规启动/切主题无异常（主动路径未单独压测）。

---

## D. 布局重入与槽位广播

### D1. `LayoutManager` 在布局敏感回调里同步重算 + 强制 `UpdateLayout()` → 整窗冻结

- **现象**：`COMException 0x8000FFFF (E_UNEXPECTED)`，调试器中断 / 窗口冻结。
- **根因**：`Recalculate` 来自 `XamlRoot.Changed` 等布局敏感回调时同步执行，触发嵌套 `MeasureOverride`；原实现在 `UpdateRightPanelMargin` 里还强制 `UpdateLayout()`，而主窗口树中的 WebView2 等原生元素强制测量必抛。
- **修复**：重算排到调度器低压队列；**禁止**在 `LayoutManager` 里调 `_rightSidePanel.UpdateLayout()`。
  `Core/LayoutManager.cs:68-72`（注释）、`Core/LayoutManager.cs:187-191`（注释）
- **回归验证**：开副窗口 / 改窗口尺寸 → 不再出现 E_UNEXPECTED。

### D2. 新窗口显示成功后漏广播 `ActiveSlot/SlotActivated`

- **现象**：新窗口打开但底部状态条显示 `slot 0`。
- **修复**：原生分支显示成功后补 `ActiveSlot = slot; SlotActivated?.Invoke(slot);`。
  `Core/NativeWindowManager.cs`
- **回归验证**：新窗口状态条显示 `slot N`（N 与日志一致）。

---

## E. 标题栏隐藏与拖拽区（本轮新做）

### E1. 隐藏系统标题栏

- **修复**：
  - 主窗口 / 原生多视图窗口（均为 CoreWindow）：`CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true` + `Window.Current.SetTitleBar(TitleBarDragArea)` + `ThemeManager.UpdateTitleBarColors()`
  - AppWindow 副窗口：`_appWindow.TitleBar.ExtendsContentIntoTitleBar = true` + `ApplyAppWindowTitleBarColors()`（caption 按钮透明底、悬停/按下浅色）
  - 顶部标签条行右侧预留空间给系统 min/max/close（隐藏标题栏后仍悬浮在右上角）：
    由 `LayoutManager.UpdateSafeZonePadding` 给 `TabBarBorder` 设 **右 Padding ≥130px**、
    `UpdateRightPanelMargin` 给 `RightSidePanel` 设 **右 Margin 50px**（合计 ≥180px > 实测 caption 宽 146px），
    不再在 `NewTabBtn` 上写死 142px
  - `DemoPage.xaml.cs`（`SetupTitleBar`、`ApplyAppWindowTitleBarColors`）
- **回归验证**：截图确认标签条即窗口顶部、系统按钮浮在右上角且不压 `+` 按钮；日志 `[Demo] 已隐藏系统标题栏 slot=N mode=...`。

### E2. 标签条拖拽区被 `ScrollViewer` 命中盖掉 → 拖不动窗口

- **现象**：`DRAG FAILED`，鼠标在标签条空白处拖动窗口无反应。
- **根因**：`TabBarBorder` → `ScrollViewer` → `StackPanel` 这条链路上，`ScrollViewer` 自身参与命中测试，挡住了位于其**外层**的 `TitleBarDragArea`，`Window.SetTitleBar` 拿不到命中。
- **修复（两次迭代，最终形态）**：
  1. `TabBarBorder` 及内层 `TabBarPanel` **一律不设 `Background`**（设了会整行吃掉命中）
  2. `TitleBarDragArea` 从 ScrollViewer 内容层挪出来，改成 `TabBarBorder` 内 Grid 的**第一个子元素 + `Grid.ColumnSpan="4"`**（垫底、铺满整行）。标签 `StackPanel`、`NewTabBtn`、滚动按钮在其**之上**，能自己命中就照常响应；右侧 `*` 空列与标签间空隙落到底层矩形 → 标题栏拖拽
  - 中间态（只放进 ScrollViewer 内容层）的缺陷：`Auto` 列宽下 ScrollViewer 只有标签内容那么宽，右侧空列**没有**拖拽区，`MOVED delta=0,0`
  - `DemoPage.xaml` 标签条 Grid
- **回归验证**：`MOVED delta=200,80` 与光标位移完全一致；标签点击 / 右键菜单 / 标签拖拽排序同时正常。

---

## F. 反向集成 review 清单（合并到 `master` 前）

分支领先 `master` **9 个提交、0 落后**，差异 18 个文件（+1984 / -259），**可直接 fast-forward 无冲突**。四类处理结果如下（2026-10-09 复核）：

| 类别 | 文件 | 处理 |
|---|---|---|
| ① 基建（直接合） | `Core/NativeWindow.cs`（364 行，新增）、`Core/NativeWindowManager.cs`（243 行，新增）、`Core/WindowManager.cs`（改为委托）、`Controls/ToolbarControl.xaml.cs`（跨线程 marshal）、`Services/WindowGeometryManager.cs`（65 行，新增） | ✅ 已合入 |
| ② 崩溃兜底（直接合） | `App.xaml.cs` 的 `AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`、`Window.Current.Closed` 日志 | ✅ 已合入 |
| ③ Demo 状态（合前还原） | ~~`App.xaml.cs` 注释掉的数据源加载~~ → 现为 `App.xaml.cs:115-126` 主动加载 5 个数据源；~~`Navigate(typeof(DemoPage))`~~ → 现为 `App.xaml.cs:131` **`Navigate(typeof(MainPage))`** | ✅ 已还原 |
| ④ DemoPage | `DemoPage.xaml` / `DemoPage.xaml.cs`（新增 180 + 622 行） | ✅ 决策：**保留但不作启动页**——由主菜单「打开多窗口测试页」经 `WindowManager.OpenDemoWindowAsync()` 进入 |

**启动页切换验证（2026-10-09 12:46–13:00，Debug/x86，`exit=0`）**：

1. 冷启动 → 主窗口渲染 **MainPage**（标签条 + 工具栏 + 新标签页），不再是 Demo 状态栏 ✅
2. 主菜单出现新项 **「打开多窗口测试页」**（`AppMenuManager` 菜单项，位于「打开 Metro IE 窗口」之后）→ 点击开 Demo 副窗口 `slot=1 thread=12` ✅
3. Demo 状态栏 `MainPageWinBtn` → MainPage 副窗口 `slot=2 thread=16`，`OnLoaded 完成`、`已注册副窗口=2`、进程存活、**无 0x8001010E**（G2 回归通过）✅
4. 杀进程重启 → 主窗口重建 slot=0 标签 + **`[NativeWM] 恢复副窗口 slot=2` 自动重建**（此项此前无入口：`RestoreSecondaryWindowsAsync` 只在 `MainPage.xaml.cs:457` 调用，启动页是 Demo 时从不执行）✅

**合并方式**：本地 fast-forward 后推送 `EdgeRebuild/master`（2026-10-09 决定）。

---

## G. 已知未结案

### G1. row0（标签条整行）鼠标点击失效 —— 本轮**未复现**（疑为测试环境假象）

- **上一轮现象（2026-10-08 早些时候）**：
  - UIA `InvokePattern` 调用 `NewTabBtn` → 标签数 +1 ✅
  - **真实鼠标点击** `NewTabBtn` → 标签数不变 ❌；标签关闭按钮同样无效 ❌
  - 标签条空白处拖动窗口正常 ✅；底部状态条按钮正常 ✅
- **本轮（Tier1 验证，2026-10-08 晚）全部通过**：
  - 真实鼠标左键点 `+` → `标签 1 → 标签 2`（连续两次）✅
  - 真实鼠标右键点标签 → 7 项右键菜单弹出 ✅
  - 真实鼠标拖拽标签 → `[拖拽] 排序 0 -> 2` … `释放：完成排序` ✅
  - 空白条拖动窗口 → `MOVED delta=200,80` ✅
- **判断**：未找到能稳定复现的条件。测试脚本从"盲点"改为**先 `SetForegroundWindow` + `FromPoint` 校验 pid 再点击**后即通过，怀疑上一轮是**目标窗口未激活**导致点击落在后台窗口，而非应用缺陷。
- **状态**：**不再阻塞**；若后续复现，从 E2 的命中链（`TabBarBorder` 无背景 / 拖拽矩形垫底）继续查。

### G2. MainPage 在副原生视图内 `MeasureOverride` 抛 `0x8001010E` → 进程崩溃 【已结案 2026-10-09】

- **现象**：从 Demo 标签条拖出标签（`WindowManager.OpenNewWindowAsync` → 开 **MainPage** 副窗口），或连开两个副窗口时，约 1 秒后：
  `Unhandled Exception (线程 N): COMException (0x8001010E: 应用程序无法为另一线程上的接口启动通信)`，
  调用栈止于 `IFrameworkElementOverridesMethods.MeasureOverride` → `FrameworkElement.MeasureOverride`，**没有任何应用帧**。
- **根因**：**WinUI 2（`Microsoft.UI.Xaml`）控件无法在本进程的第二个 UI 线程上被测量**。
  `Controls/HubPane.xaml` 与 `Controls/SettingsPane.xaml` 里的 `muxc:NavigationView`（内部模板含 WinUI 的
  `ItemsRepeater` / `ItemsRepeaterScrollHost`）在**进程内第一个实例所在线程**上正常，第二个 MainPage 实例换线程后
  一 `Measure` 就 `RPC_E_WRONG_THREAD`。WinUI 的静态/全局状态是**按"首个使用线程"绑定**的，不支持多 UI 线程。
  这也解释了全部现象：
  - 只有 1 个 MainPage 实例时永远正常（`#1` 拿到首个线程），`#2` 永远崩；
  - 连开 2 个 **DemoPage** 副窗口没事（Demo 树里没有 muxc 控件）；
  - `ToolbarControl`（纯 UWP）从线程 4 → 10 → 14 跨线程都正常。
- **排查过程（关键证据链）**：
  1. 复现入口：`DemoPage` 状态栏测试按钮 `MainPageWinBtn` → `WindowManager.OpenNewWindowAsync(EdgeHtml, "about:blank")`。
  2. 临时探针（`MainPage.xaml.cs` 的 `MeasureOverride` try/catch + `ProbeMeasure` 递归复测，**已移除**）逐层下钻：
     `MainPage → RootGrid → Grid[5]` 下**只有两个 `SplitView` 失败**，其余兄弟（`FindBar`/`ToolbarControl`/`TtsPlayer`…）全 `ok`。
  3. `ProbeDiag` 显示 `threadAccess=True`、`style=null`、`template=set`、`ApplyTemplate()` 返回 False 且不抛
     → 排除"元素跨线程""模板应用失败""属性 getter 抛错"。
  4. 逐项否掉的假设：
     - `PaneBackground` 的 `AcrylicBrush` → 换纯色照崩（同 Build 验证）；
     - `SplitView` 显式本地 `<Style TargetType="SplitView"/>` 绕开默认样式 → 照崩；
     - 把 `XamlControlsResources` 从 `App.xaml` 下沉到 `HubPane/SettingsPane.Resources`（每线程一份）→ 照崩；
     - `muxc:SplitView` 替代 → `error CS0234 Microsoft.UI.Xaml.Controls 中不存在 SplitView`（该 WinUI 版本无此类型），回退。
  5. **决定性一步**：直接对 `hubPane`/`SettingsPaneControl` 单独 `Measure` → 均 `0x8001010E`；
     继续下钻命中终端节点 `NavigationView#HubNavView` → … → `ItemsRepeater#MenuItemsHost` / `#FooterMenuItemsHost`（children=0），
     而它们的兄弟（`PaneToggleButtonGrid`/`TopNavArea`/`ScrollBar`…）全 `ok` → 真凶就是 WinUI 控件。
- **修复**：把 WinUI 2 控件整体换成系统自带控件（UWP 自 `10.0.17763` 起就有 `NavigationView`，本项目 `TargetPlatformMinVersion=10.0.17763.0` 正好够）：
  - `Controls/HubPane.xaml`、`Controls/SettingsPane.xaml`：去掉全部 `muxc:` 前缀（`NavigationView`/`NavigationViewItem` 共 348 处）+ 删除 `xmlns:muxc`；
  - `Controls/HubPane.xaml.cs`、`Controls/SettingsPane.xaml.cs`：`Microsoft.UI.Xaml.Controls.NavigationView*` → `Windows.UI.Xaml.Controls.NavigationView*`；
  - `App.xaml`：隐式样式 `TargetType="muxc:NavigationView"` → `TargetType="NavigationView"`；
    **保留** `<muxc:XamlControlsResources/>`（`MainPage.xaml:349` 等处 `{StaticResource TabViewButtonBackground}` 还要靠它，纯资源字典不产生 WinUI 元素，跨线程无害）。
  - 全仓 `.xaml` 现在只剩 `App.xaml:10` 一处 `muxc:`。
- **验证（2026-10-09 11:52）**：
  | 项 | 结果 |
  |---|---|
  | 连开 3 个 MainPage 副窗口（slot=1/2/3，线程 10/14/16） | ✅ 三次 `OnLoaded 完成`，`已注册副窗口=3`，进程存活，日志无 `0x8001010E` |
  | 副窗口内打开 Hub 面板（`HubBtn` → `HubSplitView.IsPaneOpen`） | ✅ UWP `NavigationView` 模板完整：汉堡按钮 + 收藏夹/历史记录/稍后读/下载/集锦 + 右侧收藏列表 |
  | 临时探针 `MeasureOverride`/`ProbeMeasure`/`ProbeDiag`/`TryMeasure` | ✅ 已全部移除 |
- **顺带修掉的次要缺陷**：`MainPage.OnLoaded` 原按 `if (_appWindow != null)` 分支，但**原生视图窗口 `_appWindow` 恒为 null**
  → 副视图误走"主窗口"分支（`_windowActive=true`、`ReportSlotFocus(0)`、不订阅槽位广播、不接管键盘）。
  已改为按 `_isNewWindow` 判定，并把 `_sessionSlot = _pendingSessionSlot` 提前到该分支之前，
  订阅后补一次 `OnSlotActivated(WindowManager.ActiveSlot)` 同步初始激活态；`CleanupStaticEvents` 补
  `WindowManager.SlotActivated -= OnSlotActivated` 与 `KeyDown/KeyUp` 反注册。
- **遗留约束（写给以后的自己）**：**不要在 `MainPage`/`HubPane`/`SettingsPane` 及任何会进副窗口的页面里新增 WinUI 2（`muxc:`）元素**，
  否则多窗口一开第二个就重现本崩溃。要用 WinUI 控件只能：① 换 UWP 等价控件；② 该页面永远只允许一个实例（= 单 UI 线程）。

---

## H. 验证工具与环境约定

- **构建**：必须用 VS MSBuild，`dotnet build` 不跑 UWP XAML 标记编译会产生约 552 个假错误
  `& "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" EdgeRebuild.csproj /p:Configuration=Debug /p:Platform=x86`
- **部署坑**：MSBuild 输出在 `win-x86\`，但注册的松散布局是 `win-x86\AppX\`（VS 部署生成，**不被我们的构建刷新**）——每次测试前必须
  `Get-ChildItem win-x86 -File | Copy-Item -Destination win-x86\AppX -Force`
  然后 `Add-AppxPackage -Register ...\win-x86\AppX\AppxManifest.xml -ForceApplicationShutdown`
- **启动**：`explorer.exe "shell:AppsFolder\AlanPlex.EdgeRebuild_cxt6mtc0m2z7c!App"`
- **日志**：`%LOCALAPPDATA%\Packages\AlanPlex.EdgeRebuild_cxt6mtc0m2z7c\LocalState\crash.log`，必须用 UTF-8 读（控制台会把中文显示成乱码）
- **验证手段**：UIA（`AutomationElement.FromPoint` / `InvokePattern` / `BoundingRectangle`）+ 截图（`CopyFromScreen` 保存 PNG 后查看）+ 日志
- **截图坐标注意**：UIA `BoundingRectangle` 含窗口阴影，`CopyFromScreen` 按它截会带上周围桌面
- **真实鼠标测试三件套**（缺一条就会得出"点击无效"的假结论）：
  1. `ShowWindow(SW_RESTORE)` + `SetWindowPos(HWND_TOP, SWP_NOACTIVATE)` + `SetForegroundWindow` 把目标窗口提到最前
  2. `AutomationElement.FromPoint` 校验 `ProcessId == 目标进程` 再按下去
  3. 拖拽必须用 `mouse_event(MOUSEEVENTF_MOVE, dx, dy)` 逐帧发相对位移——`SetCursorPos` **不保证**产生 `WM_MOUSEMOVE`，只用它会得到"按下后没移动"的假拖拽

---

## I. Phase 1（Tier1 标签条）恢复记录

### I1. 接回的三个管理器

| 管理器 | 职责 | `DemoPage.xaml.cs` 接线 |
|---|---|---|
| `LayoutManager` | 标签宽度 `100~160` / 固定标签 `36` / 溢出滚动按钮 / 右侧安全区 Padding | 构造于 `_tabManager` 之前；`LayoutUpdated` → 写 `Container.Width`；`XamlRoot.Changed`、`TabBarBorder.SizeChanged` → `UpdateTabLayout()` |
| `TabDragManager` | 拖拽排序 / 拖出成新窗口 / 右键触发 | `SortRequested` → `ReorderTab`+`SyncTabPanelOrder`；`DetachRequested` → `WindowManager.OpenNewWindowAsync` 成功才 `CloseTabAsync`；`DragStarted/DragCompleted` 维护 `_isDragging`（抑制重算）；`TabCreated` → `AttachEvents` |
| `TabContextMenuManager` | 7 项右键菜单 | `TabRightTapped` → `ShowTabRightClickMenu`；`onTogglePin` 接 `TogglePinTabAsync`；打印/查看源/捕获/共享四个条目给**占位实现**（Phase 6 再接真） |

`TabBarOrderChanged` → `SyncTabPanelOrder()`（`Children.Clear()` 后按 `Tabs` 顺序重挂 `Container`）。

### I2. `DemoPage.xaml` 标签条行结构

`Row0 = 32`，`TabBarBorder`（**无 Background**，`SizeChanged` → 重算）内 4 列 `Auto / Auto / Auto / *`：

1. 垫底的 `TitleBarDragArea`（`Grid.ColumnSpan="4"`，见 E2）
2. `ScrollLeftBtn`（`Collapsed`，溢出才显示）
3. `TabScrollViewer`（`Hidden` 滚动条、`ViewChanged`）→ `TabBarPanel`
4. `RightSidePanel` → `ScrollRightBtn` + `NewTabBtn`
5. `*` 空列（吸收余量，也是主要的标题栏拖拽命中区）

外加根 Grid 上 `Canvas x:Name="DragCanvas"`（`Grid.RowSpan="4"`、`IsHitTestVisible="False"`、`Canvas.ZIndex="1000"`）供 `TabDragManager` 画拖拽幽灵。

> 按钮样式：`TabIconButtonStyle`（`Page.Resources`，24×24、MDL2 箭头、无 `RevealButtonStyle` 依赖——后者定义在 `MainPage.xaml` 页面资源里，Demo 页取不到）。

### I3. 验证结果（2026-10-08 晚）

| 项 | 结果 |
|---|---|
| 新建标签（真实左键 `+`） | ✅ `标签 1 → 标签 2` |
| 标签宽度自适应 | ✅ 17 标签时压到 `MinTabWidth=100`，7 标签时约 132 |
| 溢出滚动 | ✅ 17 标签出现 `ScrollLeftBtn/ScrollRightBtn`，7 标签时隐藏 |
| 右键菜单 | ✅ 7 项（新建/重载/关闭/固定/关闭其他/关闭右侧/添加到集锦） |
| 拖拽排序 | ✅ 日志 `[拖拽] 排序 0 -> 2` … `释放：完成排序` |
| 拖出成新窗口 | ✅ `shown=True`（但会走进 G2 的 MainPage 崩溃路径） |
| 标题栏拖拽（空白条） | ✅ `delta=200,80` |
| `crash.log`（Demo 自身） | ✅ 无新增异常 |

### I4. Phase 2（数据源加载）恢复

- `App.xaml.cs` 还原被注释的 5 项加载：收藏夹 / 稍后读 / 历史记录 / 下载记录 / 集锦（每项独立 `try/catch`，失败只记 `[启动] xxx加载失败（已降级）` 不阻断启动）。
- `ThemeManager.Initialize()` 保持在其后（顺序不变）。
- **回归验证**：启动日志出现 `[下载目录] ...`（`DownloadManager.LoadDownloadsAsync` 触发）且**无**任何「加载失败」行；页面正常上屏。
