## ADDED Requirements

### Requirement: Model requirements are tiered into required and optional sets
The system SHALL classify models into a required tier for baseline translation and an optional tier for enhancement features.

#### Scenario: Required and optional tiers are evaluated separately
- **WHEN** startup validation runs
- **THEN** required-tier model absence SHALL be treated as blocking, and optional-tier model absence SHALL be treated as non-blocking

### Requirement: Baseline translation requires FastText and default Marian pair
The required tier SHALL include language detection support and a default translation pair model needed for first usable translation.

#### Scenario: Required tier readiness
- **WHEN** `FastText` and the default Marian language-pair model are installed
- **THEN** the app SHALL allow opening overlay translation flow without requiring Qwen

### Requirement: Setup wizard downloads required tier only
First-run setup SHALL download required-tier models by default and SHALL NOT force optional-tier model download.

#### Scenario: First-run completes without optional model
- **WHEN** user completes setup with required-tier download complete and optional-tier not downloaded
- **THEN** first-run setup SHALL finish successfully and translation SHALL remain available

### Requirement: Optional Qwen model is on-demand for post-processing
The system SHALL request optional model download only when the user enables a post-processing mode requiring Qwen.

#### Scenario: Enable post-processing without Qwen installed
- **WHEN** user switches mode from `Off` to a Qwen-backed mode and Qwen is not installed
- **THEN** the app SHALL prompt or guide the user to download Qwen before executing post-processing
