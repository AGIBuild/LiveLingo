## ADDED Requirements

### Requirement: UI language preference is configurable
The Settings UI SHALL provide a language selector for UI language with at least `en-US` and `zh-CN`.

#### Scenario: Language dropdown options
- **WHEN** user opens Settings
- **THEN** a language selector is visible with `English` and `简体中文` options

### Requirement: UI language preference is persisted
Selected UI language SHALL be saved in `UserSettings.UI.Language` and reloaded on next startup.

#### Scenario: Persist language selection
- **WHEN** user selects `zh-CN` and saves settings
- **THEN** `settings.json` stores `ui.language = "zh-CN"`

#### Scenario: Startup applies stored language
- **WHEN** app starts and `settings.json` contains `ui.language = "zh-CN"`
- **THEN** localization service current culture is set to `zh-CN` before tray menu is created

### Requirement: Runtime language change updates entry points
After language preference changes, the app SHALL update tray/menu and newly opened windows using the new language without app restart.

#### Scenario: Tray menu refresh after language change
- **WHEN** user changes UI language and saves
- **THEN** tray menu labels are rebuilt in the selected language

#### Scenario: New settings window uses new language
- **WHEN** user closes and reopens Settings after changing language
- **THEN** Settings labels are shown in the selected language
