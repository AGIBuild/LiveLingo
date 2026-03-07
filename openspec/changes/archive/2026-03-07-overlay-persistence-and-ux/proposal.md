## Why

浮动翻译窗口存在 4 个影响可用性的问题：(1) 用户在 overlay 中调整的目标语言和翻译方向每次关闭后丢失，下一次打开又恢复默认；(2) 目标语言选择器使用原生 ComboBox 风格生硬，不符合 overlay 极简美学；(3) 没有从 overlay 快速打开设置的入口；(4) 包含空行的多段文本翻译时，空行后的内容被截断丢失。

## What Changes

- Overlay 关闭时将用户在浮窗中调整的翻译方向（源/目标语言）和注入模式持久化回 `UserSettings`
- 目标语言选择器从原生 ComboBox 改为文本链接式交互：默认显示语言代码文本链接，点击后激活美化的下拉面板
- 标题栏右上角增加设置图标按钮（⚙），点击打开 Settings 窗口
- 修复 `LlamaTranslationEngine` 中 `"\n\n"` 作为 anti-prompt 导致多段文本翻译截断的 bug

## Capabilities

### New Capabilities
- `overlay-session-persistence`: Overlay 关闭时持久化翻译方向和模式配置
- `overlay-language-picker`: 将目标语言选择器改为文本链接式交互并美化下拉面板
- `overlay-settings-entry`: 标题栏增加设置入口图标
- `multiline-translation-fix`: 修复空行截断翻译的 bug

### Modified Capabilities

## Impact

- `LiveLingo.App`: `OverlayViewModel`（持久化逻辑、设置入口事件）、`OverlayWindow.axaml`（语言选择器重新设计、设置图标）、`App.axaml.cs`（设置图标事件处理）
- `LiveLingo.Core`: `LlamaTranslationEngine`（移除 `\n\n` anti-prompt）
- 测试：OverlayViewModel 持久化单测、LlamaTranslationEngine 多段文本单测
