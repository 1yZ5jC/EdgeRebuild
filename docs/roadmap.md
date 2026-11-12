# EdgeRebuild 改进路线图

来源：旧版 Edge（EdgeHTML/Spartan）与 Metro IE（Win8/8.1）设计对标分析。
状态标记：[ ] 待办 / [x] 完成 / [-] 暂缓（含原因）

## 下一阶段开发计划（2026-08-24 制定）
### R1 稳定化收尾（当前批次）
- [ ] R1-1 多窗口回归清单执行：主/副/Metro/InPrivate 开关、槽位恢复、快捷键路由、关闭链日志全绿
      （2026-11-05 已完成 17763 兼容模式下的主/副/Metro/InPrivate/槽位恢复/Ctrl+T/W 路由验证，
       修复副窗口快捷键跨线程 0x8001010E；标准模式补齐 Ctrl+L/F/G/H/J/D/Tab/1/2/Shift+T/R/F5 矩阵，
       修复 Ctrl+J 选中“稍后读”的索引缺陷与退出 InPrivate 残留两个标签的问题；
       修复标签悬停预览 DataWriter 流释放导致截图空白，新标签页不再跳过 NTP，
       Metro 网格恢复 EdgeHTML 标签缩略图（用户实测普通网页/NTP/Metro 均正常）；
       待真实 1809、Ctrl+Tab 内容焦点 accelerator 改造、Ctrl+P 与 Ctrl+Shift+M/S 手动确认）
- [ ] R1-2 关闭链观察期：收集 `[窗口关闭]` 异例行一周，确认 WebView2 销毁竞态已绝迹
- [x] R1-3 存量警告清零：FindViewItem 返回类型改 `TabViewItem?`（连带 4 处调用点判空）、
      HistoryTemplateSelector 加 partial、ReaderService.ParseResult/DownloadDocToCacheAsync 返回类型可空化；
      全仓可空注解存量债（CS8618 等）另行专项，不在本项范围
- [x] R1-4 下载目录回退可视化：落入应用私有目录时 toast 告知用户一次（含真实路径指引）

### R2 工程健康
- [x] R2-1 核心服务单元测试：Tests/EdgeRebuild.CoreTests（xUnit，源码链接方式共享纯逻辑文件），
      UrlInputResolver 全矩阵 25 用例通过；运行方式 `dotnet test Tests/EdgeRebuild.CoreTests`
- [x] R2-2 轮询定时器治理：pageKeyTimer / ctxPollTimer 仅在活动标签为 EdgeHTML 时运行，
      随 TabChanged / 清空启停（SyncPageKeyPolling / SyncCtxPolling）
- [x] R2-3 [-] WinUI 3 迁移：**因项目设计原因永久排除**（不评估、不规划）
- [x] R2-4 NativeAOT 版本策略根治：移除显式 `Microsoft.DotNet.ILCompiler` 固定与 `RuntimeFrameworkVersion`，
      运行时包与 ILCompiler 全部交给 SDK bundled 版本成套解析（SDK 10.0.403 → 均为 9.0.22）；
      修复 `Overriding System.Private.CoreLib.dll with a newer version is not supported`，
      以后升级 .NET 9 补丁只需升级 SDK，不再需要手工同步两个版本号

### R3 功能演进（原 P2 遗留 + 新增）
- [x] R3-1 集锦 Collections：CollectionGroup/CollectionItem 双表 + CollectionManager 单例 + Hub 第五区
      （分组列表→条目列表→右键添加→导出 Markdown/HTML）+ TabContextMenuManager/MetroPage 右键入口
- [x] R3-2 垂直标签试验：RootGrid 双列 + VerticalTabSidebar（200px）+ 菜单切换，标签在水平/垂直面板间迁移
- [x] R3-3 命令栏窄窗自适应：ToolbarControl VisualStateManager（≥720 全显 / 540-719 隐藏引擎选择器 /
      <540 再隐藏收藏+捕获按钮），SizeChanged 触发状态切换
- [x] R3-4 EdgeHTML 能力对齐再评估：结论——EdgeHTML 引用 21 文件（EdgeHtmlTab 412 行 vs WebView2Tab 895 行），
      维护面可控；Metro IE 强依赖 EdgeHTML（ms-appx-web 协议），不裁剪

### R4 旧版 Edge 设计对标（2026-11-07 记录，详见 `docs/edge-legacy-design-analysis.md`）
- [x] R4-1 快速收益：Hub 五区空状态（复用 `HubEmptyState`：线框图标 + 一句说明 + 一个主行动，
      搜索无结果切“清除搜索”）、菜单快捷键标注（Ctrl+P/F/Shift+S/H/Shift+T，只标真实注册项）+
      “缩放 N%”标题、PDF 适合页面/适合宽度两态、历史条目域名色块（首字母 + 同域名稳定配色，
      标题/域名双行 + 右侧时间）
      （2026-11-08 完成；用户实测功能已实现，待真实 1809 环境复验）
- [ ] R4-2 标志性功能：标签页中心 + Tab Sweep MVP（搁置/整组还原/删除/空状态/时间桶/持久化/无障碍）
- [x] R4-3a 阅读器视觉对齐 + 活动标签提示条（2026-11-12）：
      EPUB 阅读器与 PDF 阅读器统一为 40px 阅读栏 / 36×32 按钮 / 200px 侧栏 `#F7F7F7` /
      24px 状态栏 / 2px 进度线 / 章节圆点并入状态栏；EdgeHTML 下改用 `ms-local-stream`
      同源加载 reader 资源与虚拟 `book.epub`，规避 ms-appdata XHR 封锁；修复搜索
      （`book.resolve`）、书签面板入口（“添加当前页书签”）；活动标签水平 2px 顶部、
      垂直 3px 左侧强调条，亮/暗/ModernIE 均验证；恢复标签栏与工具栏之间整窗宽淡灰分割线，
      地址栏保持 40px 满高。Release|x86 NativeAOT 发布通过。
- [ ] R4-3b 打磨剩余：标签预览带、PDF 单双页/连续滚动 + 工具栏锁定、捕获/批注紫色强调、Hub 窄窗溢出
- [x] R4-3c 本地文档打开 + 阅读器会话恢复（2026-11-12）：
      新增 `DocumentOpenService` 统一识别网络/本地 PDF·EPUB，先下载或复制到
      `LocalState\contentcache`（按来源哈希命名，避免互相覆盖），再交给 EdgeHTML EPUB 阅读器
      或 WebView2 PDF/EPUB 查看器；PDF 始终强制 WebView2（EdgeHTML 无法运行 pdf.js）。
      阅读器标签恢复解析 `src`/`cache`/`file` 后重建缓存与查看器地址，不再直接 Navigate
      旧的 `ms-local-stream` / `pdfviewer.local` 地址。
      入口：菜单“打开本地文件…”（FileOpenPicker）、拖放到窗口非网页区域、地址栏
      `file:///` 与 Windows 路径（含空格、UNC）；网页内容区保留给页面自身拖放（如上传）。
      已验证本地 EPUB/PDF 打开、重启恢复、拖放（非网页区域）；单元测试 31 项通过；
      Release|x86 NativeAOT 发布通过（约 11.3 MB）。
- [-] R4-4 暂缓：PDF 朗读、Sweep 重命名/共享整组/首次引导动画、经典皮肤、扩展体系/独立 PDF 进程
      （原因：成本/收益不匹配，或与现有 WebView2/pdf.js 架构冲突）
- [ ] R4-5 待决策：Tab Sweep 高保真复刻 vs 产品化“标签页中心”；先 quick wins vs 先 Tab Sweep；
      固定标签是否排除；InPrivate/多窗口的搁置策略

## P0 — 高价值低成本
- [x] P0-4 多主窗口会话覆盖隐患：`SessionTabs` 单快照表，两个主窗口互踩（D1 同款）。方案：副窗口 `ParticipatesInSession=false`
- [x] P0-1 阅读列表（旧版 Edge Reading List）：Hub 第四区 + `ReadingListManager`（复刻 FavoritesManager 模式）+ Metro「更多→稍后读」
- [x] P0-2 选中文本→搜索引擎：TabContextMenuManager Selection 组加「用 {引擎} 搜索」
- [x] P0-3 主窗口标签悬停缩略图预览：300ms 防抖 + WebCaptureService 捕获 + 按标签缓存
- [x] P0-5 阅读视图字号/间距可调（ReaderService 注入参数化 CSS）

## P1 — 中成本高还原度
- [x] P1-6 Metro 底栏滚动感知自动隐藏（WebView2 滚动桥 150ms 节流回报；下滚收起、上滚/近顶/切标签/开面板恢复；EdgeHTML 不支持已注明）
- [x] P1-7 磁贴主页 ↔ 收藏夹联动：设置→常规「磁贴主页数据源=来自收藏夹」时，ntpmetro 每次加载注入前 8 个收藏覆盖磁贴（NtpTileSeeder，双引擎挂钩；镜像语义，页内编辑下次加载被覆盖）
- [x] P1-8 系统分享 DataTransferManager.ShowShareUI（主窗口网页右键「分享页面」+ Metro「更多→分享」；AppWindow 不支持时降级 toast）
- [x] P1-9 后台播放音频的标签显示喇叭指示（WebView2 音频桥；仅主窗口标签条；EdgeHTML 无消息通道不做，已知限制）
- [x] P1-10 标签挂起显性化：设置→性能加挂起排除名单（URL 关键词）；tooltip 注明收益与自动恢复

## P2 — 战略级
- [x] P2-11 集锦 Collections → 已合并至 R3-1（新表 + Hub 第五区 + 右键菜单 + 导出）
- [x] P2-12 垂直标签试验 → 已合并至 R3-2（RootGrid 双列 + VerticalTabSidebar + 菜单切换）
- [x] P2-13 Metro 窗口 InPrivate 开关（设置面板→隐私；IsInPrivateWindow 驱动，新标签生效、不持久化）

## AppBar 与 Win8.1 差异对齐
- [x] A1 布局：由六等分平铺改为「左侧全局命令 / 右侧上下文命令」分组（真 8.1 规范）
- [x] A2 按钮形态：图标+文字横排 → 8.1 式「图标上、标签下」垂直组合
- [x] A3 底缘上滑手势唤出（根网格触点跟踪，起点在窗口底缘 24px 内上滑 >40px）
- [x] A4 [-] Win+Z 键盘唤出：**放弃**——Win11 中 Win+Z 被系统抢占为贴靠布局，无法可靠拦截
- [x] A5 [-] 超时自动隐藏：**不加**——真实 8.1 为 light-dismiss（点外即收），我们已一致

## 多窗口稳定性（AppWindow 模型 + 会话槽位化）
- [x] MW0 **真·多视图 CreateNewView 已打通（2026-10-08，分支 `feature/multi-window-rewrite`）**：
      此前判定「UWP 多视图在此机器不可用、须迁 WinUI 3」的结论**作废**——当时被两个 bug 假阻塞：
      ① `viewId` 用 `-1` 哨兵而 `ApplicationView.Id` 是负数，`TryShowAsStandaloneAsync` 从未执行；
      ② `CreateNewView` 线程无 `SynchronizationContext`，`await` 续体掉线程池碰 XAML 即
      `RPC_E_WRONG_THREAD` 静默崩溃。修复后原生多视图窗口上屏、关闭、清理全链路验证通过。
      详细问题清单（15 条：现象/根因/修复/回归验证 + 反向集成 review 清单）见
      **`docs/multiview-solved-issues.md`**。当前 `PreferNativeView = true`，
      AppWindow 同线程方案保留为回退分支。

- [x] MW1 快捷键焦点守卫：AppWindow 同线程模式采用「GotFocus 上报槽位 + WindowManager
      广播 + IsFocusedWindow 双重判定」（失活窗口焦点元素残留问题）
- [x] MW2 关闭副窗口禁用全局快捷键：核查后**非缺陷**——ShortcutManager.Enable/Disable 为引用计数
- [x] MW3 标签预览 Popup 锚定本页 XamlRoot（AppWindow 共享主 UI 层时必需）
- [x] MW4 会话槽位化：SessionTabRecord 加 Slot 列，每窗口独立快照；启动主窗恢复 Slot 0 后
      WindowManager.RestoreSecondaryWindowsAsync 按槽位重建副主窗口；关闭即清槽位防幽灵恢复
- [x] MW5 WindowManager 注册表：AppWindow TryCreateAsync + Closed 联动清槽位；
      槽位最小未用分配/回收
- [x] MW6 MetroPage 窗口关闭不释放 WebView：Unloaded 时 DisposeAll + 停轮询定时器

## 多窗口 Demo 功能恢复（2026-10-08 起，分支 `feature/multi-window-rewrite`）

计划与逐条问题记录见 **`docs/multiview-solved-issues.md`**（E/C/G/I 各节）。

- [x] Phase 0 文档：`docs/multiview-solved-issues.md` 新建 + MW0 改写为「已打通」（Test8）
- [x] Phase 1 Tier1 标签条：`LayoutManager` + `TabDragManager` + `TabContextMenuManager` 接回；
      标签条行改 4 列 `Auto/Auto/Auto/*` + 溢出左右滚动按钮 + `DragCanvas` + 垫底 `TitleBarDragArea`；
      真实鼠标验证通过（新建/右键 7 项/拖拽排序/拖出/标题栏拖拽），详见 I 节
- [x] Phase 2 数据源加载：还原 `App.xaml.cs:114-125` 注释（5 项各自 try/catch 降级；启动日志无「加载失败」）
- [x] Phase 2.5 启动页改 `MainPage`（`App.xaml.cs:131`）+ 主菜单加「打开多窗口测试页」进 Demo；
      重启后 `RestoreSecondaryWindowsAsync` 生效（启动页是 Demo 时该路径从不执行）。验证见 F 节
- [ ] Phase 3 悬停缩略图预览 + Pin 固定标签（`MainPage.xaml.cs:1367-1451`、`TogglePinTabAsync`）
- [ ] Phase 4 收藏夹栏（→5 行布局）+ 快捷键（`ShortcutManager` 按页 KeyDown）
- [ ] Phase 5 垂直标签侧栏（`VerticalTabSidebar` + `ApplyVerticalTabs`）
- [ ] Phase 6 Hub 面板 / 网页捕获 / 查找栏

**已知阻塞 G2 — 已解除（2026-10-09）**：MainPage 副原生视图 `MeasureOverride` 抛 `0x8001010E` 崩溃。
根因是 **WinUI 2（`muxc:`）控件不支持多 UI 线程**（首个使用线程独占），已把 `HubPane`/`SettingsPane` 的
`muxc:NavigationView` 换成 UWP 自带 `NavigationView` 并验证 3 个副窗口 + Hub 面板均正常。
详见 `multiview-solved-issues.md` G2 节。

> **硬约束**：`MainPage`/`HubPane`/`SettingsPane`（及任何会进副窗口的页面）**不得新增 `muxc:` 元素**，
> 否则多开一个 MainPage 窗口即重现 G2 崩溃；`App.xaml` 里的 `muxc:XamlControlsResources` 只作资源字典保留。
