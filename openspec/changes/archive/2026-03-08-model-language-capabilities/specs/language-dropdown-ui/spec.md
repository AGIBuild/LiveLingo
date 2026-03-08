## ADDED Requirements

### Requirement: Settings source language uses ComboBox

Settings 页面的 DefaultSourceLanguage 输入控件 SHALL 替换为 `ComboBox`，数据源来自 `ITranslationEngine.SupportedLanguages`。

#### Scenario: Source language shows dropdown with available languages
- **WHEN** user opens Settings window
- **THEN** source language field SHALL display a `ComboBox` containing all languages from the translation engine

#### Scenario: Source language selection persists
- **WHEN** user selects "中文" from source language dropdown and saves
- **THEN** `UserSettings.Translation.DefaultSourceLanguage` SHALL be set to "zh"

### Requirement: Settings target language uses ComboBox

Settings 页面的 DefaultTargetLanguage 输入控件 SHALL 替换为 `ComboBox`，数据源同样来自 `ITranslationEngine.SupportedLanguages`。

#### Scenario: Target language shows dropdown with available languages
- **WHEN** user opens Settings window
- **THEN** target language field SHALL display a `ComboBox` containing all languages from the translation engine

#### Scenario: Target language selection persists
- **WHEN** user selects "English" from target language dropdown and saves
- **THEN** `UserSettings.Translation.DefaultTargetLanguage` SHALL be set to "en"

### Requirement: SettingsViewModel exposes language list from engine

`SettingsViewModel` SHALL inject `ITranslationEngine` and expose `AvailableLanguages` property of type `IReadOnlyList<LanguageInfo>` sourced from `ITranslationEngine.SupportedLanguages`.

#### Scenario: ViewModel provides language list for binding
- **WHEN** `SettingsViewModel` is constructed with a valid `ITranslationEngine`
- **THEN** `AvailableLanguages` SHALL be non-empty and match the engine's supported languages

### Requirement: ComboBox displays language name and stores code

`ComboBox` SHALL display `LanguageInfo.DisplayName` to the user and bind selected value to the language code string.

#### Scenario: User sees friendly names but code is stored
- **WHEN** dropdown shows "日本語"
- **THEN** selecting it SHALL set the backing property to "ja"

### Requirement: Overlay available languages from engine

`OverlayViewModel` SHALL source its available languages from `ITranslationEngine.SupportedLanguages` instead of a hardcoded static list. The static `AvailableLanguages` field SHALL be removed.

#### Scenario: Overlay language cycling uses engine languages
- **WHEN** user clicks target language in overlay to cycle
- **THEN** it SHALL cycle through languages returned by `ITranslationEngine.SupportedLanguages`

#### Scenario: Overlay language list matches engine
- **WHEN** `OverlayViewModel` is constructed
- **THEN** the language cycle list SHALL contain exactly the languages from the engine
