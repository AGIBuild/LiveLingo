## ADDED Requirements

### Requirement: Marian is the default translation engine
The system SHALL register `MarianOnnxEngine` as the runtime implementation of `ITranslationEngine` for the translation stage.

#### Scenario: DI resolves Marian for translation
- **WHEN** the app builds services via `AddLiveLingoCore()`
- **THEN** resolving `ITranslationEngine` SHALL return an instance of `MarianOnnxEngine`

### Requirement: Qwen is reserved for post-processing only
The system SHALL use Qwen-based components only through `ITextProcessor` in the post-processing stage, and SHALL NOT use Qwen as the primary translation engine.

#### Scenario: Translation-only request bypasses Qwen
- **WHEN** `TranslationRequest.PostProcessing` is `null` or `Off`
- **THEN** the pipeline SHALL complete translation without invoking any Qwen-based processor

#### Scenario: Post-processing request invokes Qwen processors
- **WHEN** `TranslationRequest.PostProcessing` enables summarize/optimize/colloquialize
- **THEN** the pipeline SHALL run matching `ITextProcessor` instances after raw translation

### Requirement: Unsupported language pairs fail explicitly in translation stage
The translation stage SHALL fail fast with a descriptive `NotSupportedException` when no Marian model exists for the requested source-target pair.

#### Scenario: Unsupported pair is requested
- **WHEN** `TranslateAsync` is called with an unregistered language pair
- **THEN** the engine SHALL throw `NotSupportedException` containing the pair code in the message
