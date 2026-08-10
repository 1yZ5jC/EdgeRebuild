# Edge 20 (10240) 外壳参考设计规范

> 来源:RinGO-Edge_Chromium 仓库 `Product/` 目录(Edge 20 / Windows 10 10240 外壳原型)。
> 本文档将参考实现的设计与行为提炼为规范清单,供 EdgeRebuild 对照实施。
> 注意:规范描述的是参考实现的行为与外观,不约束 EdgeRebuild 的实现技术(EdgeRebuild 为 WinUI 3 宿主,非 WinForms)。

---

## 1. 文档目的与来源

参考实现为「RinGO Edge」原型:WinForms 无边框窗口 + WebView2 承载 HTML/CSS/JS 外壳,每个标签页一个独立 WebView2。
源码布局为扁平结构,无 MSBuild 工程,由 `build.ps1` 使用 .NET Framework 4.0 csc.exe 直接编译。

涉及文件:

| 文件 | 行数 | 职责 |
|---|---|---|
| `Program.cs` | 4740 | `EdgeWindow`(窗口)、全部桥接处理、WebView2 接线 |
| `PrintWindow.cs` | 477 | 打印预览窗口 |
| `ProfileStore.cs` | 950 | 配置持久化(settings/history/favorites/downloads/passwords) |
| `BrowserFavoritesImporter.cs` | 318 | 收藏夹导入(Chromium/IE/Firefox) |
| `index.html` / `app.js` / `app.css` | 36KB / 135KB / 69KB | 外壳 UI(标签栏、工具栏、中心面板、菜单) |
| `note.js` / `reading-view.js` / `media-player.js` / `print.js` | — | 内容页注入脚本 |
| `content-*.html` | — | 新标签页 / InPrivate / 错误 / 离线 内容页 |

部署为平铺文件(不内嵌资源):`bin\ui\`(外壳)、`bin\content\`(内容页)、`bin\content\errorpages\`(完整 Edge 错误页集)、`bin\assets\`(Logo)。

---

## 2. 总体架构

### 2.1 分层

三个 WebView2 层次,共享同一个 `CoreWebView2Environment`(`userDataFolder = %LOCALAPPDATA%\RinGOEdge\WebView2`):

1. **外壳 WebView**(shell WebView):占满整个客户区,加载 `https://edge10240.local/index.html`(虚拟主机映射 `edge10240.local` → `ui\` 目录)。整个 UI(标签条、地址栏、窗口控制、中心面板、菜单)都是 HTML/CSS。
2. **内容宿主**(contentHost `Panel`):位于 76px 外壳条下方的矩形区域;每个标签页一个 WebView(`DockStyle.Fill`,由 `ActivateContentTab` 切换可见性),加载 `https://edge10240-content.local/<page>`(映射 → `content\`)。`navigationCover` 面板(浅灰 ~#F2F2F2)在导航期间覆盖显示。
3. **启动画面**:蓝色 TableLayoutPanel + `edge-logo-white.png`,外壳 WebView `NavigationCompleted` 后至少保留 700ms。

### 2.2 窗口尺寸与布局

- `FormBorderStyle.None`,手动起点,最小 720×480,初始为工作区的 5/8 宽 × 9/10 高,居中。
- **`Padding = 1` = 1px 主题色边框**:`BackColor` = DWM 强调色(读自 `HKCU\Software\Microsoft\Windows\DWM\ColorizationColor`,失败回退 `#0078D7`)。
- 最大化从不使用 `WindowState.Maximized`;而是 `maximizedToWorkingArea` 填充工作区 + `Padding = 0`。
- 布局/DPI(`UpdateNativeLayout`、`ScaleForCurrentDpi`):外壳条高度 = 76 + (收藏夹条 44) + (查找栏 44) 逻辑像素,按 `GetDpiForWindow` P/Invoke 缩放(四舍五入 ×dpi/96)。
- 固定中心面板/设置后,内容宽度减 `HubWidth = 360`。

### 2.3 命中分层(TabInfo 关键机制)

`UpdateShellRegion` 给整客户区的 `webView` 设置 `Region`,由以下区域并集构成:

- 外壳条(shell strip)
- 7px(`ResizeBorder`)左/右/下缩放条
- 浮动便笺选择器(180×330)
- 固定的中心面板列 / 下载栏(62px,最大 960 宽)/ 查找选项矩形

仅这些区域的点击才到达 WebView2,其余点击落在原生窗口上——这是替代 `WS_THICKFRAME`/`WM_NCHITTEST` 的机制。

### 2.4 移动 / 缩放 / 贴靠

- `WndProc` 仅拦截 `WM_DWMCOLORIZATIONCOLORCHANGED`(0x0320 → 刷新强调色 → `SendWindowState`)与 `WM_DPICHANGED`(0x02E0 → 重新布局)。
- 移动/缩放流程:JS 发 `window/drag` 或 `window/resize-<edge>` → `GetResizeHit` 映射为 `HT*` 常量(HtCaption=2, HtLeft..HtBottomRight=10..17)→ `BeginNativeMove(hitTest)`: `ReleaseCapture()` + `SendMessage(Handle, WM_NCLBUTTONDOWN=0x00A1, hitTest)` 走原生模态移动/缩放循环 → 循环后 `ApplyEdgeSnap`/`ApplyVerticalEdgeSnap` 实现 Aero 贴靠(左右半屏、四角 1/4、顶部 12px 阈值 → 最大化到工作区)。
- `ToggleFullscreen`:全屏用屏幕边界,记录 `fullscreenRestoreBounds` / `fullscreenWasMaximized`。
- `DetachContentTabAsync`:在光标处另开新 `EdgeWindow`。

### 2.5 键盘与关闭流程

- `KeyPreview` + `ProcessCmdKey`:F11、F12、Ctrl+L、Ctrl+F、Ctrl+P、Alt+←/→、F5;F7 切换光标浏览。
- 关闭:`OnFormClosing` 取消用户关闭,发 `{"type":"request-close"}`;JS 必须回 `window/close-confirmed` → `closeConfirmed=true; Close()`;随后 `SaveLastSessionPage()` 把最后 URL(新标签页为 `edge://newtab`)写入 `settings.json`。
- `Application.OpenForms` 用于跨窗口通知(`NotifySettingsChanged`、`NotifyPasswordsChanged`)。

---

## 3. 桥接协议(原生 ⇄ JS)

### 3.1 传输与序列化

- 传输:`WebMessageReceived` / `PostWebMessageAsJson`,外壳与每个标签页内容 WebView 都有;统一用 `JavaScriptSerializer`(System.Web.Extensions)。
- JS→C# 消息形状:`UiMessage { scope, action, tabId, theme, overlay, url, title, id, ids, domain, favicon, color, destination, folderId, key, value, zoom, activate, enabled, username, password, matchCase, wholeWord, left, top, width, height }`。
- 外壳消息做来源(m)校验:`https://edge10240.local`。

### 3.2 JS → 宿主(scope/action 全清单)

| Scope | Actions |
|---|---|
| `window` | `minimize`, `toggle-maximize`, `toggle-fullscreen`, `drag`, `note-toggle`, `note-command`(value: pen/高亮/eraser/text/clip/save/exit/clear/color/width,回执转给内容标签页 `note-command`), `close-confirmed`, `new-window`, `new-inprivate-window`, `print`, `pin-to-start`(tabId,title,url), `devtools`, `open-internet-explorer`(tabId,url), `resize-left\|right\|top\|top-left\|top-right\|bottom\|bottom-left\|bottom-right` |
| `shell` | `overlay-changed`(overlay ∈ menu/address-suggestions/settings/modal/security/hub/hub-pinned/note/favorite/settings-pinned → 驱动 `UpdateNativeLayout`/`UpdateShellRegion`), `note-picker-changed`, `theme-changed`, `download-shelf-changed`, `find-bar-changed`, `find-options-changed`(left/top/width/height 命中区域) |
| `settings` | `refresh`, `manage-passwords`, `set`(键: show-favorites-bar, theme, startup-mode, startup-page-kind, startup-url, startup-urls(JSON), new-tab-mode, search-engine, show-search-suggestions, search-provider-upsert/delete(JSON), reading-style, reading-size, default-zoom, show-home-button, block-popups, enable-flash, caret-browsing, offer-save-passwords, save-form-entries, send-do-not-track, cookie-policy(allow/third-party/block-all), allow-protected-media, enable-page-prediction, enable-smartscreen, enable-cortana-assistance, home-page-url) |
| `passwords` | `save`, `dismiss`, `delete`, `refresh`(按 id 匹配 `pendingPasswordOffers`) |
| `reading-view` | `toggle` |
| `tabs` | `create`, `activate`, `close`, `detach`, `navigate`, `go-back`, `go-forward`, `reload`, `home`, `zoom`, `toggle-mute` |
| `favorites` | `add`, `move`, `create-folder`, `rename`, `remove`, `set-bar`, `open-new-tab` |
| `favorites-import` | `scan`, `import`(ids 逗号分隔) |
| `reading-list` | `add`, `rename`, `remove`, `open-new-tab` |
| `history` | `remove`, `remove-many`(ids), `remove-domain`(domain), `clear-all`, `open-new-tab`, `open-new-window` |
| `downloads` | `save`, `save-as`, `cancel-prompt`, `refresh`, `pause`, `resume`, `cancel`, `remove`, `open-file`, `open-folder`, `clear-list` |
| `clear-data` | ids ∈ history/cookies/cache/downloads/autofill/passwords/site-data/settings → `CoreWebView2.Profile.ClearBrowsingDataAsync` + 存储重置,回 `clear-data-result` |
| `find` | `start`, `close`, `previous`, `next`(+value, matchCase, wholeWord)→ `CoreWebView2Find`(`CreateFindOptions`: FindTerm/IsCaseSensitive/ShouldMatchWord/ShouldHighlightAllMatches/SuppressDefaultFindDialog, `StartAsync`) |
| `inprivate` / `newtab` | `focus-address`, `restore-start` |

### 3.3 宿主 → JS(消息 `type` 全清单)

`window-state {maximized, fullscreen, accentColor}`, `window-mode {inPrivate}`, `settings-state`(约 28 个字段,含 `webViewVersion`), `favorites-state`(items+folders+showFavoritesBar), `reading-list-state`, `history-state`, `downloads-state`(项为 PascalCase `Id/FileName/SourceUrl/TargetPath/State/ReceivedBytes/TotalBytes/...` + `FileExists` + `CurrentSession`), `tab-created {tabId,title,activate}`, `tab-state {url,title,loading,canGoBack,canGoForward,zoom,favicon,secure,securityHost,playingAudio,muted,readingAvailable,readingActive}`, `find-state {tabId,matchCount,activeMatchIndex}`, `focus-address {inPrivate,newTab}`, `open-find`, `request-close`, `note-closed`, `download-prompt {id,fileName,sourceUrl,totalBytes}`, `download-prompt-closed`, `passwords-state`, `password-save-offer`, `favorites-import-sources`, `favorites-import-result {count}`, `clear-data-result`, `note-result`, `note-clip-result`, `note-command`。

内容页另有:`theme {theme}`。打印窗口:`print-init`, `print-page-count`, `print-result`。

对外暴露的 JS 对象:`window.edgePrototype = { requestCloseFromHost, setMaximized }`(经 `chrome.webview` API)。

### 3.4 内容页消息与脚本注入

- 特权内容消息(免来源校验两类):`password-capture` scope(注入的 `window.__ringoPasswordCaptureEnabled/_Installed` 表单监视脚本捕获 submit/click → 建 `PendingPasswordOffer`,仅查询 origin,不回显密码,发 `password-save-offer` 给外壳)与 `note` scope(`save`/`clip`/`close`:滚动截图 PNG 写入 `%LOCALAPPDATA%\RinGOEdge\Notes\<guid>.html`,回 `note-result`;`clip` 按 DPI 缩放视口裁剪并设剪贴板(Bitmap + "PNG" DataFormats),回 `note-clip-result`)。
- 内部内容来源(origin `edge10240-content.local`):`tabs/navigate`, `error/retry|search|go-back|open-network-settings|open-proxy-settings|continue-certificate`(`continue-certificate` 设置 `tab.CertificateBypassUrl`)。
- 注入脚本:`note.js`(`window.__edgeWebNote.toggle()`)、`reading-view.js`(`__EDGE_READER_COMMAND__` JSON 替换,detect/enter/exit/update + style/size)、主题(`document.body.dataset.theme`)、光标浏览(`__ringoCaretBrowsingEnabled`, `selection.modify` 映射)、密码捕获脚本、`media-player.js`(`AddScriptToExecuteOnDocumentCreatedAsync`)。
- 异步模式:处理器全部 `async void`,C# 从不阻塞,全部以 JSON postMessage 推送回执;仅 `ExecuteScriptAsync` 的字符串结果("true"/"false")回流(阅读可用性、`navigator.onLine`、错误页健康)。

---

## 4. 配置文件规范

### 4.1 文件与格式

目录 `%LOCALAPPDATA%\RinGOEdge\Profile\`:

- `favorites.json` — `FavoritesDocument{Version=8, Items, Folders}`
- `history.json` / `reading-list.json` / `downloads.json` — `ProfileDocument<T>{Version, Items}`
- `settings.json` — 裸 `BrowserProfileSettings`
- `passwords.json` — 密码记录

记录结构:

- `FavoriteRecord {Id, Title, Url, Favicon, FolderId, CreatedAt, UpdatedAt}`
- `FavoriteFolderRecord {Id, Name, ParentId, ...}`(特殊文件夹名 `"favorites-bar"` 对应收藏夹栏)
- `HistoryRecord {..., VisitCount}`, `ReadingListRecord {...}`, `DownloadRecord {..., ReceivedBytes/TotalBytes, State}`
- `PasswordRecord {Id, Origin, UserName, EncryptedPassword, UpdatedAt}` — **DPAPI 加密**(`ProtectedData.Protect(..., DataProtectionScope.CurrentUser)`,base64)

### 4.2 保证与限制

- 上限:历史 10,000、下载 2,000、阅读列表 5,000、密码 1,000(保存时裁剪)。
- **原子写入 `TryWriteJson`**:写 `path.<guid>.tmp` 后 `File.Replace`/`File.Move`,UTF-8 无 BOM;全程 `syncRoot` 锁。
- 损坏/缺失文件降级为默认值;`NormalizeSettings` 按版本门控默认值(`CurrentVersion=8`):v3 弹窗/Flash、v4 密码提议、v5 表单条目/DNT/Cortana、v6 Bing+Baidu 搜索提供程序、v7 搜索建议、v8 Cookie 策略/受保护媒体/预读/SmartScreen。
- 启动模式 `start|new-tab|previous|specific`;启动页类别 `msn|bing|custom`。
- `RunSelfTest()` 供 `--self-test`(退出码 0/4)。
- 访问器返回深克隆(`CloneItems`,重新序列化)。

### 4.3 收藏夹导入

`BrowserFavoritesImporter.Scan()` 枚举:

- Chromium 书签:`%LOCALAPPDATA%\<Edge|Google\Chrome|Chromium|BraveSoftware\Brave-Browser|Vivaldi|\Opera Software\Opera Stable>\...\Bookmarks`(Default..Profile 5)
- IE:`%USERPROFILE%\Favorites`(`*.url`,读 `URL=` 行)
- Firefox:`%APPDATA%\Mozilla\Firefox\Profiles` 逐个 profile 的 `places.sqlite`,通过 **P/Invoke sqlite3**(`mozsqlite3.dll` 的 `sqlite3_open_v2/close/prepare_v2/step/finalize/column_int64/column_text`,查询 `moz_bookmarks`+`moz_places`)

`Import(ids)` 把 Chromium `bookmark_bar` 根映射到特殊文件夹 `"favorites-bar"`,其余归入各浏览器文件夹;按规范化 URL 去重后合并入库;回 `favorites-import-result`。

---

## 5. WebView2 接线

### 5.1 环境

一个 `CoreWebView2Environment.CreateAsync(null, "%LOCALAPPDATA%\RinGOEdge\WebView2", null)`,外壳、内容标签页、打印窗口共用。启动时要求运行时存在(否则退出码 3)。

### 5.2 外壳视图设置

`AreDefaultContextMenusEnabled=false`、`AreDevToolsEnabled=true`、`AreBrowserAcceleratorKeysEnabled=false`、`IsZoomControlEnabled=false`;`SetVirtualHostNameToFolderMapping("edge10240.local", uiDirectory, DenyCors)`。`NavigationCompleted` 后 ≥700ms 隐藏启动画面,推送初始状态(`SendWindowState/SendWindowMode/SendSettingsState/SendFavoritesState/SendReadingListState/SendHistoryState/SendDownloadsState/SendTabState`)。

### 5.3 内容标签页设置

`CreateContentTabAsync`(每个 `BrowserTab` 一个 WebView,位于 `contentHost`):`AreDevToolsEnabled=true`、`AreBrowserAcceleratorKeysEnabled=false`、`IsWebMessageEnabled=true`、`HiddenPdfToolbarItems=CoreWebView2PdfToolbarItems.None`、`IsPasswordAutosaveEnabled=false`(自有密码管理器)、`IsGeneralAutofillEnabled=SaveFormEntries`;映射 `edge10240-content.local` → `content\`(DenyCors),服务 `newtab.html`/`inprivate.html`/`blank.html`/`error.html`/`offline.html`/`errorpages\*`。新标签页源:MSN(`https://go.microsoft.com/fwlink/?LinkId=521959`),除非 `NewTabMode == "blank"`。`NavigateContentTab` 经 `blank.html` 中转,规避失败文档问题(`PendingNavigationUrl` 流程)。

### 5.4 每标签页事件

- `PermissionRequested`:仅 `*Protected*` 类,按 `AllowProtectedMedia` Allow/Deny。
- `ContextMenuRequested` → `ReplaceContextMenuPrintCommand`:把「打印」项替换为 `webViewEnvironment.CreateContextMenuItem`,另有「询问 Cortana」项(`ms-cortana` 协议存在时)。
- `ServerCertificateErrorDetected`:对 `CertificateBypassUrl` 一次性 `AlwaysAllow`,否则 `Cancel`。
- `NewWindowRequested`:`IsUserInitiated` 弹窗拦截,一律 `Handled` → 新外壳标签页。
- `WebResourceRequested` 过滤器 `"*"`/All:加 `DNT: 1`;`third-party`/`block-all` 策略剥 `Cookie`;拦截推测性预取(`Purpose`/`Sec-Purpose` 头);`.swf` Flash 用 `CreateBlockedResourceResponse`(403 text/plain);`IsThirdPartyRequest` 主机后缀判断。
- `DownloadStarting` / `IsDocumentPlayingAudioChanged` / `IsMutedChanged` / `Find.MatchCountChanged` / `ActiveMatchIndexChanged` / `NavigationStarting` / `SourceChanged` / `DocumentTitleChanged` / `FaviconChanged` / `HistoryChanged` / `NavigationCompleted`。

### 5.5 错误页管线

`ShowNavigationErrorPageAsync`:在线探测(`NetworkInterface.GetIsNetworkAvailable` + `navigator.onLine`)→ `ResolveErrorPageName(CoreWebView2WebErrorStatus, httpStatusCode, online)`:

| 条件 | 页面 |
|---|---|
| 离线 | `offline.html` |
| HTTP 400/403/404/406/410/500/501 | `http_xxx.htm` |
| 证书类 | `invalidcert.htm` |
| 代理 | `proxyerror.htm` |
| 超时 | `servbusy.htm` |
| 取消 | `navcancl.htm` |
| 服务器响应坏 | `tlserror.htm` |
| DNS/连接 | `dnserror.html` |
| 其他 | `error.html` |

错误页以 `https://edge10240-content.local/<page>#<转义后的失败 URL>` 导航;`IsErrorPageDocumentHealthyAsync` 健康检查,失败则 `FallbackToStableErrorPageAsync`(重试 `offline.html`/`error.html` 一次)。

### 5.6 下载与打印

- 下载:拦截 `CoreWebView2DownloadStartingEventArgs` 后 `Handled` + `GetDeferral()`,入队 `PendingDownloadPrompt{Id, FileName, EventArgs, Operation, Deferral}`,发 `download-prompt`;`save` 在 `%USERPROFILE%\Downloads` 生成去重路径 `"name (n).ext"`(查文件系统 + 记录),`save-as` 用 `SaveFileDialog`;`ResolvePendingDownload` 后 `Deferral.Complete()`。`BytesReceivedChanged`/`StateChanged` → `UpdateDownloadRecord` 状态机(下载中/已暂停/恢复中/已完成/已取消/失败),持久化到 `downloads.json`;窗口关闭时 `CancelPendingDownloads`。
- 打印:`PrintWindow`(752×790,黑底)复用共享环境;Ctrl+P 与替换后的右键菜单项都走此路;`CallToPdfStream` → `tab.View.CoreWebView2.PrintToPdfStreamAsync(settings)`。

---

## 6. UI 设计规范

### 6.1 DOM 层级

`<body>` 以 data 属性承载全部外壳状态:`data-shell="true"`、`data-maximized`、`data-fullscreen`、`data-theme="dark"`、`data-inprivate`、`data-inprivate-address`、`data-newtab`、`data-newtab-address`、`data-note`、`data-favorites-bar`、`data-hub-pinned`、`data-settings-pinned`、`data-find-bar`、`data-addressState`(`getAddressInputState()` 填充)。

```
section.browser-window            (position:fixed; inset:0; z-index:2)
├─ header.tab-bar                 高 32px,底色 #cccccc
│  ├─ div.tabs[role=tablist]
│  │  └─ div.tab[data-tab-id]      宽 200px;非激活 #d1d1d1/#505050,激活 #f2f2f2/#171717
│  │     ├─ button.tab-main[role=tab] → span.page-icon / span.tab-audio(静音)/ span.tab-title
│  │     └─ button.tab-close.mdl-glyph (&#xE8BB;)
│  ├─ button.new-tab.mdl-glyph      (&#xE710;,42×32)
│  ├─ div.drag-region               (弹性占位,窗口拖动)
│  └─ div.caption-buttons → .minimize-window/.maximize-window/.close-window
│                                  (46×32;关闭悬停 #e81123)
├─ nav.toolbar                      高 44px,底色 #f2f2f2(便笺模式 → #873a86)
│  ├─ div.note-command-bar[hidden] → .note-command[data-note-command=pen|highlighter|eraser|text|clip|save|share|exit]
│  ├─ div.note-picker[hidden]
│  ├─ 工具按钮(48×43):.back-button(E72B) .forward-button(E72A)
│  │   .refresh-button(.refresh-icon E72C + .stop-icon E711) .home-button(E80F,[hidden])
│  ├─ span.address-divider
│  ├─ form.address-form → input.address-bar + 注入 button.address-security-button (E72E 锁,.address-secure 时显示)
│  ├─ div.address-suggestions[role=listbox,hidden]
│  └─ div.toolbar-actions → .reading-button(E736,不可用时禁用)
│      .favorite-button(.favorite-glyph E734→E735 已保存)
│      .hub-button(EA37) .note-button(E932) .share-button(E72D) .more-button(E712)
├─ div.favorites-bar[hidden]        44px 行 → .favorites-bar-empty + .favorites-bar-items
├─ div.find-bar[hidden]             44px → .find-input, output.find-status("x/y" 或「没有任何结果」),
│                                    .find-previous/-next(E76B/E76C), .find-options-button+菜单
│                                    (区分大小写/全字匹配), .find-close(E711)
├─ div.favorites-bar-folder-menu[role=menu]
├─ aside.more-menu[hidden] → .new-window-command, .new-inprivate-window-command,
│   .zoom-row(.zoom-out E738, output.zoom-value, .zoom-in E710), .find-page-command,
│   .print-command, .pin-start-command, .devtools-command, .open-ie-command,
│   .open-feedback, .open-settings
├─ 右缘面板(position:absolute; top:77px; right:0; 宽 360px; z-index:8; 左边框 #d0d0d0;固定时 bottom:0):
│   .settings-panel/.advanced-settings-panel/.search-provider-panel/
│   .import-favorites-panel/.settings-clear-data-panel/.feedback-panel/.password-manager-panel
│   每个有 .settings-header(44px):返回按钮(E72B)、strong 标题、.settings-pin(E718/E711 按 [data-pinned="true"] 切换)
├─ aside.hub-panel[data-pinned]     360px 右缘覆盖
│  ├─ nav.hub-tabs → .hub-tab[data-hub-page=favorites|reading-list|history|downloads]
│  │                  字形 E734/E7BC/E81C/E896 + .hub-pin(E718/E711)
│  └─ section.hub-page[data-hub-page] ×4 → header .hub-page-header,
│      列表容器(.hub-favorites-list/.hub-reading-list/.hub-history-list/.hub-downloads-list),
│      .hub-empty 文本;history 页含 section.clear-data-panel
├─ aside.favorite-flyout[hidden]    320px,top:76px right:60px → 标签页(收藏夹 E734 / 阅读列表 E7BC),
│                                   #favorite-name, #favorite-location, .confirm-favorite/.cancel-favorite
├─ .favorite-context-menu/.history-context-menu/.download-context-menu
│      (命令:open-new-tab, create-folder, rename, remove;open-new-window, remove-domain;
│       open-file, open-folder, pause, resume, cancel, remove)
├─ section.download-shelf[hidden]   底部居中 960px,上边框 2px #0078d7,z-index:16
├─ section.password-save-flyout[hidden] 320px,top:78px right:12px,z-index:14
└─ div.modal-layer[role=dialog] → .close-dialog(443px,边框 var(--windows-accent-color)) = 「关闭所有标签页」确认
```

运行时注入:`.security-panel`(网站身份浮出,追加到 `.browser-window`)、8 个 `.resize-handle`(`.resize-top/-bottom/-left/-right/-top-left/-top-right/-bottom-left/-bottom-right`,最大化/全屏时隐藏)。

### 6.2 设计令牌

**无 CSS 自定义属性色板**;唯一属性 `:root { --windows-accent-color: #0078d7; }` 由宿主运行时设置(`updateWindowState` → `document.documentElement.style.setProperty(...)`,读 DWM 窗口强调色)。其余全部为字面量 hex,由超大的 `body[data-theme="dark"]` 块(约 380 行)整体覆盖。

**字体**:`@font-face "Browser MDL2 Assets"`(BrowserMDL.ttf,weight 400);回退 `"Segoe MDL2 Assets", "Segoe UI Symbol", sans-serif`;正文 `"Segoe UI", "Microsoft YaHei UI", sans-serif`,基准字号 15px。

**浅色(默认)色板**:

| 用途 | 值 |
|---|---|
| 页面/面板底色 | `#ffffff` |
| 工具栏/收藏夹栏/查找栏/中心面板 | `#f2f2f2` |
| 标签栏 | `#cccccc` |
| 非激活标签页 | `#d1d1d1` 文字 `#505050` 边框 `#b7b7b7` |
| 激活标签页 | `#f2f2f2` |
| 文字 | `#171717` |
| 主强调色(accent) | `#0078d7`(悬停 `#0067b8`) |
| 链接 | `#0067c0`(暗色主题 `#4ca0e0`) |
| 禁用 | `#9a9a9a` |
| 悬停 / 按下 | `#d3d3d3` / `#c8c8c8`,列表项悬停 `#dedede` |
| 建议项选中 | `#acd3ee` + 3px 左边框 `#0078d7` |
| 收藏夹栏文件夹 | `#e8a900` |
| 已保存星标 | `#ffb900` |
| 错误 | `#c42b1c` |
| 关闭按钮悬停 | `#e81123` |
| 便笺模式紫 | `#873a86` |
| InPrivate 徽章 | `#0067b8` |

**暗色色板**:chrome/页面 `#171717`;标签栏 `#1f1f1f`;非激活标签 `#333333`/文字 `#d0d0d0` 边框 `#565656`;激活标签 `#171717`/文字 `#ffffff`;工具栏 `#171717` 边框 `#3f3f3f`;面板/菜单/浮出 `#202020` 边框 `#4a4a4a`;右键菜单 `#2b2b2b` 边框 `#555555`;下拉/选择器 `#242424`;弱化文字 `#bdbdbd`/`#bbb`;输入框边框 `#767676`;悬停 `#3b3b3b`,按下 `#4a4a4a`;选中 `#0078d7` + 白字;模态遮罩 `rgba(0,0,0,.72)`;开关强调 `#0078d7`。

**关键尺寸**:标签页 200px(<900px 150px,<620px 130px,<620px 隐藏 1 个之后);标签栏 32px;工具栏 44px;工具按钮 48×43(<620px 40px);地址表单 32px 高;面板 360px;下载栏 960px;关闭对话框 443px;收藏夹/密码浮出 320px;建议列表 `top:43px`,`max-height: min(330px, calc(100vh - 120px))`;缩放条 62px。

**阅读视图主题**(内联自定义属性 `--reader-bg/fg/muted/link/size`):默认 `#f4f0df/#202020/#5c574c/#0067b8`;浅色 `#ffffff/#202020/#666666/#0067b8`;复古 `#d8d2c1/#28251f/#625d52/#005a9e`;暗色 `#202020/#eeeeee/#bcbcbc/#66b8ff`。字号 小/中/大/特大 = 16/18/21/24px。

**InPrivate 视觉**:`.tabs::before` — 96×32 `#0067b8` 徽章,白字「▧ InPrivate」12px;地址栏与分隔线在 `data-inprivate-address="true"` 前为 `opacity:0`/`visibility:hidden`(然后 margin-right:22px)。新标签页模式(`data-newtab`)同理。

**便笺模式**:标签栏+工具栏整体重着色 `#873a86`,工具栏隐藏除 `.note-command-bar`/`.note-picker` 外全部子元素;标题栏悬停染 `#b7b7b7`。

**响应式断点**:900px(标签→150px,隐藏阅读/便笺/共享按钮,错误页左移 25%);620px(单标签,隐藏前进按钮,按钮 40px,面板全宽)。

### 6.3 MDL2 字形码点表

**内联 HTML 实体**:`E8BB`(关闭×)、`E710`(添加/新标签/放大)、`E72B`(返回)、`E72A`(前进/前往)、`E72C`(刷新)、`E711`(停止/清除/关闭)、`E80F`(主页)、`E736`(阅读视图)、`E734`(收藏星形描边)、`EA37`(中心)、`E932`(Web 便笺)、`E72D`(共享)、`E712`(更多)、`E718`(固定)、`E76B`(查找上一个)、`E76C`(查找下一个/右箭头)、`E738`(缩小)、`E7BC`(阅读列表)、`E81C`(历史)、`E896`(下载)、`E8FA`(账户)、`EB7C`(离线)、`EB7D`(页面错误)、`E721`(搜索)。

**CSS `content:` 码点**:`E7C3`(地球/页面图标,默认站点图标)、`E767`(播放中声音)、`E74F`(已静音)、`E70F`(笔)、`E7E6`(高亮笔)、`E75C`(橡皮擦)、`E8BD`(文本)、`E924`(剪辑)、`E74E`(保存)、`E921`(最小化)、`E922`(最大化)、`E923`(还原,`body[data-maximized="true"]` 时)、`E735`(已保存星形,金色 `#ffb900`)、`E721`(搜索)、`E72E`(锁)、`E8B7`(文件夹,金色 `#e8a900`/`#ffb900`)、`E70E`(下箭头/折叠)。JS 字符串:`'\uE70E'`, `'\uE711'`, `'\uE76C'`。

打印页另用 `E749`(打印机)、`E72B`/`E72A`(预览前后)。媒体播放器故意用纯 CSS 绘制控件(无 MDL2):强调色 `#6531d5`、底 `#252525`,进度/音量条 8px/14px,2.5s 自动隐藏,空格/←/→ 键盘,全屏用 Fullscreen API,fit/align-fill 切换。

**字体约定**:`.mdl-glyph` = `font-family:"Browser MDL2 Assets"; font-weight:400`。面板字形 22–23px,工具栏 19–20px,便笺 22px,标题栏 10px,标签关闭 10px;内联 HTML 实体按继承字号。

---

## 7. 行为清单

### 7.1 地址栏(核心)

`getAddressInputState()`(app.js 约 206 行)按正则分类输入:`focused-empty` / `focused-url` / `focused-search`。URL 判定:存在协议,或类主机形式(含 `.`、IPv6 `[...]`、点分四段、`localhost`)。
聚焦:填充 `activeAddress`,置 `addressFocusPristine=true`,全选(mouseup 在 pristine 时重选);Esc / 空值失焦恢复 `activeAddress`。
建议(`renderAddressSuggestions`):空查询 → 搜索提供程序主页 URL,标题「热门站点」;非空 → [搜索行, 标题/URL 子串匹配的历史前 3 条, 合成细化(查询词 + 官网/教程/新闻/下载)];方向键环绕索引;Enter 激活选中行 → `tabs.navigate`。`showSearchSuggestions===false` 时禁用。

### 7.2 标签页语义

- 本地 `nextTabId`(起点 2)+ `bumpNextTabId`;乐观建 UI 再发 `tabs.create`。
- 激活发 `tabs.activate` 并重跑查找。
- 关闭:最后标签 → 「关闭所有标签页」模态;否则激活 `min(index, remaining.length-1)` 邻位。
- 拖动重排按兄弟矩形中点;指针离开标签条 >12px → `tabs.detach`(若为唯一标签先建替代标签)。
- 站点图标为 `.page-icon` 的背景图;`has-audio`/`is-muted` → 字形 E767/E74F;点声音按钮 → `tabs.toggle-mute`。

### 7.3 新标签页 / InPrivate 页面

新标签页:`#f2f2f2` 底,顶部内边距 38px,账户字形 E8FA 位于 `right:13%` top 38px,居中列 `min(856px, calc(100% - 48px))`,h1「接下来做什么?」28px/400,搜索框 34px 白底 + 20px 搜索图标(E721),蓝色提交 `#1e8df7`(悬停 `#0879e6`)。提交逻辑:http(s) 原样;`\S+\.\S+` → 前缀 `https://`;否则 Bing 搜索(InPrivate 页同款启发式)。
InPrivate 页:`#f2f2f2` 底,居中卡片 856px at top 38px,h1 26px,搜索 856×32 白输入 + `#0078d7` 前往按钮,隐私文案 14px,链接 `#0067c0`;`.inprivate-compact` 类隐藏 h1+搜索并上移卡片至 top 12px(原生拥有地址栏时外壳使用)。

### 7.4 阅读视图

评分公式:`text.length + paragraphs*210 + headings*90 + semanticBonus(900) - linkDensity*2400 - bodyPenalty(500)`;要求 ≥3 个可见 `p` ≥28 字符且 linkDensity <0.52。
`cleanClone` 剥离 script/style/noscript/iframe/form/nav/aside/footer/广告/社交/评论。
覆盖层 `#edge-html-reading-view` fixed inset:0,文章宽度 `min(760px, calc(100% - 72px))`,顶部内边距 68px,行高 1.82,元信息行(来源主机 + 作者 + 日期)。
命令契约:`(function(command){...})(__EDGE_READER_COMMAND__)`,动作 `detect|enter|update|exit`;进入/退出时保存并恢复滚动与溢出。

### 7.5 Web 便笺

全尺寸画布:`#edge-web-note-canvas` + 独立高亮画布(`mix-blend-mode:multiply`,0.58 透明度)+ 笔画平滑预览画布;剪辑遮罩 `rgba(0,0,0,0.58)`。
浮动紫色 `#7b1f7f` 工具栏;工具 pen/highlighter/eraser/text/clip。
笔颜色:`['#fff','#999','#000','#ffed00','#ff9814','#599400','#16b6d6','#0e58d0','#603996','#e20082','#ef1020','#7a3f25']`;高亮颜色:`['#e5008b','#0b80cf','#12b8d8','#16f20d','#fff000','#ff8f00']`;默认笔 `#599400` 粗 2,高亮 `#0b80cf` 粗 10。
剪辑:`note.clip` 视图矩形 + 隐藏遮罩后双重 rAF 计时。

### 7.6 查找栏 / 下载栏 / 缩放

- 查找:`find` scope 的 start/next/previous/close;状态「index/count」;选项菜单经 `shell.find-options-changed` 上报几何(原生避免与弹窗重叠);宿主可经 `open-find` 打开。
- 下载栏:状态文本 下载中「X / Y - Z%」/ 已暂停「X 已暂停 - Z%」/ 恢复中 / 已完成 / 已取消 / 失败;按状态给操作按钮;关闭记录 `downloadShelfDismissedId`;居中,面板固定时左移。
- 缩放:±25 步进,钳制 25–500,`tabs.zoom` 显示。

### 7.7 设置 Schema(`browserSettings`)

version, showFavoritesBar, theme, startupMode/startupPageKind(startupUrl/startupUrls), newTabMode(top-sites-and-content|top-sites|blank), searchEngine + searchProviders[{id,name,urlTemplate}], showSearchSuggestions, cookiePolicy(allow|third-party|block-all), allowProtectedMedia, enablePagePrediction, enableSmartScreen, readingStyle/readingSize, defaultZoom, showHomeButton/homePageUrl('about:start'), blockPopups, enableFlash, alwaysUseCaretBrowsing, offerToSavePasswords, saveFormEntries, sendDoNotTrack, enableCortanaAssistance/cortanaAvailable, webViewVersion。

历史分组键:`past-hour`(「过去 1 小时」)/ `today`(「今天 - M/D」)/ `yesterday` / `date-YYYY-M-D`(「星期几 - M/D」)/ `earlier`;默认展开 `['past-hour']`;折叠/展开字形 E70E/E76C;删除整组发逗号合并 ids。

### 7.8 跨面机制

- 所有覆盖层与 `syncOverlayState()` → `shell.overlay-changed` 紧耦合,使原生宿主围绕打开的 chrome 面重排内容 WebView。
- 单个 `browserWindow` 点击外部处理器按固定顺序关闭各浮出(未固定时);doc 级 Esc 按优先级顺序关闭。
- `hideFlyouts()` 批量关闭 more-menu/settings/hub/favorite-flyout/security。
- 内容页暗色主题:`body[data-theme="dark"]`(宿主 `theme` 消息或 `?theme=` 预览参数)。
- 外壳预览参数:`?theme=dark&state=menu|settings|hub|favorite|dialog`。
- 错误/离线页 `.error-layout`:`left:35.15%; top:72px; width:620px` 弹性布局,80×88 字形(EB7D 错误 / EB7C 离线,`#171717`),h1 24px,文本链接 `#0067c0`;重试/搜索发 `error.retry`/`error.search`,`#failed-url` 由 `location.hash` 填充;暗色下文字 `#f2f2f2`、链接 `#4ca0e0`。

---

## 8. 构建与打包(参考实现)

- `build.ps1`:从 nuget 引导 WebView2 SDK 1.0.4129.50(`Microsoft.Web.WebView2.<ver>.nupkg` → `.deps\`,Expand-Archive);用 `lib\net462\Microsoft.Web.WebView2.Core.dll`、`...\WinForms.dll`、`runtimes\win-x64\native\WebView2Loader.dll`。
- 编译:.NET Framework 4.0.30319 csc.exe(Framework64 优先),`/target:winexe /platform:x64 /optimize+`,引用 System/System.Core/System.Drawing/System.Web.Extensions(JavaScriptSerializer)/System.Security(DPAPI)/System.Windows.Forms + 两个 WebView2 DLL;`/win32icon` 由 `edge-logo-blue.png` 手工转 ICO(New-PngIcon);`/win32manifest:app.manifest`(PerMonitorV2 dpiAwareness、asInvoker)。
- 部署=平铺拷贝,无内嵌;`bin\ui\`、`bin\content\`(含 error-page-manifest.json、BrowserMDL.ttf、从 `..\resources\Assets\Fonts` 拷 ReadMDL2.ttf)、`bin\content\errorpages\`(完整 Edge 错误页集递归)、`bin\assets\` + WebView2 DLL;随后 `bin\RinGOEdge.exe --self-test` 作为门禁。
- `build-package.ps1`:调 build.ps1 后 Inno Setup(`installer.iss`,x64compatible、管理员、简体中文)→ `dist\RinGOEdge-Setup-1.0.0-x64.exe`,打印 SHA256。
- `run.cmd`:bin 缺失则先构建,再 `start "" bin\RinGOEdge.exe %*`。
- `Program.Main` 启动校验:必查资源文件存在(否则退出码 2)、`Environment.OSVersion.Build >= 10240`(退出码 5)、WebView2 运行时(退出码 3)、注册 PDF「打开方式」(`HKCU\Software\Classes\Applications\RinGOEdge.exe`)、打开 `BrowserProfileStore.OpenDefault()`;处理 URI 启动参数(http/https、本地 `file://`、`--pinned-url` base64)。

---

## 9. 与 EdgeRebuild 的差异对照

| 维度 | 参考实现(应参考的行为) | EdgeRebuild 现状 |
|---|---|---|
| 宿主技术 | WinForms 无边框窗口 + FormBorderStyle.None | WinUI 3(.NET 9,net9.0-windows10.0.26100.0),打包应用(x86/x64,AppPackages) |
| 外壳 UI | 单一 HTML 外壳 WebView2 承载全部 chrome | XAML 页面 + WebView2;另有 `Metro/` 双 UI 变体(对齐参考项目之外的 Metro 形态) |
| 标签/内容 | 每个标签页一个 WebView2(DockStyle.Fill + visibility 切换) | 待对照标签页容器实现 |
| 窗口行为 | DWM 强调色 1px 边框、最大化到工作区、伪造 WM_NCLBUTTONDOWN 贴靠、Region 命中分层 | WinUI 3 原生窗口行为为主,需对照取舍 |
| 桥接协议 | JS postMessage `{scope,action}` 大清单 + 宿主消息推送 | 已有 `Services/` 层(含 URL 解析器服务、MainPage 与 Metro 地址栏共用),消息契约需逐项对齐 |
| 地址栏语义 | 无点纯词 → 搜索;有协议/点/`[...]`/点分四段/localhost → 导航;全角点转 ASCII | 已实现共享 URL 解析服务(主窗口 + Metro),与参考启发式一致 |
| 持久化 | JSON 文件 + DPAPI + 原子写 + 上限裁剪 + 版本门控默认值 | `Core/` 现有存储实现待对照补充(密码加密、版本门控等) |
| 内容页 | 虚拟主机名映射 edge10240-content.local 服务 newtab/inprivate/error/offline/errorpages 集 | `Assets/Ntp`、`Assets/NtpMetro`、`Assets/PdfViewer`、`Assets/EpubReader` 为对应内容资源 |
| 构建 | build.ps1(csc 直编 + WebView2 SDK nuget + 平铺部署) | MSBuild/解决方案 + 打包(签名的 AppX),差异较大,仅行为对齐 |
| 错误页 | offline/dns/cert/proxy/超时/取消/bad-response/http_xxx + hash #failed-url 管线 | 待对照实现 |

> 建议工作方式:以本文档第 6、7 章为验收清单(逐条勾对 UI 视觉与交互行为),第 3–5 章为桥接与存储契约,第 9 章为实施取舍记录。

---

## 10. 借鉴实施路线图(2026-08 更新)

> 结论先行:两个项目技术栈差异大(参考 = WinForms + HTML 外壳;EdgeRebuild = UWP/WinUI + XAML 控件),因此「行为层」可借鉴,「实现层」不照搬。本清单按性价比排序,并标注是否已实施。

### 10.1 不值得照搬的部分(保持现状)

- HTML 外壳、Region 命中分层、伪 `WM_NCLBUTTONDOWN` 贴靠模拟 —— WinUI 原生窗口能力已覆盖。
- JSON + DPAPI + 版本门控存储 —— EdgeRebuild 的 SQLite 单文件更优,无需改。
- 单一外壳桥接大清单 —— EdgeRebuild 为直接 C# 调用,无需桥。

### 10.2 已决定实施的部分(按优先级)

| # | 借鉴项 | 参考行为(§7 对应) | 实施要点 | 状态 |
|---|---|---|---|---|
| 1 | 自定义错误页流水线 | §7.8 + §5.5 | offline/dns/cert 等页面,`#failed-url` hash 传参,页内「重试」(导航回失败 URL)/「搜索」(URL 解析器搜索);经 `contentcache.local` 虚拟主机映射提供,双引擎分支处理 | 进行中 |
| 2 | 历史分组 + 分组删除 | §7.7 历史分组键 | HubPane 历史按「过去 1 小时/今天 - M/D/昨天/星期几 - M/D/更早」分组,展开/折叠,整组删除 | 进行中 |
| 3 | 收藏夹栏(44px) | §6.1 favorites-bar + §7 favorites 文件夹 | 工具栏下方常驻行,显示收藏夹栏文件夹(参考特殊文件夹名 `"favorites-bar"`),溢出为文件夹菜单 | 进行中 |
| 4 | 空地址栏「热门站点」建议 | §7.1 空查询→搜索提供程序主页 | ToolbarControl 建议下拉增加空态:当前搜索引擎主页 + 快速站点 | 进行中 |
| 5 | InPrivate 窗口 + 徽章 | §6.2 InPrivate 视觉 + §7.3 页面 | 独立窗口模式:标签条 InPrivate 徽章、地址栏淡入(聚焦才显示)、InPrivate 起始页、不写入历史/会话 | 进行中 |
| 6 | 下载栏状态机 | §7.6 | 底部常驻栏:下载中「X / Y - Z%」/已暂停/失败 + 操作按钮 | 未开始 |
| 7 | Web 便笺实时绘制 | §7.5 | 页面实时画布(高亮 multiply 0.58、紫色便笺模式),优于现有截图批注方案,属大型改造 | 未开始 |
| 8 | 设置 Schema 对齐 | §7.7 | cookiePolicy / 受保护媒体 / 预测 / SmartScreen / 搜索建议开关 → 补 `WebResourceRequested` 钩子 | 未开始 |
| 9 | 收藏导入 | §4.3 | Chromium/IE/Firefox 导入(UWP 走文件选择器) | 未开始 |
| 10 | 阅读列表 + 收藏浮出双标签 | §6.1 favorite-flyout | Hub 加「阅读列表」分区,星标弹窗可存收藏夹或阅读列表 | 未开始 |

### 10.3 实施验收对照(§ 符号对应参考章节)

- §6.2 InPrivate:徽章 96×32 `#0067b8` 白字「▧ InPrivate」;地址栏在聚焦前隐藏,聚焦后 margin-right 还原。
- §5.5 错误页:`offline.html` / `dnserror.html` / `invalidcert.htm` 最小集;失败 URL 以 `#` 传入;页内重试/搜索按钮。
- §7.7 历史键:`past-hour` 默认展开;标题格式「过去 1 小时」「今天 - M/D」「昨天」「星期几 - M/D」「更早」。