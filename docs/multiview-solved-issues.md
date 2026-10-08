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
  - 顶部标签条行右侧预留 **142px** 给系统 min/max/close（隐藏标题栏后仍悬浮在右上角）
  - `DemoPage.xaml.cs`（`SetupTitleBar`、`ApplyAppWindowTitleBarColors`）
- **回归验证**：截图确认标签条即窗口顶部、系统按钮浮在右上角且不压 `+` 按钮；日志 `[Demo] 已隐藏系统标题栏 slot=N mode=...`。

### E2. 标签条拖拽区被 `ScrollViewer` 命中盖掉 → 拖不动窗口

- **现象**：`DRAG FAILED`，鼠标在标签条空白处拖动窗口无反应。
- **根因**：`TabBarBorder` → `ScrollViewer` → `StackPanel` 这条链路上，`ScrollViewer` 自身参与命中测试，挡住了位于其**外层**的 `TitleBarDragArea`，`Window.SetTitleBar` 拿不到命中。
- **修复**：把 `TitleBarDragArea` 矩形**放进 ScrollViewer 内容层**（`Grid` 内第一子元素），标签 `StackPanel` 放它之上——空白处命中拖拽区，标签命中自己的 `Border`。
  `DemoPage.xaml:18-40`
- **回归验证**：`DRAG OK`，窗口位移 300,120 与光标位移完全一致。

---

## F. 反向集成 review 清单（合并到 `master` 前）

分支领先 `master` **7 个提交、0 落后**，差异 8 个文件（+1273 / -151），**可直接 merge 无冲突**。但需按四类分别处理：

| 类别 | 文件 | 处理 |
|---|---|---|
| ① 基建（直接合） | `Core/NativeWindow.cs`（363 行，新增）、`Core/NativeWindowManager.cs`（243 行，新增）、`Core/WindowManager.cs`（改为委托）、`Controls/ToolbarControl.xaml.cs`（跨线程 marshal）、`Services/WindowGeometryManager.cs`（65 行，新增） | ✅ 合入 |
| ② 崩溃兜底（直接合） | `App.xaml.cs` 的 `AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`、`Window.Current.Closed` 日志 | ✅ 合入 |
| ③ Demo 状态（合前还原） | `App.xaml.cs:114-125` **注释掉的数据源加载**、`App.xaml.cs:130` **`Navigate(typeof(DemoPage))` 应改回 `MainPage`** | ⚠️ 合并前还原 |
| ④ DemoPage | `DemoPage.xaml` / `DemoPage.xaml.cs`（新增 95 + 437 行） | 待决策：作为启动页合入 / 保留但不启动 / 不合 |

**合并决策待定**，本分支暂不合并（2026-10-08 决定：先写文档）。

---

## G. 已知未结案

### G1. row0（标签条整行）鼠标点击失效 —— 未调查

- **现象**：
  - UIA `InvokePattern` 调用 `NewTabBtn` → 标签数 `标签 1 → 标签 2` ✅（证明 XAML 处理器与 `OnNewTab` 逻辑正常）
  - **真实鼠标点击** `NewTabBtn`（`SetCursorPos` 到按钮中心，坐标已核对）→ 标签数不变 ❌
  - **真实鼠标点击**标签关闭按钮 → 同样无效 ❌
  - 标签条空白处**拖动窗口正常**（`DRAG OK`，位移 300,120 与光标一致）✅
  - 底部状态条 `NewWindowBtn` 真实点击正常 ✅
- **结论**：**row0 整行（top 32px）的鼠标输入被吞掉**，row3 不受影响。
- **两个候选根因（均未验证）**：
  1. 系统 caption 拖拽覆盖层在 `ExtendViewIntoTitleBar` 模式下吞掉了整个 top 32px 的非客户区输入
  2. `TitleBarDragArea` 放进 ScrollViewer 内容层后，命中测试/父级裁剪与 `Window.SetTitleBar` 的预期不符
- **未做的验证**：`TabBarBorder` / `NewTabBtn` / `TitleBarDragArea` 三处 `PointerPressed` 诊断日志 + 真实鼠标点击 + 读 `crash.log`
- **影响**：接入 `TabDragManager`（单击切换标签）后大概率同样失效。**本轮按决定不调查**，只记录；若 Tier1 验证时撞上，记录现象后继续。

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
