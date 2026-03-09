## MODIFIED Requirements

### Requirement: Pipeline orchestration
`TranslationPipeline` SHALL orchestrate: (1) language detection if source language is null, (2) same-language short-circuit, (3) model readiness preflight via readiness service, (4) translation via engine, (5) post-processing via processors.

#### Scenario: Full pipeline with auto-detect
- **WHEN** `ProcessAsync` is called with `SourceLanguage = null` and Chinese input
- **THEN** the pipeline SHALL detect "zh", validate translation-model readiness, translate via MarianMT, and return `TranslationResult` with `DetectedSourceLanguage = "zh"`

#### Scenario: Same language short-circuit
- **WHEN** detected source language equals `TargetLanguage`
- **THEN** the pipeline SHALL return `SourceText` as `Text` with `TranslationDuration = TimeSpan.Zero`

#### Scenario: Specified source language skips detection
- **WHEN** `SourceLanguage = "zh"` is explicitly set
- **THEN** the pipeline SHALL NOT call `ILanguageDetector` and SHALL proceed directly to readiness preflight and translation

#### Scenario: Post-processing readiness enforced only when requested
- **WHEN** `PostProcessing` is null or disabled
- **THEN** the pipeline SHALL NOT perform post-processing model readiness checks

#### Scenario: Missing post-processing model raises typed error
- **WHEN** `PostProcessing` is requested and readiness check fails
- **THEN** the pipeline SHALL throw `ModelNotReadyException` with post-processing model metadata before invoking processors
