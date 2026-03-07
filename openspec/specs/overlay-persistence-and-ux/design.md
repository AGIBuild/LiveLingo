## Context

Overlay 窗口在每次热键触发时从 `UserSettings` 读取翻译方向和注入模式创建新的 `OverlayViewModel`，用户在 overlay 中切换的语言对和模式在关闭时丢失。目标语言选择器使用原生 `ComboBox`，视觉风格与 overlay 极简暗色主题不协调。标题栏没有设置入口，需要通过托盘菜单才能打开设置。`LlamaTranslationEngine` 的 `AntiPrompts` 包含 `"\n\n"`，导致翻译多段文本时在第一个空行处截断。

## Goals / Non-Goals

**Goals:**
- Overlay 中调整的翻译方向和注入模式在关闭时自动持久化，下次打开时应用相同配置
- 目标语言选择器改为文本链接式交互，匹配 overlay 暗色极简美学
- 标题栏增加设置入口图标
- 修复多段文本翻译截断 bug

**Non-Goals:**
- 不改造源语言选择器（保持当前自动检测 + 底部指示器设计）
- 不在 overlay 内增加完整设置面板（只提供快捷入口跳转到 Settings 窗口）
- 不改变翻译引擎的整体架构

## Decisions

### 1) Overlay 关闭时持久化

**决策**：在 `OverlayViewModel` 中注入 `ISettingsService`，在 `Cancel()` 或 `RequestClose` 触发时调用 `_settingsService.Update()` 将当前 `TargetLanguage`、`_sourceLanguage`、`Mode` 写回 `UserSettings.Translation` 和 `UserSettings.UI`。

**原因**：OverlayViewModel 已持有所有需要持久化的状态，直接在关闭路径写回最简洁。不需要引入新的事件或服务。

**DI 变更**：`ShowOverlay` 方法在创建 `OverlayViewModel` 时额外传入 `ISettingsService`。

### 2) 文本链接式语言选择器

**决策**：将底栏 `ComboBox` 替换为自定义布局：默认状态显示目标语言 code 的 `TextBlock`（带下划线/链接样式），点击时通过 `Popup` 显示美化的语言列表。选中后 `Popup` 关闭，恢复文本链接显示。

**原因**：原生 `ComboBox` 在 overlay 中视觉突兀且占用空间，文本链接交互更贴合极简设计。使用 `Popup` 而非替换 `ComboBox` 可避免自定义控件复杂度，同时获得完全自定义的弹出样式。

### 3) 设置图标入口

**决策**：在标题栏 `Grid` 中 `LiveLingo` 文本前面增加 `⚙` 按钮（使用 `iconBtn` 样式），点击时通过 `OverlayViewModel` 的 `RequestOpenSettings` 事件通知 `App.axaml.cs` 调用 `ShowSettings()`。

**原因**：ViewModel 不能直接引用 `App` 实例，通过事件解耦符合 MVVM 架构规范。

### 4) 多段文本翻译截断修复

**决策**：从 `LlamaTranslationEngine.TranslateAsync` 的 `AntiPrompts` 列表中移除 `"\n\n"`，只保留 `["</s>", "<|im_end|>"]`。

**原因**：`"\n\n"` 作为 anti-prompt 会在生成的翻译输出中遇到空行时停止推理，导致多段文本翻译截断。`<|im_end|>` 已足够标识模型完成输出。

## Risks / Trade-offs

- **[持久化频率]** 每次关闭 overlay 都写文件 → Mitigation: `JsonSettingsService` 已经是异步写入，频率取决于用户使用频率，不会造成性能问题
- **[Popup 定位]** 自定义 Popup 在不同屏幕 DPI 下可能定位偏移 → Mitigation: 使用 Avalonia `Popup` 的 `PlacementTarget` + `PlacementMode` 自动定位
- **[移除 \n\n anti-prompt]** 模型可能在应该停止时继续生成 → Mitigation: `MaxTokens=512` 和 `<|im_end|>` 已提供充分的停止保障；已有 `text.Length * 5` 长度限制兜底
