## 1. Core Interface & Types

- [x] 1.1 在 `LiveLingo.Core.Engines` 命名空间添加 `LanguageInfo` record（`Code`, `DisplayName`）。验收：编译通过。
- [x] 1.2 在 `ITranslationEngine` 接口添加 `IReadOnlyList<LanguageInfo> SupportedLanguages { get; }` 属性。验收：编译通过。

## 2. Engine Implementations

- [x] 2.1 `LlamaTranslationEngine`: 基于现有 `LanguageNames` 字典实现 `SupportedLanguages` 属性，重构 `SupportsLanguagePair()` 检查语言是否在列表中。验收：单元测试验证列表内容和 SupportsLanguagePair 行为。
- [x] 2.2 `StubTranslationEngine`: 实现 `SupportedLanguages` 返回测试用语言列表。验收：编译通过。
- [x] 2.3 `MarianOnnxEngine`: 实现 `SupportedLanguages`（从模型 ID 解析语言对）。验收：编译通过。
- [x] 2.4 更新所有引用 `ITranslationEngine` 的 mock/测试代码，适配新属性。验收：所有现有测试通过。

## 3. Settings UI Dropdown

- [x] 3.1 `SettingsViewModel` 注入 `ITranslationEngine`，暴露 `AvailableLanguages` 属性。验收：ViewModel 测试验证属性来自引擎。
- [x] 3.2 `SettingsWindow.axaml` 中将源语言和目标语言 `TextBox` 替换为 `ComboBox`，`DisplayMemberBinding` 绑定 `DisplayName`，`SelectedValueBinding` 绑定 `Code`。验收：UI 显示下拉列表。
- [x] 3.3 更新 `SettingsViewModel` 测试，验证语言选择保存正确的语言代码。验收：测试通过。

## 4. Overlay Dynamic Language List

- [x] 4.1 `OverlayViewModel` 注入 `ITranslationEngine`，删除静态 `AvailableLanguages` 字段，改用引擎的 `SupportedLanguages`。验收：单元测试验证语言列表来自引擎。
- [x] 4.2 更新 `CycleLanguage` 逻辑适配动态列表。验收：循环切换测试通过。
- [x] 4.3 更新所有受影响的 Overlay 测试。验收：全部测试通过。

## 5. Integration & Final Verification

- [x] 5.1 运行 `nuke test` 确保所有测试通过（line ≥96%, branch ≥92%）。验收：构建成功，覆盖率达标。
- [ ] 5.2 运行 `nuke run` 手动验证 Settings 下拉列表和 Overlay 语言循环正常。验收：功能正常。
