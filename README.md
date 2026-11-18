# EdgeRebuild

EdgeRebuild 是一个 **Vibe Coding** 的浏览器外壳项目，目标是复刻旧版 Edge（EdgeHTML / Spartan）与 Metro IE 的风味，同时保留现代 Chromium 内核的可用性。

当前里程碑：**7.8 · R5 质量项**（R5-1 快速切换 / R5-4 无障碍与 nullable 清理，2026-11-18）

## 项目概览

- **平台**：UWP + WinUI 2（Microsoft.UI.Xaml），最低 Windows 10 1809（10.0.17763），目标 SDK 10.0.26100
- **语言 / 运行时**：C# / .NET 9，Release 使用 NativeAOT 发布
- **渲染内核**：EdgeHTML（系统旧版 `WebView`）与 WebView2（Chromium）双内核，可在运行时切换
- **数据**：SQLite（sqlite-net-pcl），保存收藏、历史、会话、下载、稍后读、集锦、搁置标签与设置

## 功能

### 双内核与标签页

- 运行时切换 EdgeHTML / WebView2；地址、前进后退、缩放、查找、阅读模式在双引擎下保持一致。
- 标签新建 / 关闭 / 重开（Ctrl+Shift+T）、拖拽排序、拖出独立窗口、固定标签、中键关闭；右键菜单支持关闭其他 / 关闭右侧、移动到新窗口、搁置、固定等。
- 标签悬停缩略图预览、可展开的标签预览带（缩略图行）、垂直标签侧栏。
- **标签页快速切换**：Ctrl+Shift+A 搜索当前窗口标签的标题 / URL，↑↓ 选择、Enter 跳转、Esc 或点击面板外关闭；焦点在网页内同样可用。
- 会话恢复：实时快照 + 启动懒加载，只为当前标签初始化引擎，后台标签首次激活时才加载；崩溃后也能恢复。
- 多窗口：拖出标签 / 菜单新建独立窗口（独立 UI 线程），快捷键带焦点窗口守卫；副窗口不参与主会话快照。
- InPrivate：独立窗口或当前窗口切换，不记录历史、不保存会话、不提供 Tab Sweep。
- Tab Sweep（标签中心）：把暂时不看的标签搁置到独立抽屉，按时间分组，可单个 / 全部还原；固定标签、最后一个标签与 InPrivate 不参与；清除浏览数据时一并清空。

### 经典 Edge 体验

- **Hub 中心**：收藏夹、历史记录、稍后读、下载、集锦五个分区，分区内搜索与空状态引导。
- **收藏夹**：无限层级文件夹、重命名 / 移动 / 递归删除、上移 / 下移、导入 / 导出 Netscape HTML、根级文件夹收藏栏菜单、打开全部 / 在新窗口打开全部；同一 URL 全局唯一，新收藏默认进根目录。
- 收藏编辑面板（地址栏星标 / Ctrl+D）：改名、选择文件夹、新建子文件夹、删除收藏。
- **新标签页**：Classic 与 Metro 磁贴两套主页；搜索建议来自历史与收藏；支持收藏磁贴种子。
- **Metro IE 窗口**：无标签条、标签缩略图网格、磁贴主页，复刻 Win8 / 8.1 IE。
- 工具栏：地址栏自动补全与搜索、中文域名 / Punycode、HTTPS 失败自动降级、安全状态、引擎切换、缩放、查找栏、网页捕获（截图批注）、大声朗读（TTS）、打印。
- 皮肤：Spartan / Modern IE 双皮肤，跟随系统浅色 / 深色；动画尊重系统“显示动画”设置，启动与会话恢复阶段不播放。
- 启动性能：设置一次性缓存、Hub / 设置按需加载、`crash.log` 记录启动计时。

### 阅读与文档

- **阅读模式**：Readability.js 提取正文，双引擎进入 / 退出，经典阅读排版。
- **EPUB 阅读器**：EdgeHTML 下通过 `ms-local-stream` 同源加载，目录 / 书签 / 搜索 / 进度 / 状态栏；WebView2 下同样可用。
- **PDF 阅读器**：WebView2 + pdf.js，单双页 / 连续滚动、适合页面 / 适合宽度、工具栏锁定 / 自动隐藏、紫色批注强调；PDF 始终使用 WebView2。
- 本地文档：菜单 / 拖放 / 地址栏 `file:///` 或 Windows 路径打开 EPUB / PDF；复制到 `LocalState\contentcache`，重启后可恢复。
- 阅读会话恢复、EPUB 书签、PDF 批注记忆。

### 数据与隐私

- SQLite 持久化：Favorites / FavoriteFolders、History、SessionTabs、SweptTabGroups / SweptTabs、ReadingList、Collections、Downloads、Settings。
- 清除浏览数据：Cookie / 历史 / 缓存等，并清空 Tab Sweep 分组。
- Do Not Track 开关：对已初始化的 WebView2 生效，新标签在初始化时统一应用。
- 后台标签挂起（WebView2）以节省内存与 CPU。

## 技术结构

```text
Core/       双引擎标签抽象、TabManager、拖拽 / 右键 / 会话 / 收藏 / 集锦 / 搁置等管理器
Services/   设置、主题与皮肤、下载、快捷键、平台兼容（1809 / 18362+）、Web 捕获等
Controls/   工具栏、Hub、设置面板、查找栏、收藏栏等自定义控件
Metro/      Metro IE 窗口与磁贴主页
Assets/     新标签页、EPUB / PDF 阅读器、错误页、图标字体与注入脚本
Themes/     Spartan / Modern IE 皮肤资源
Tests/      纯逻辑单元测试（xUnit，源码链接共享）
docs/       路线图、旧版 Edge 设计分析、问题记录
```

## 构建与运行

### 环境要求

- Windows 10 1809（10.0.17763）或更高
- Visual Studio（UWP 工作负载 + Windows SDK 10.0.26100）
- .NET 9 SDK
- Microsoft Edge WebView2 Runtime（WebView2 内核需要）

### 编译与测试

```powershell
msbuild EdgeRebuild.csproj /t:Build /p:Configuration=Debug /p:Platform=x86
dotnet test Tests\EdgeRebuild.CoreTests\EdgeRebuild.CoreTests.csproj
```

也可以用 Visual Studio 打开 `EdgeRebuild.sln`，选择 `Debug|x86` 部署到本机。项目使用临时测试证书（`EdgeRebuild_TemporaryKey.pfx`），仅供本地调试。

### 发布

Release|x86 使用 NativeAOT（`PublishAot=true`）发布，产物约 11 MB（视依赖与 SDK 版本而定）。不要手动固定 `Microsoft.DotNet.ILCompiler` 或 `RuntimeFrameworkVersion`，版本交给 SDK 成套解析；详见 `docs/roadmap.md` R2-4。

## 已知限制

- EdgeHTML 为系统旧内核，部分现代网站不兼容；可在工具栏切换到 WebView2。
- PDF 查看器依赖 WebView2 / pdf.js，EdgeHTML 标签会自动使用 WebView2 打开 PDF。
- 真实 Windows 10 1809 设备回归仍在进行（代码最低目标为 17763）；复验步骤见
  `docs/win10-1809-regression-checklist.md`。
- Tab Sweep 缩略图复用预览缓存；重启后未命中缓存的卡片显示骨架图。
- 会话快照为单份，副窗口不写入主会话。
- 双击标签栏空白处新建标签尚未实现。

## 路线图

近期工作与状态见 `docs/roadmap.md`。近期计划：

- R5-4 质量项：图标按钮无障碍名称、Hub 键盘导航、nullable 警告清理已完成；真实 1809 复验按
  `docs/win10-1809-regression-checklist.md` 执行。
- R1-1 / R1-2：多窗口回归清单与关闭链观察。

暂缓：PDF 朗读、Sweep 整组重命名 / 共享、经典皮肤、扩展体系、独立 PDF 进程（见 R4-4）。

## 许可

见 `LICENSE.txt`。
