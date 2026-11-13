# 标签预览带展开/收起动画 + 箭头按钮预研

日期：2026-11-13
状态：已实现（2026-11-13，待实测）

## 1. 结论

| 项目 | 可行性 | 推荐方案 | 预估工作量 |
| --- | --- | --- | --- |
| 预览带展开/收起动画 | 高 | `Height` 0↔120 Storyboard，180ms/140ms，`CubicEase.EaseOut` | 1.5–2 小时（含实测） |
| 按钮改为状态箭头 | 很高 | 直接切换 Segoe MDL2 字形 `E70D`（下）/ `E70E`（上） | 0.5 小时 |
| 箭头旋转动画（可选） | 高 | `FontIcon` + `RotateTransform` 0°↔180°，独立动画 | +0.5 小时 |

不需要新依赖，`TargetPlatformMinVersion=10.0.17763.0` 下 API 均可用。

## 2. 现状

- `MainPage.xaml` 的 `RootGrid` 行定义：标题/标签栏 32、预览带 `Auto`、工具栏 40、收藏夹栏 `Auto`、内容 `*`。
- `TabPreviewStrip` 是 `Grid.Row=1` 的 `Border`，`Height=120`、`Visibility=Collapsed`；行高为 `Auto`，所以改 `Height` 会自然把工具栏和网页下推。
- 卡片根元素固定 `Height=120`，外层 `ScrollViewer` 负责裁剪；动画时卡片不会被压扁，只会被视口裁掉。
- 切换按钮有两个：水平标签栏 `TabPreviewToggleBtn`、垂直侧栏 `VerticalPreviewBtn`，都调用 `TabPreviewToggleBtn_Click`，当前字形 `E8A9`。
- `SetTabPreviewStripOpen` 目前是瞬时显示/隐藏：打开时重建卡片并 `Visibility=Visible`，关闭时立即清空卡片、断开位图。
- `MainPage` 目前没有 Storyboard 动画；`Metro/MetroPage.xaml.cs` 的 `AnimateBottomBarHeight` 已有对 `Height` 做 `DoubleAnimation` 的先例。

## 3. 动画方案对比

### 方案 A：直接动画 `TabPreviewStrip.Height`（推荐）

- 打开：`0 → 120`；关闭：`120 → 0`。
- 行高 `Auto` 自动跟随，工具栏/网页平滑下推，符合现有交互定义。
- 实现量最小，和现有代码结构一致。
- 代价：`Height` 属于依赖动画，每帧都会触发布局；`WebView2`/`EdgeHTML` 内容区会逐帧改变尺寸，是唯一需要实测的风险点。
- 必须显式设置 `EnableDependentAnimation = true`，否则依赖属性动画可能不执行。

### 方案 B：Transform + Clip（A 的退路）

- 动画期间保持行高不变，预览带和下方内容用 `TranslateTransform` 整体位移，外层加裁剪；动画结束后一次性提交行高并复位变换。
- 优点：位移是独立动画，不逐帧重排 WebView，理论上更顺滑。
- 缺点：要同时变换工具栏和内容区，结束时有一次布局提交；实现和边界处理明显更复杂，可能出现末帧轻微跳动。
- 建议：先做 A，只有实测 WebView 明显卡顿或闪白时才升级到 B。

### 方案 C：只做淡入/EntranceThemeTransition

- 布局瞬时跳变，预览带淡入/滑入。
- 实现最简单，但不是真正的“展开”，内容区仍会瞬间下移，不建议作为最终方案。

## 4. 方案 A 实现要点

### 4.1 状态与常量

```csharp
private const double PreviewStripHeight = 120;
private Storyboard _previewStripStoryboard;
```

### 4.2 核心动画

```csharp
private void AnimatePreviewStrip(bool open)
{
    double current = TabPreviewStrip.Visibility == Visibility.Visible
        ? TabPreviewStrip.ActualHeight
        : 0;

    // 先把当前实际高度写成基准值，Stop() 才不会跳回旧值
    TabPreviewStrip.Height = current;
    _previewStripStoryboard?.Stop();
    _previewStripStoryboard = null;

    if (open && TabPreviewStrip.Visibility != Visibility.Visible)
    {
        TabPreviewStrip.Visibility = Visibility.Visible;
        TabPreviewStrip.Height = 0;
        current = 0;
    }

    double target = open ? PreviewStripHeight : 0;
    if (Math.Abs(current - target) < 0.5)
    {
        FinishPreviewStripAnimation(open);
        return;
    }

    var sb = new Storyboard();
    var anim = new DoubleAnimation
    {
        From = current,
        To = target,
        Duration = TimeSpan.FromMilliseconds(open ? 180 : 140),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        EnableDependentAnimation = true
    };
    Storyboard.SetTarget(anim, TabPreviewStrip);
    Storyboard.SetTargetProperty(anim, "Height");
    sb.Children.Add(anim);
    sb.Completed += (s, e) =>
    {
        if (!ReferenceEquals(_previewStripStoryboard, sb)) return;
        FinishPreviewStripAnimation(open);
    };
    _previewStripStoryboard = sb;
    sb.Begin();
}
```

### 4.3 收尾与资源释放

```csharp
private void FinishPreviewStripAnimation(bool open)
{
    TabPreviewStrip.Height = open ? PreviewStripHeight : 0;
    _previewStripStoryboard?.Stop();
    _previewStripStoryboard = null;

    if (!open)
    {
        TabPreviewStrip.Visibility = Visibility.Collapsed;
        TeardownPreviewStripCards();   // 原关闭分支里的清空逻辑移到这里
    }
}
```

关闭时 `_isPreviewStripOpen` 立即置 `false`，但卡片和位图延迟到动画结束再释放；捕获任务里的
`if (!_isPreviewStripOpen) return;` 已经能阻止关闭后继续写入图片。

### 4.4 快速连续点击

- 每次切换先读取 `ActualHeight` 作为新基准，再 `Stop()`，避免跳回 120/0。
- `Completed` 里用 `ReferenceEquals(_previewStripStoryboard, sb)` 判断，旧动画的完成事件不会把新动画收起。
- 关闭动画中途重新打开时，可以从当前高度直接反向动画；卡片是否重建可沿用现有 `RebuildTabPreviewStrip`，缩略图有缓存，重载成本可接受。

### 4.5 无障碍 / 系统设置

```csharp
private readonly UISettings _uiSettings = new UISettings();
...
if (!_uiSettings.AnimationsEnabled)
{
    // 系统关闭动画：直接设置最终高度并走收尾逻辑
}
```

`UISettings.AnimationsEnabled` 在 17763 可用；系统“显示动画”关闭时不做 Storyboard。

## 5. 箭头按钮

### 5.1 字形

- 收起状态（预览带关闭）：`E70D` ChevronDown，表示向下展开。
- 展开状态（预览带打开）：`E70E` ChevronUp，表示向上收起。

两个按钮共用同一状态：

```csharp
private void UpdatePreviewToggleVisualState()
{
    string glyph = _isPreviewStripOpen ? "\uE70E" : "\uE70D";
    string tip = _isPreviewStripOpen ? "收起标签预览" : "展开标签预览";

    TabPreviewToggleBtn.Content = glyph;
    VerticalPreviewBtn.Content = glyph;
    ToolTipService.SetToolTip(TabPreviewToggleBtn, tip);
    ToolTipService.SetToolTip(VerticalPreviewBtn, tip);
    AutomationProperties.SetName(TabPreviewToggleBtn, tip);
    AutomationProperties.SetName(VerticalPreviewBtn, tip);
}
```

- XAML 默认值改成 `E70D` + “展开标签预览”，避免启动瞬间显示旧图标。
- `ApplyPreviewStripTheme` 现有的强调色逻辑保持不变；箭头状态只改字形/提示，不改颜色。
- 需要 `using Windows.UI.Xaml.Automation;`。

### 5.2 可选旋转动画

如果希望箭头有连续感，可把按钮内容改成命名 `FontIcon`：

```xml
<FontIcon x:Name="TabPreviewChevron" Glyph="&#xE70D;" FontSize="12"
          RenderTransformOrigin="0.5,0.5">
    <FontIcon.RenderTransform>
        <RotateTransform x:Name="TabPreviewChevronRotate"/>
    </FontIcon.RenderTransform>
</FontIcon>
```

用 `DoubleAnimation` 动画 `(UIElement.RenderTransform).(RotateTransform.Angle)`，0°↔180°，
150ms。旋转属于独立动画，不参与布局，性能无忧。垂直侧栏按钮需要第二套命名元素。
若不想增加 XAML 复杂度，直接换字形已经满足需求。

## 6. 风险与边界

| 风险 | 影响 | 处理 |
| --- | --- | --- |
| WebView 逐帧重排 | 展开时可能卡顿/闪白 | 先实测 WebView2 与 EdgeHTML；必要时降级方案 B 或缩短时长 |
| 关闭动画期间资源未释放 | 多占约 140ms 的缩略图内存 | 可接受；捕获任务已用 `_isPreviewStripOpen` 拦截 |
| 快速连点 | 高度跳变/旧动画收尾 | 读 `ActualHeight` + 停止旧 Storyboard + `Completed` 引用校验 |
| 关闭动画中切垂直标签 | 卡片重建与高度动画叠加 | 切换模式前停止动画并归位到 0/120 |
| 标签增删/切换 | 卡片顺序与选中态 | 现有 `SyncPreviewStripOrder`/`UpdatePreviewStripSelection` 不变；卡片固定高 120 |
| 系统关闭动画 | 无障碍/晕动症 | 读 `UISettings.AnimationsEnabled`，关闭时瞬时切换 |
| 17763 | API 不存在 | `Height` 动画、`UISettings`、`EnableDependentAnimation` 均为 17763 可用 |

## 7. 验证清单（实现后）

1. 900×800、1210×800 下展开/收起，工具栏与网页下推/上移连续。
2. WebView2 普通页 + EdgeHTML EPUB 页各测一次，观察展开过程是否闪白/卡顿。
3. 连续快速点击 10 次，高度不跳变、不崩溃、内存无持续增长。
4. 展开动画中切换标签、关闭标签、打开 Hub/设置，状态与箭头一致。
5. 垂直标签模式切换时预览带仍正常，箭头状态正确。
6. 系统“显示动画”关闭时瞬时展开/收起。
7. 箭头字形、ToolTip、UIA Name 在两种按钮上都随状态变化。

## 8. 建议

先实现方案 A + 字形切换，保留方案 B 作为性能退路。动画参数先取 180ms/140ms，
实测后可在 140–220ms 之间微调；不建议引入 Composition 自定义动画，当前收益不足以抵消复杂度。

## 9. 实现记录（2026-11-13）

已按方案 A + 字形切换落地，未启用可选的箭头旋转动画。

- `MainPage.xaml`：两个预览按钮默认字形改为 `E70D`，ToolTip/UIA Name 为“展开标签预览”。
- `MainPage.xaml.cs`：
  - `PreviewStripHeight=120`、`_previewStripStoryboard`、`UISettings _uiSettings`。
  - `SetTabPreviewStripOpen` 只负责状态、卡片重建和启动动画；收起时不再立即释放卡片。
  - `AnimatePreviewStrip` 以 `wasVisible`/`ActualHeight` 为基准，先固化当前高度再停止旧动画；
    `Completed` 用 `ReferenceEquals` 校验，系统关闭动画时直接走收尾。
  - `FinishPreviewStripAnimation` 提交最终高度；收起时再隐藏并调用 `TeardownPreviewStripCards`
    释放位图。
  - `UpdatePreviewToggleVisualState` 同步两个按钮的字形、ToolTip 和 UIA Name。
- 验证：Debug|x86 构建通过；UI 行为与性能由用户在真实环境实测。
