## ADDED Requirements

### Requirement: UserSettings data model
The system SHALL define a `UserSettings` class with nested settings groups: `HotkeySettings`, `TranslationSettings`, `ProcessingSettings`, `UISettings`, `AdvancedSettings`. All properties SHALL have sensible defaults.

#### Scenario: Default settings values
- **WHEN** a new `UserSettings` instance is created
- **THEN** `Hotkey.OverlayHotkey` SHALL be `"Ctrl+Alt+T"`, `Translation.DefaultTargetLanguage` SHALL be `"en"`, `Processing.DefaultInjectionMode` SHALL be `PasteAndSend`

### Requirement: JSON file persistence
`JsonSettingsService` SHALL persist `UserSettings` to `settings.json` using `System.Text.Json` with indented formatting and camelCase property naming.

#### Scenario: Save and reload settings
- **WHEN** `Update()` is called to change a setting and the app restarts
- **THEN** `LoadAsync()` SHALL restore the changed setting from the JSON file

#### Scenario: Settings file location
- **WHEN** running on Windows
- **THEN** settings SHALL be stored at `%LOCALAPPDATA%\LiveLingo\settings.json`

### Requirement: Corrupt file recovery
If `settings.json` contains invalid JSON, `LoadAsync` SHALL log a warning and return default `UserSettings` without crashing.

#### Scenario: Corrupt JSON file
- **WHEN** `settings.json` contains `"{invalid json"`
- **THEN** `LoadAsync()` SHALL return default `UserSettings` and the app SHALL start normally

### Requirement: Settings change notification
`ISettingsService.SettingsChanged` event SHALL fire whenever `Update()` is called, providing the updated `UserSettings` instance to all subscribers.

#### Scenario: Subscribe to changes
- **WHEN** a subscriber registers for `SettingsChanged` and `Update()` is called
- **THEN** the subscriber SHALL receive the updated `UserSettings`

### Requirement: Thread-safe file access
`LoadAsync` and `SaveAsync` SHALL use a `SemaphoreSlim(1,1)` to prevent concurrent file access.

#### Scenario: Concurrent save calls
- **WHEN** two `Update()` calls happen simultaneously
- **THEN** both SHALL complete without file corruption
