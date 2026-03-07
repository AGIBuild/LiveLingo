## Context

Overlay 浮动翻译窗口（`OverlayWindow`）是 LiveLingo 的核心交互入口。当前架构遵循 MVVM，`OverlayViewModel` 封装所有业务逻辑（零 Avalonia 依赖），View 层只负责绑定和布局。本次改进涉及 ViewModel 新增属性/命令、XAML 布局重构、Code-behind 动画处理和 App.axaml.cs 定位逻辑改进，均在现有架构边界内完成。

当前状态：
- ViewModel 在 `OnSourceTextChanged` 中直接调用 `RunPipelineAsync`（无防抖）
- 窗口固定 `Height="220"`，翻译结果区域 `MaxHeight="60"`
- 无复制功能、无加载指示器
- `PositionOverlay` 不做屏幕边界检测
- 无窗口出入动画

## Goals / Non-Goals

**Goals:**
- 减少频繁输入导致的重复 pipeline 调用（防抖 400ms）
- 翻译结果区域自适应高度，长文本不被截断
- 提供一键复制翻译结果的能力
- 翻译进行中有明确视觉反馈
- 底栏布局紧凑化，快捷键提示与状态信息分离
- 窗口不超出屏幕可见区域
- 出入动画平滑
- 支持源/目标语言互换
- 源输入框显示字符计数

**Non-Goals:**
- 不修改翻译 pipeline 内部逻辑
- 不修改 ITranslationEngine / IClipboardService 接口定义
- 不引入新的外部依赖
- 不涉及 macOS 平台适配（ClampToScreen 目前使用 Avalonia 跨平台 Screens API）
- 不重构 DI 注册流程

## Decisions

### D1: 防抖实现方式 — Task.Delay + CancellationToken

在 ViewModel 中用 `Task.Delay(400, ct)` 实现防抖，每次 SourceText 变化时 cancel 前一次 CTS。

**备选方案**: Rx Observable.Throttle — 引入 System.Reactive 依赖，对于单一防抖场景过重。

**理由**: 零额外依赖，逻辑清晰，完全可测试。

### D2: IClipboardService 注入方式 — 构造函数可选参数

`OverlayViewModel` 构造函数新增 `IClipboardService? clipboard = null`，保持向后兼容。

**备选方案**: 通过 DI 容器自动注入 — 需要修改 DI 注册，OverlayViewModel 不在 DI 容器中（每次手动 new）。

**理由**: OverlayViewModel 由 `App.ShowOverlay` 手动构建，optional 参数最简单。

### D3: 窗口高度策略 — SizeToContent="Height" + MaxHeight

移除固定 Height，让 Avalonia 根据内容自动计算高度，MaxHeight="500" 防止无限增长。

**备选方案**: 固定高度 + ScrollViewer — 需要手动管理滚动，UX 不如自适应直观。

**理由**: Avalonia 原生支持 SizeToContent，零额外代码。

### D4: 淡入淡出 — Panel.Opacity + DoubleTransition

根 Panel 初始 Opacity=0，OnOpened 后 30ms 设为 1 触发 CSS-like transition。关闭时先设 Opacity=0，160ms 后调用 Close()。

**备选方案**: Avalonia Animation API（Storyboard） — 更强大但代码量大，简单淡入淡出不需要。

**理由**: 声明式 Transition 最简洁，且完全在 View 层处理（符合架构规则）。

### D5: 屏幕边界检测 — Avalonia Screens API

使用 `overlay.Screens.ScreenFromWindow(overlay)` 获取当前屏幕工作区，clamp overlay position 到可见区域内。在 Show() 后 50ms 延迟调用（等待布局完成）。

**备选方案**: Win32 MonitorFromWindow — 不跨平台。

**理由**: Avalonia Screens API 跨平台，且已在 Avalonia 11 中稳定。

### D6: _sourceLanguage 可变性 — 移除 readonly

为支持 SwapLanguages，将 `_sourceLanguage` 从 `readonly string?` 改为 `string?`。新增 `SelectedSourceLanguage` 属性用于 UI 显示。

```
OverlayViewModel
├── _sourceLanguage: string?          (可变, 用于 pipeline 调用)
├── SelectedSourceLanguage: LanguageInfo?  (ObservableProperty, UI 显示)
├── SelectedTargetLanguage: LanguageInfo?  (ObservableProperty, ComboBox 绑定)
├── SwapLanguagesCommand              (交换 source ↔ target)
├── CopyTranslationCommand            (复制 + 反馈)
├── IsTranslating: bool               (pipeline 运行状态)
├── ShowCopiedFeedback: bool           (复制反馈闪现)
└── SourceTextLength: int              (字符计数)
```

## Risks / Trade-offs

- **[防抖延迟感知]** 400ms 延迟可能让用户觉得"不够即时" → 后续可配置化（UserSettings）
- **[SizeToContent 抖动]** 翻译结果动态变化时窗口高度可能频繁变化 → MaxHeight 限制 + 翻译区域 MinHeight 缓解
- **[淡出关闭竞态]** FadeOutAndClose 中 160ms 定时器到期前如果用户再次操作 → Dispatcher 保证 UI 线程顺序执行，Close 是最终操作
- **[Swap 语言缺失]** 如果原 sourceLanguage 不在 availableLanguages 中，swap 后 target 不变 → 通过 FirstOrDefault 处理，找不到则不更新 target
