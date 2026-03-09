## ADDED Requirements

### Requirement: Post-processing mode selector in overlay
Overlay post-processing behavior SHALL be controlled only by `Processing.DefaultMode`; translation active-model selection SHALL NOT implicitly enable post-processing.

#### Scenario: Translation model switch does not force post-processing
- **WHEN** user changes active translation model while processing mode is `Off`
- **THEN** overlay SHALL keep post-processing disabled

#### Scenario: Switch mode to Colloquial
- **WHEN** user selects "Colloquial" from the mode selector
- **THEN** `PostProcessMode` SHALL update to `ProcessingMode.Colloquialize` and subsequent translations SHALL include colloquial post-processing

#### Scenario: Mode persists across overlay instances
- **WHEN** user sets mode to "Optimize", closes overlay, and reopens it
- **THEN** the mode selector SHALL show "Optimize" as the selected mode

### Requirement: Two-stage preview
When post-processing is enabled, the overlay SHALL display the raw MarianMT translation immediately after Stage 1 completes, then replace it with the post-processed result after Stage 2 completes.

#### Scenario: Two-stage display
- **WHEN** user types text with post-processing enabled
- **THEN** `TranslatedText` SHALL first show the raw translation (with status "Translated (Xms)"), then update to the polished version (with status "Polished (Xs)")

#### Scenario: User can inject at any stage
- **WHEN** user presses Ctrl+Enter during Stage 2 (post-processing in progress)
- **THEN** the currently visible `TranslatedText` (raw translation) SHALL be injected

### Requirement: On-demand model download
When the user selects a non-Off post-processing mode for the first time and the Qwen model is not installed, the system SHALL prompt for download confirmation before proceeding.

#### Scenario: First-time mode selection triggers download prompt
- **WHEN** user selects "Colloquial" and Qwen model is not installed
- **THEN** a confirmation dialog SHALL appear: "AI Polish requires Qwen2.5-1.5B model (1.0 GB). Download now?" with Download and Cancel buttons

#### Scenario: Cancel download reverts to Off
- **WHEN** user clicks Cancel on the download prompt
- **THEN** `PostProcessMode` SHALL revert to `Off`

#### Scenario: Download completes enables processing
- **WHEN** user clicks Download and the download completes
- **THEN** the selected post-processing mode SHALL become active and processing SHALL begin

### Requirement: Loading status during model load
When the Qwen model is being loaded into memory (first use after download or after idle unload), the overlay SHALL display "Loading AI model..." in the status bar.

#### Scenario: Model loading status
- **WHEN** post-processing is triggered and model needs to be loaded
- **THEN** `StatusText` SHALL show "Loading AI model..." until model loading completes

### Requirement: Error handling and graceful degradation
If post-processing is enabled but the required post-processing model is not ready, the overlay SHALL provide actionable guidance and continue with translation-only fallback for the current request.

#### Scenario: Missing post-processing model falls back to translation-only
- **WHEN** user enables `Summarize`/`Optimize`/`Colloquialize` and submits text while Qwen is not installed
- **THEN** overlay SHALL show a message guiding user to `Settings -> Models`, and SHALL retry current request without post-processing

#### Scenario: Off mode does not trigger post-processing readiness warnings
- **WHEN** `Processing.DefaultMode` is `Off`
- **THEN** overlay SHALL NOT log or display post-processing model-not-ready warnings

#### Scenario: OOM during inference
- **WHEN** LLM inference throws `OutOfMemoryException`
- **THEN** `StatusText` SHALL show error message, `TranslatedText` SHALL remain as raw translation, and `PostProcessMode` SHALL revert to `Off`

#### Scenario: Inference timeout
- **WHEN** LLM inference exceeds 10 seconds
- **THEN** inference SHALL be cancelled and the raw translation SHALL be used as the final result
