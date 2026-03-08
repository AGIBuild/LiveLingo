## Why

Settings 页面的源语言和目标语言目前是自由文本输入框，用户需要记住语言代码（如 zh、en、ja）才能正确填写。可用语言应由翻译引擎的能力决定——`LlamaTranslationEngine` 内部已有 `LanguageNames` 字典，`OverlayViewModel` 也有硬编码的 `AvailableLanguages`，两处重复且与 UI 无关联。需要让翻译引擎统一声明语言能力，并在 UI 中以下拉列表呈现。

## What Changes

- 扩展 `ITranslationEngine` 接口，添加 `SupportedLanguages` 属性让引擎声明支持的语言
- 新增 `LanguageInfo` record 统一语言代码与显示名
- `LlamaTranslationEngine` 基于现有 `LanguageNames` 字典实现 `SupportedLanguages`
- Settings 页面源语言/目标语言 `TextBox` 替换为 `ComboBox`，数据源来自引擎
- Overlay 的硬编码 `AvailableLanguages` 删除，改为从引擎获取

## Capabilities

### New Capabilities
- `engine-language-declaration`: ITranslationEngine 接口扩展，引擎声明支持的语言列表，新增 LanguageInfo 值类型
- `language-dropdown-ui`: Settings 页面源语言/目标语言改为下拉选择，Overlay 语言列表动态化

### Modified Capabilities

## Impact

- `LiveLingo.Core`: ITranslationEngine 接口添加属性、LlamaTranslationEngine/StubTranslationEngine 实现、新增 LanguageInfo record
- `LiveLingo.App`: SettingsViewModel 注入引擎获取语言列表、SettingsWindow.axaml 控件替换、OverlayViewModel 删除硬编码列表
- 测试: 引擎测试、SettingsViewModel 测试、Overlay 语言循环测试需更新
