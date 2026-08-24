# EdgeRebuild 改进路线图

来源：旧版 Edge（EdgeHTML/Spartan）与 Metro IE（Win8/8.1）设计对标分析。
状态标记：[ ] 待办 / [x] 完成 / [-] 暂缓（含原因）

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
- [ ] P2-11 集锦 Collections（拖拽收集链接/图片/文本成组导出）——最大单项，建议独立规划：新表 + Hub 第五区 + 拖拽协议 + 导出
- [ ] P2-12 垂直标签试验（TabManager 已解耦容器；需布局切换开关与标签条双宿主方案）
- [x] P2-13 Metro 窗口 InPrivate 开关（设置面板→隐私；IsInPrivateWindow 驱动，新标签生效、不持久化）

## AppBar 与 Win8.1 差异对齐
- [x] A1 布局：由六等分平铺改为「左侧全局命令 / 右侧上下文命令」分组（真 8.1 规范）
- [x] A2 按钮形态：图标+文字横排 → 8.1 式「图标上、标签下」垂直组合
- [x] A3 底缘上滑手势唤出（根网格触点跟踪，起点在窗口底缘 24px 内上滑 >40px）
- [x] A4 [-] Win+Z 键盘唤出：**放弃**——Win11 中 Win+Z 被系统抢占为贴靠布局，无法可靠拦截
- [x] A5 [-] 超时自动隐藏：**不加**——真实 8.1 为 light-dismiss（点外即收），我们已一致

## 多窗口稳定性（AppWindow 模型 + 会话槽位化；真·多视图实验结论见下）
- [x] MW0 **真·多视图实验（已回退）**：CoreApplication.CreateNewView 在本机环境
      TryShowAsStandaloneAsync 无限挂起、SwitchAsync 无法上屏、DisableSystemViewActivationPolicy
      无效——窗口永不显示。结论：UWP 多视图在此机器/运行时组合不可用；
      **迁移 WinUI 3（每窗口原生 HWND + AppWindow.Activated 原生事件）是获得真正多窗的正途**。
- [x] MW1 快捷键焦点守卫：AppWindow 同线程模式采用「GotFocus 上报槽位 + WindowManager
      广播 + IsFocusedWindow 双重判定」（失活窗口焦点元素残留问题）
- [x] MW2 关闭副窗口禁用全局快捷键：核查后**非缺陷**——ShortcutManager.Enable/Disable 为引用计数
- [x] MW3 标签预览 Popup 锚定本页 XamlRoot（AppWindow 共享主 UI 层时必需）
- [x] MW4 会话槽位化：SessionTabRecord 加 Slot 列，每窗口独立快照；启动主窗恢复 Slot 0 后
      WindowManager.RestoreSecondaryWindowsAsync 按槽位重建副主窗口；关闭即清槽位防幽灵恢复
- [x] MW5 WindowManager 注册表：AppWindow TryCreateAsync + Closed 联动清槽位；
      槽位最小未用分配/回收
- [x] MW6 MetroPage 窗口关闭不释放 WebView：Unloaded 时 DisposeAll + 停轮询定时器
