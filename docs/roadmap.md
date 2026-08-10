# EdgeRebuild 路线图

> 记录远期/中期目标与取舍说明,供后续会话延续。当前阶段以功能迭代为主,本文件的条目为「知道了但暂不做」。

## 远期目标:多窗口迁移到 ApplicationView(CreateNewView)

**状态**:远期规划(未排期)。

**动机**:当前子窗口用 `AppWindow`(Windows.UI.WindowManagement)共享主 UI 线程,但这套 API 有硬伤:

- `AppWindowTitleBar` 在 UWP 无 `SetDragRectangles`(WinUI 3 才有),一旦 `ExtendsContentIntoTitleBar=true`,
  平台将顶部条带自动设为默认拖拽区并吞掉该区域全部指针输入 → 子窗口标签栏整排不可点
  (新建标签按钮被迫隐藏,标签切换/关闭只能靠 Ctrl+Tab / Ctrl+W)。
- 现有规避手段均不理想:标签栏下移破坏观感;不扩展标题栏则顶部多一条原生标题栏(近期临时方案)。

**目标方案**:`ApplicationView` / `CoreApplication.CreateNewView` 多视图,每窗口独立线程 + 独立 `Window`,
`ApplicationViewTitleBar.SetTitleBar` 精确圈定拖拽区,标签栏完全可点且保留沉浸式布局
(与真实 Edge 的多窗模型一致)。

**主要工程量与风险**(历史上正是这些导致 RPC_E_WRONG_THREAD 崩溃而弃用):

1. 全部静态事件跨线程封送:
   `DownloadManager.DownloadProgressChanged/DownloadStatusChanged`、`FavoritesManager.CollectionChanged`、
   `SettingsManager.SettingChanged`、`ThemeManager.ThemeChanged` 等,回调内触碰 XAML 必须
   `Dispatcher.RunAsync` 封送到对应窗口的线程。
2. `ObservableCollection` 跨线程绑定:`HistoryManager.History`、`DownloadManager.Downloads` 被多窗口
   ListView 绑定,集合变更通知跨线程会抛异常,需统一走事件 + 队列刷新。
3. `ThemeManager.ApplyTheme()` 目前直接刷全窗口控件,多线程下需改为逐窗口派发。
4. `ShortcutManager`(全局注册)、`SessionManager`(保存时机)、焦点判定 `IsFocusedWindow` 需复查。

**近期临时方案(已采纳)**:子窗口 `ExtendsContentIntoTitleBar=false` 用系统标题栏,标签栏恢复正常交互;
代价是子窗口顶部多一条原生标题栏,与主窗口观感略不一致。待 ApplicationView 迁移后消除。

## 备注

- InPrivate:目前提供「新建 InPrivate 窗口」(AppWindow)与「进入/退出 InPrivate 模式」(主窗口内)双入口;
  迁移后窗口入口可摆脱子窗口交互限制。
- 参考实现规范见 `docs/edge20-10240-reference-design.md`。

## Metro IE(Ferry 模式)设计哲学与功能缺口映射

**状态**:已收集(2026-08,来源:B8 博客 Rob Mauceri、MSDN Metro 指南、EightForums 实测教程)。
后续 Metro 功能开发以本清单为准绳,优先级见表格。

### 设计哲学(六条)

1. **内容优先于镀铬**:全屏只有网页,一切控件靠边缘手势/右键/Win+Z 唤出;地址栏放底部,
   避免遮挡页面顶部导航区。
2. **边缘即功能**:底部滑入 = 地址栏+标签条;顶部下滑 = 标签视图(全屏缩略图);右侧 = Charm;
   左上 = 应用切换。
3. **标签是一等公民**:呈现为全屏缩略图集合而非小标签条(Metro 调研 97% 用户 ≤5 个标签)。
4. **新标签页 = 导航磁贴**:常用站点 + 固定站点 + 收藏,输入时过滤成磁贴流("固定到开始屏"取代收藏叙事)。
5. **标签级隐私**:IE10 首创 InPrivate 标签(不写历史/缓存,专属主页)。
6. **触控优先但键盘共存**:44px 命中区、滑动手势 + Win+Z/快捷键;无插件无工具栏;160ms 级滑入动效。

### 已实现对齐(现状核对)

- 全屏无镀铬、底部地址栏/标签条、右键唤出命令栏、标签缩略图网格、NtpMetro 磁贴主页 ✓
- 8.1 默认形态为「唤出式」,本实现为「常驻式」,等价于 8.1 的设置项形态,可接受

### 缺失功能与实施方案

| 优先级 | 功能 | 对齐哲学 | 方案 |
|---|---|---|---|
| 近期 | 标签视图空态升级 | ②④ | 空态显示常用站点磁贴 + 新建标签大钮(读 FavoritesManager 画磁贴) |
| 近期 | InPrivate 标签(标签级) | ⑤ | `IBrowserTab` 加 per-tab 隐私标志;`TabManager.UrlChanged` 写历史按标签判断;命令栏「更多」加入口,复用 `SetInPrivate()` 专属主页 |
| 近期 | 地址栏收藏星 + 导航磁贴 | ④ | 地址栏加收藏钮(FavoritesManager 已有);NTP 磁贴流缺入口 |
| 近期 | Ctrl+Shift+T 重开标签 | ⑥ | `TabManager.TryReopenClosedTab` 已有,Metro 键盘补一行 |
| 中期 | Win+Z 唤出命令栏 | ② | AppWindow 内可能被系统拦截,需实测;失败则以 Ctrl+Z 替代 |
| 中期 | 「始终显示地址栏和标签」开关 | ①② | 设置面板加开关:常驻 vs 唤出(当前为常驻) |
| 中期 | 底边上滑手势唤出 Chrome | ② | ManipulationDelta 判定;仅触屏有价值,低优先级 |

### 实现教训(已踩坑)

- `ItemsWrapGrid` 直接挂 `Children` 不渲染(需 ItemsControl 托管)→ 用 GridView 直挂卡片即可;勿用「ScrollViewer(HorizontalScrollMode=Disabled)+WrapGrid」:该组合的折行测量在本 AppWindow 内**首次真实测量即冻结 UI 线程**(无异常、无布局通道、心跳死),与何时首次布局无关(常驻可见/延迟可见都一样);GridView 自带 ItemsWrapGrid 折行+滚动+虚拟化,无此问题。另:不要手动给这类面板设 `Width`(0 视口下会把卡片压成 0 宽)。
- `ProgressRing` 在卡片重建后进入可见树时,显式 `Width/Height` + 构造期 `IsActive=true` 可能触发布局测量阶段 `COMException 0x8000FFFF`(crash.log 堆栈:IFrameworkElementOverrides.MeasureOverride)→ 布局中断、整层网格不渲染且调试器中断 → 不给显式尺寸(用固定尺寸包装 Grid),捕获开始时才 `IsActive=true`,结束后 `IsActive=false`+Collapsed。
- 禁用 `UIElement.UpdateLayout()` 强制布局:主窗口树存在原生元素(WebView2 等)在强制布局时测量必抛 `0x8000FFFF`(三层嵌套 MeasureOverride);开副窗口/改尺寸 → `XamlRoot.Changed` → 强制布局即触发 → 调试器中断、布局断链。强制布局一律改为读取系统布局后的值(延迟一帧无碍),或在调度器低压队列延迟执行。
- AppWindow 覆盖层:不要用 `Collapsed→Visible` 切换(延迟首次布局本身就是雷区),改为常驻 `Visible` + `Opacity=0` + `IsHitTestVisible=False`。
- UWP `RenderTargetBitmap` 无法捕获 WebView / WebView2 内容 → EdgeHTML 标签缩略图永远为空且阻塞 UI 线程,已跳过,降级 favicon 卡片。