## 1. Overlay Session Persistence

- [x] 1.1 在 `OverlayViewModel` 构造函数中注入 `ISettingsService`，记录初始 `_sourceLanguage`、`TargetLanguage`、`Mode` 用于后续差异比较。验收：编译通过。
- [x] 1.2 新增 `PersistIfChanged()` 方法：比较当前值与初始值，有差异则调用 `_settingsService.Update()` 写回 `Translation.DefaultSourceLanguage`、`Translation.DefaultTargetLanguage`、`UI.DefaultInjectionMode`。验收：单测覆盖有变更和无变更两种场景。
- [x] 1.3 在 `Cancel()` 和 `RequestClose` 路径中调用 `PersistIfChanged()`。验收：关闭浮窗后 settings.json 反映新配置。
- [x] 1.4 更新 `App.axaml.cs` 中 `ShowOverlay` 传入 `ISettingsService`。验收：编译通过。

## 2. Language Picker UX

- [x] 2.1 在 `OverlayWindow.axaml` 中将底栏 `ComboBox` 替换为 `TextBlock`（文本链接样式）+ `Popup`（语言列表面板）。验收：默认显示目标语言 code 文本链接。
- [x] 2.2 为 Popup 内容创建带暗色主题的 `ItemsControl`（圆角、hover 高亮、语言名称 + code 显示）。验收：Popup 打开后显示美化列表。
- [x] 2.3 实现点击文本链接打开 Popup → 选择语言关闭 Popup → 绑定 `SelectedTargetLanguage` 触发重翻。验收：选中新语言后 Popup 关闭并触发翻译。

## 3. Settings Entry Icon

- [x] 3.1 在 `OverlayViewModel` 增加 `RequestOpenSettings` 事件和 `OpenSettingsCommand`。验收：编译通过。
- [x] 3.2 在 `OverlayWindow.axaml` 标题栏添加 ⚙ 按钮（`iconBtn` 样式），绑定 `OpenSettingsCommand`。验收：UI 显示设置图标。
- [x] 3.3 在 `App.axaml.cs` 中订阅 `RequestOpenSettings` 事件，调用 `ShowSettings()`。验收：点击图标打开设置窗口。

## 4. Multiline Translation Fix

- [x] 4.1 在 `LlamaTranslationEngine.TranslateAsync` 的 `AntiPrompts` 中移除 `"\n\n"`，只保留 `["</s>", "<|im_end|>"]`。验收：编译通过。
- [x] 4.2 在 `LlamaTranslationEngineTests` 增加测试：验证 `AntiPrompts` 不包含 `"\n\n"`，确认 `BuildPrompt` 保留多段文本结构。验收：新增测试通过。

## 5. Tests & Verification

- [x] 5.1 增加 `OverlayViewModel` 持久化单测：覆盖语言变更后持久化、无变更不调用 Update、模式切换持久化。验收：测试通过。
- [x] 5.2 更新已有 `OverlayViewModelTests` 以适配新构造函数签名（增加 `ISettingsService` 参数）。验收：现有测试全部通过。
- [x] 5.3 执行 `dotnet build` + `dotnet test` 确认无回归。验收：构建通过、测试通过。
