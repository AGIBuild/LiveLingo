## MODIFIED Requirements

### Requirement: UserSettings data model
The system SHALL define a single observable settings model named `SettingsModel` with nested groups: `HotkeySettings`, `TranslationSettings`, `ProcessingSettings`, `UISettings`, `AdvancedSettings`, `UpdateSettings`. All properties SHALL have sensible defaults.

#### Scenario: Default settings values
- **WHEN** a new `SettingsModel` instance is created
- **THEN** `Hotkeys.OverlayToggle` SHALL be `"Ctrl+Alt+T"`, `Translation.DefaultTargetLanguage` SHALL be `"en"`, and `UI.DefaultInjectionMode` SHALL be `"PasteAndSend"`

### Requirement: JSON file persistence
`JsonSettingsService` SHALL persist `SettingsModel` to `settings.json` using `System.Text.Json` with indented formatting and camelCase property naming.

#### Scenario: Save and reload settings
- **WHEN** `Replace()` is called with updated settings and the app restarts
- **THEN** `LoadAsync()` SHALL restore the changed values from the JSON file

#### Scenario: Settings file location
- **WHEN** running on Windows
- **THEN** settings SHALL be stored at `%LOCALAPPDATA%\\LiveLingo\\settings.json`

### Requirement: Corrupt file recovery
If `settings.json` contains invalid JSON, `LoadAsync` SHALL log a warning and return default `SettingsModel` without crashing.

#### Scenario: Corrupt JSON file
- **WHEN** `settings.json` contains `"{invalid json"`
- **THEN** `LoadAsync()` SHALL return default `SettingsModel` and the app SHALL start normally

### Requirement: Settings change notification
`ISettingsService.SettingsChanged` SHALL fire whenever `Replace()` successfully applies a new `SettingsModel`.

#### Scenario: Subscribe to changes
- **WHEN** a subscriber registers for `SettingsChanged` and `Replace()` is called successfully
- **THEN** the subscriber SHALL be notified after `Current` is updated

### Requirement: Thread-safe file access
`LoadAsync` and `SaveAsync` SHALL use a `SemaphoreSlim(1,1)` to prevent concurrent file access.

#### Scenario: Concurrent replace calls
- **WHEN** two `Replace()` calls happen simultaneously
- **THEN** both SHALL complete without file corruption and the final persisted file SHALL match the last successful replacement
