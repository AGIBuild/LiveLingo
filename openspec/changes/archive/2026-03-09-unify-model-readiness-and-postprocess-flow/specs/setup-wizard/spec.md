## MODIFIED Requirements

### Requirement: Step 2 — Model download
The setup wizard SHALL download required translation baseline models for the selected language pair with progress indication. FastText SHALL NOT be included in this required set.

#### Scenario: Download progress displayed
- **WHEN** required models are downloading
- **THEN** the wizard SHALL show progress and current model name/order

#### Scenario: Required set uses Marian baseline only
- **WHEN** user reaches Step 2 for a selected pair
- **THEN** required downloads SHALL include the selected Marian translation model and SHALL NOT include `FastTextLid`

#### Scenario: Download complete advances to Step 3
- **WHEN** all required models finish downloading
- **THEN** the wizard SHALL advance to Step 3
