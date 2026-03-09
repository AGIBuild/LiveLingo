## MODIFIED Requirements

### Requirement: Model registry
A static `ModelRegistry` class SHALL define model descriptors for translation and post-processing. FastText MAY remain defined as a descriptor, but SHALL NOT be part of the setup required download set.

#### Scenario: Required set excludes FastText
- **WHEN** `ModelRegistry.RequiredModels` or `GetRequiredModelsForLanguagePair()` is queried
- **THEN** returned required models SHALL include translation baseline only and SHALL NOT include `FastTextLid`

### Requirement: Model manifest
Each installed model directory SHALL continue to contain a `manifest.json` used by `ListInstalled()` for readiness checks and user-visible model lists.

#### Scenario: Installed model discoverable by readiness service
- **WHEN** model download completes and manifest exists
- **THEN** `ListInstalled()` SHALL include the model with correct id/type metadata
