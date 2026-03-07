## Why

Overlay 浮动翻译窗口是用户与 LiveLingo 交互的核心界面，当前存在输入即时触发翻译（无防抖）、固定高度截断翻译结果、无翻译结果复制功能、无加载状态指示等体验问题，导致频繁输入时 pipeline 重复调用、长文本无法完整显示、缺乏操作反馈。需要系统性提升该窗口的交互体验。

## What Changes

- 输入防抖：SourceText 变化后延迟 400ms 再触发翻译 pipeline，快速连续输入只执行最后一次
- 自适应高度：窗口移除固定高度，改为 SizeToContent + MaxHeight 约束
- 一键复制：翻译结果区域添加复制按钮，通过 IClipboardService 复制并显示反馈
- 加载状态：翻译进行中显示 indeterminate ProgressBar
- 语言选择器紧凑化：ComboBox 只显示语言代码，宽度缩小
- 快捷键提示分离：StatusText 只显示状态信息，快捷键提示用独立的键盘标签样式展示
- 定位改进：Show 后重新测量并 clamp 到屏幕可见区域
- 淡入淡出动画：窗口出现/消失使用 Opacity 过渡动画
- 源/目标语言互换：添加 SwapLanguages 命令和 ⇄ 按钮
- 字符计数：源输入框右下角显示当前输入字符数

## Capabilities

### New Capabilities
- `overlay-interaction`: 覆盖输入防抖、一键复制、语言互换、字符计数等交互增强功能
- `overlay-visual`: 覆盖自适应高度、加载状态、语言选择器样式、快捷键提示样式、淡入淡出动画等视觉改进
- `overlay-positioning`: 覆盖窗口 Show 后屏幕边界检测与重定位逻辑

### Modified Capabilities

## Impact

- `OverlayViewModel.cs`：新增属性（IsTranslating、ShowCopiedFeedback、SourceTextLength、SelectedSourceLanguage）、命令（CopyTranslation、SwapLanguages）、防抖逻辑；构造函数签名变更（新增 IClipboardService 参数）
- `OverlayWindow.axaml`：全面重构布局（SizeToContent、ProgressBar、Copy 按钮、kbd 样式、紧凑 ComboBox、Swap 按钮、字符计数）
- `OverlayWindow.axaml.cs`：淡入淡出动画逻辑（FadeOutAndClose）
- `App.axaml.cs`：PositionOverlay 改用估算高度、新增 ClampToScreen 辅助方法、注入 IClipboardService
- `OverlayViewModelTests.cs`：所有现有测试适配 400ms 防抖延迟；新增 debounce、copy、IsTranslating、swap、charcount 专项测试
