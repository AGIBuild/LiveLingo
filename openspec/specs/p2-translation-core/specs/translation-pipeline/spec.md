## ADDED Requirements

### Requirement: Pipeline orchestration
`TranslationPipeline` SHALL orchestrate: (1) language detection if source language is null, (2) same-language short-circuit, (3) model availability check, (4) translation via engine, (5) post-processing via processors (empty in P2).

#### Scenario: Full pipeline with auto-detect
- **WHEN** `ProcessAsync` is called with `SourceLanguage = null` and Chinese input
- **THEN** the pipeline SHALL detect "zh", translate via MarianMT, and return `TranslationResult` with `DetectedSourceLanguage = "zh"`

#### Scenario: Same language short-circuit
- **WHEN** detected source language equals `TargetLanguage`
- **THEN** the pipeline SHALL return `SourceText` as `Text` with `TranslationDuration = TimeSpan.Zero`

#### Scenario: Specified source language skips detection
- **WHEN** `SourceLanguage = "zh"` is explicitly set
- **THEN** the pipeline SHALL NOT call `ILanguageDetector` and proceed directly to translation

### Requirement: Cancel-and-restart support
The pipeline SHALL respect `CancellationToken` at each stage boundary (after detection, after translation). When cancelled, it SHALL throw `OperationCanceledException` without leaving partial state.

#### Scenario: Cancel during translation
- **WHEN** `CancellationToken` is cancelled while `ITranslationEngine.TranslateAsync` is executing
- **THEN** `OperationCanceledException` SHALL propagate to the caller

### Requirement: Timing information
`TranslationResult` SHALL contain accurate timing for `TranslationDuration` (time spent in engine) and `PostProcessingDuration` (null in P2).

#### Scenario: Translation timing recorded
- **WHEN** a translation completes successfully
- **THEN** `TranslationDuration` SHALL be > 0ms and reflect actual engine processing time

### Requirement: DI registration replaces stubs
P2's `AddLiveLingoCore()` SHALL register `MarianOnnxEngine` as `ITranslationEngine`, `FastTextDetector` (with `ScriptBasedDetector` fallback) as `ILanguageDetector`, and `ModelManager` as `IModelManager`, replacing all P1 stubs.

#### Scenario: Pipeline uses real engine
- **WHEN** `ITranslationPipeline` is resolved after P2 DI registration
- **THEN** calling `ProcessAsync` with Chinese text SHALL return real English translation (not `[EN] xxx`)

### Requirement: ViewModel status updates
The overlay ViewModel SHALL display status text reflecting pipeline state: "Translating..." during processing, "Translated (Xms)" on success, "Error: {message}" on failure.

#### Scenario: Status shows translating
- **WHEN** user types text and translation begins
- **THEN** `StatusText` SHALL immediately show "Translating..."

#### Scenario: Status shows timing on success
- **WHEN** translation completes in 150ms
- **THEN** `StatusText` SHALL show "Translated (150ms)"

#### Scenario: Status shows error
- **WHEN** translation fails (e.g. model not found for language pair)
- **THEN** `StatusText` SHALL show "Error: {descriptive message}"

### Requirement: First-run model download UI
On first launch, if required models (FastText + default MarianMT pair) are not installed, the app SHALL display a download dialog showing model names, sizes, and download progress before entering normal mode.

#### Scenario: First launch triggers download
- **WHEN** the app starts and `marian-zh-en` model is not installed
- **THEN** a download dialog SHALL appear listing required models and their sizes

#### Scenario: Download completes and app proceeds
- **WHEN** all required models finish downloading
- **THEN** the download dialog SHALL close and the app SHALL enter normal hotkey-listening mode
