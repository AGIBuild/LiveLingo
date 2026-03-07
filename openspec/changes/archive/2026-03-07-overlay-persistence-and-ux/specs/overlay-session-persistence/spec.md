## ADDED Requirements

### Requirement: Overlay persists translation direction on close
When the overlay closes, the system SHALL save the current source language, target language, and injection mode back to `UserSettings` via `ISettingsService.Update()`.

#### Scenario: Target language change persists
- **WHEN** user changes target language to `ja` in the overlay and closes
- **THEN** `settings.json` contains `translation.defaultTargetLanguage = "ja"`

#### Scenario: Source language change persists after swap
- **WHEN** user swaps languages (source `zh` ↔ target `en`) and closes
- **THEN** `settings.json` contains `translation.defaultSourceLanguage = "en"` and `translation.defaultTargetLanguage = "zh"`

#### Scenario: Injection mode change persists
- **WHEN** user toggles injection mode to `PasteOnly` and closes
- **THEN** `settings.json` contains `ui.defaultInjectionMode = "PasteOnly"`

### Requirement: Next overlay session applies persisted settings
The system SHALL create each new overlay with the latest persisted settings.

#### Scenario: Reopened overlay uses saved target language
- **WHEN** user previously set target language to `ja`, closed, and reopens overlay
- **THEN** the new overlay's target language is `ja`

### Requirement: Persistence only writes changed values
The system SHALL only call `ISettingsService.Update()` if at least one value (source language, target language, or injection mode) differs from the initial settings.

#### Scenario: No write when nothing changed
- **WHEN** user opens overlay, makes no changes, and closes
- **THEN** `ISettingsService.Update()` is NOT called
