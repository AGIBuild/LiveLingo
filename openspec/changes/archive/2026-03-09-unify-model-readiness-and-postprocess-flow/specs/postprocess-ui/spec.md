## MODIFIED Requirements

### Requirement: Error handling and graceful degradation
If post-processing is enabled but the required post-processing model is not ready, the overlay SHALL provide actionable guidance and continue with translation-only fallback for the current request.

#### Scenario: Missing post-processing model falls back to translation-only
- **WHEN** user enables `Summarize`/`Optimize`/`Colloquialize` and submits text while Qwen is not installed
- **THEN** overlay SHALL show a message guiding user to `Settings -> Models`, and SHALL retry current request without post-processing

#### Scenario: Off mode does not trigger post-processing readiness warnings
- **WHEN** `Processing.DefaultMode` is `Off`
- **THEN** overlay SHALL NOT log or display post-processing model-not-ready warnings

### Requirement: Post-processing mode selector in overlay
Overlay post-processing behavior SHALL be controlled only by `Processing.DefaultMode`; translation active-model selection SHALL NOT implicitly enable post-processing.

#### Scenario: Translation model switch does not force post-processing
- **WHEN** user changes active translation model while processing mode is `Off`
- **THEN** overlay SHALL keep post-processing disabled
