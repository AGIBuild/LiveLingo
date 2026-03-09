## MODIFIED Requirements

### Requirement: Settings source language uses ComboBox

Settings 页面的 DefaultSourceLanguage 输入控件 SHALL 使用 `ComboBox`，数据源来自固定语言目录（`ILanguageCatalog.All`），而非 `ITranslationEngine.SupportedLanguages`。

#### Scenario: Source language shows dropdown with fixed languages
- **WHEN** user opens Settings window
- **THEN** source语言下拉 SHALL 显示固定目录中的所有语言

#### Scenario: Source language selection persists
- **WHEN** user selects "中文" from source language dropdown and saves
- **THEN** `UserSettings.Translation.DefaultSourceLanguage` SHALL be set to "zh"

### Requirement: Settings target language uses ComboBox

Settings 页面的 DefaultTargetLanguage 输入控件 SHALL 使用 `ComboBox`，数据源同样来自固定语言目录（`ILanguageCatalog.All`）。

#### Scenario: Target language shows dropdown with fixed languages
- **WHEN** user opens Settings window
- **THEN** target语言下拉 SHALL 显示固定目录中的所有语言

#### Scenario: Target language selection persists
- **WHEN** user selects "English" from target language dropdown and saves
- **THEN** `UserSettings.Translation.DefaultTargetLanguage` SHALL be set to "en"

### Requirement: SettingsViewModel exposes language list from engine

`SettingsViewModel` SHALL expose `AvailableLanguages` property of type `IReadOnlyList<LanguageInfo>` sourced from固定语言目录（`ILanguageCatalog.All`），不再依赖 `ITranslationEngine.SupportedLanguages` 作为 UI 选择来源。

#### Scenario: ViewModel provides fixed language list for binding
- **WHEN** `SettingsViewModel` is constructed
- **THEN** `AvailableLanguages` SHALL be non-empty and match the fixed language catalog

### Requirement: ComboBox displays language name and stores code

`ComboBox` SHALL display `LanguageInfo.DisplayName` to the user and bind selected value to the language code string.

#### Scenario: User sees friendly names but code is stored
- **WHEN** dropdown shows "日本語"
- **THEN** selecting it SHALL set the backing property to "ja"

### Requirement: Overlay available languages from engine

`OverlayViewModel` SHALL source its available languages from固定语言目录（`ILanguageCatalog.All`），而非硬编码静态列表或引擎支持列表；同时，目标语言切换后 MUST 直接触发翻译流程，且 MUST NOT 在翻译前通过 `SupportsLanguagePair` 主动阻断。

#### Scenario: Overlay language cycling uses fixed catalog
- **WHEN** user clicks target language in overlay to cycle
- **THEN** it SHALL cycle through languages returned by the fixed language catalog

#### Scenario: Unsupported pair is not pre-blocked
- **WHEN** user selects a currently不支持/未安装模型的语对并输入文本
- **THEN** overlay SHALL still invoke pipeline translation and handle failure via translation error path
