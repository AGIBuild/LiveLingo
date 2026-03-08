## ADDED Requirements

### Requirement: Single observable settings model
The system SHALL define a single `SettingsModel : ObservableObject` data model for application settings, and SHALL use this model for both UI binding and JSON persistence.

`SettingsModel` SHALL include at least:
- `int SchemaVersion`
- `HotkeySettings Hotkeys`
- `TranslationSettings Translation`
- `ProcessingSettings Processing`
- `UISettings UI`
- `AdvancedSettings Advanced`
- `UpdateSettings Update`

#### Scenario: Settings model is used in both layers
- **WHEN** the settings window binds to configuration state and settings are persisted to `settings.json`
- **THEN** both operations SHALL use `SettingsModel` instead of separate profile/draft data types

### Requirement: Settings service clone and replace contract
`ISettingsService` SHALL expose a clone-and-replace API for edit sessions:

```csharp
public interface ISettingsService
{
    SettingsModel Current { get; }
    SettingsModel CloneCurrent();
    void Replace(SettingsModel model);
    event Action SettingsChanged;
}
```

`Replace` SHALL atomically swap the in-memory `Current` and trigger persistence and `SettingsChanged`.

#### Scenario: Save from working copy
- **WHEN** a view model submits an edited `SettingsModel` copy through `Replace`
- **THEN** `Current` SHALL be replaced atomically and a single settings-changed notification SHALL be emitted

### Requirement: Edit session isolation
Settings editing SHALL occur on a working-copy instance returned by `CloneCurrent()`. Unsaved changes SHALL NOT affect `Current`.

#### Scenario: Cancel discards working copy changes
- **WHEN** user edits settings and clicks Cancel
- **THEN** `Current` SHALL remain unchanged and no settings-changed notification SHALL be emitted

#### Scenario: Save applies working copy changes
- **WHEN** user edits settings and clicks Save
- **THEN** the working copy SHALL become the new `Current` via `Replace`

### Requirement: Message notification without payload
The desktop message channel SHALL publish a single `SettingsChangedMessage` without payload, and subscribers SHALL read the latest state from `ISettingsService.Current`.

#### Scenario: Overlay receives settings change
- **WHEN** settings are saved
- **THEN** overlay logic SHALL receive `SettingsChangedMessage` and pull the latest values from `ISettingsService.Current`

### Requirement: Edge-case handling for invalid replacements
`Replace` SHALL reject `null` models and SHALL preserve the previous `Current` when serialization or file write fails.

#### Scenario: Null replacement
- **WHEN** `Replace(null)` is called
- **THEN** the service SHALL throw `ArgumentNullException` and SHALL NOT modify `Current`

#### Scenario: Persistence write failure
- **WHEN** `Replace` cannot persist to disk due to IO error
- **THEN** the service SHALL keep the previous `Current` and SHALL NOT emit a successful change notification
