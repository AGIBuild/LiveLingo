## ADDED Requirements

### Requirement: First-run detection
The system SHALL detect first-run by checking if `settings.json` exists. If absent, the setup wizard SHALL be displayed before normal app startup.

#### Scenario: First launch shows wizard
- **WHEN** the app starts and `settings.json` does not exist
- **THEN** `SetupWizardWindow` SHALL be displayed

#### Scenario: Subsequent launches skip wizard
- **WHEN** the app starts and `settings.json` exists
- **THEN** the wizard SHALL NOT be displayed and normal startup SHALL proceed

### Requirement: Step 1 — Language selection
The wizard SHALL present dropdowns for source language ("I write in") and target language ("Translate to") with at least: Chinese, Japanese, Korean, English, German, French, Spanish, Russian, Portuguese.

#### Scenario: Select Chinese to English
- **WHEN** user selects source=Chinese, target=English and clicks Next
- **THEN** the wizard SHALL proceed to Step 2 with `"zh→en"` as the language pair to download

### Requirement: Step 2 — Model download
The wizard SHALL download required models (FastText + selected MarianMT pair) with progress indication. The user SHALL be able to cancel the download.

#### Scenario: Download progress displayed
- **WHEN** models are downloading
- **THEN** the wizard SHALL show a progress bar and current model name

#### Scenario: Cancel download
- **WHEN** user clicks Cancel during download
- **THEN** the download SHALL stop and the wizard SHALL return to Step 1

#### Scenario: Download complete advances to Step 3
- **WHEN** all models finish downloading
- **THEN** the wizard SHALL automatically advance to Step 3

### Requirement: Step 3 — Shortcut confirmation
The wizard SHALL display current hotkey bindings with option to change them, and the default injection mode selector.

#### Scenario: Finish saves settings
- **WHEN** user clicks Finish on Step 3
- **THEN** `settings.json` SHALL be created with selected language pair, hotkeys, and injection mode

### Requirement: Back navigation
The wizard SHALL support Back navigation from Step 3 to Step 2 (if download complete) and from Step 2 to Step 1.

#### Scenario: Go back from Step 3
- **WHEN** user clicks Back on Step 3
- **THEN** the wizard SHALL return to Step 2 (showing completed download status)
