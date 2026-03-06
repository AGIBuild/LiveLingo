## ADDED Requirements

### Requirement: Translation pipeline interface
The system SHALL expose `ITranslationPipeline` in `LiveLingo.Core.Translation` namespace with a single `ProcessAsync(TranslationRequest, CancellationToken)` method returning `TranslationResult`.

```csharp
public interface ITranslationPipeline
{
    Task<TranslationResult> ProcessAsync(TranslationRequest request, CancellationToken ct = default);
}
```

#### Scenario: Pipeline processes translation request
- **WHEN** `ProcessAsync` is called with a valid `TranslationRequest`
- **THEN** the pipeline SHALL return a `TranslationResult` containing translated text, detected source language, raw translation, and timing information

#### Scenario: Pipeline propagates cancellation
- **WHEN** the `CancellationToken` is cancelled during processing
- **THEN** the pipeline SHALL throw `OperationCanceledException`

### Requirement: Translation request and result records
The system SHALL define `TranslationRequest` and `TranslationResult` as immutable records.

```csharp
public record TranslationRequest(string SourceText, string? SourceLanguage, string TargetLanguage, ProcessingOptions? PostProcessing);
public record TranslationResult(string Text, string DetectedSourceLanguage, string RawTranslation, TimeSpan TranslationDuration, TimeSpan? PostProcessingDuration);
```

#### Scenario: Auto-detect source language
- **WHEN** `SourceLanguage` is null
- **THEN** the pipeline SHALL detect the source language automatically and populate `DetectedSourceLanguage`

#### Scenario: Same source and target language
- **WHEN** detected source language equals `TargetLanguage`
- **THEN** the pipeline SHALL return the original `SourceText` without invoking the translation engine

### Requirement: Translation engine interface
The system SHALL expose `ITranslationEngine` in `LiveLingo.Core.Engines` namespace as a `IDisposable` with `TranslateAsync` and `SupportsLanguagePair` methods.

```csharp
public interface ITranslationEngine : IDisposable
{
    Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct = default);
    bool SupportsLanguagePair(string sourceLanguage, string targetLanguage);
}
```

#### Scenario: Translate with supported pair
- **WHEN** `TranslateAsync` is called with a supported language pair
- **THEN** the engine SHALL return the translated text

#### Scenario: Translate with unsupported pair
- **WHEN** `TranslateAsync` is called with an unsupported language pair
- **THEN** the engine SHALL throw `NotSupportedException`

### Requirement: Text processor interface
The system SHALL expose `ITextProcessor` in `LiveLingo.Core.Processing` namespace with `Name` property and `ProcessAsync` method.

```csharp
public interface ITextProcessor : IDisposable
{
    string Name { get; }
    Task<string> ProcessAsync(string text, string language, CancellationToken ct = default);
}

public record ProcessingOptions(bool Summarize = false, bool Optimize = false, bool Colloquialize = false);
public enum ProcessingMode { Off, Summarize, Optimize, Colloquialize }
```

#### Scenario: Process text with a named processor
- **WHEN** `ProcessAsync` is called on a processor with `Name = "colloquialize"`
- **THEN** the processor SHALL return the post-processed text in the specified language

### Requirement: Language detector interface
The system SHALL expose `ILanguageDetector` in `LiveLingo.Core.LanguageDetection` namespace.

```csharp
public interface ILanguageDetector : IDisposable
{
    Task<DetectionResult> DetectAsync(string text, CancellationToken ct = default);
}
public record DetectionResult(string Language, float Confidence);
```

#### Scenario: Detect language of input text
- **WHEN** `DetectAsync` is called with Chinese text "你好世界"
- **THEN** the result SHALL contain `Language = "zh"` and `Confidence > 0`

### Requirement: Model manager interface
The system SHALL expose `IModelManager` in `LiveLingo.Core.Models` namespace for downloading, listing, and deleting models.

```csharp
public interface IModelManager
{
    Task EnsureModelAsync(ModelDescriptor descriptor, IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default);
    IReadOnlyList<InstalledModel> ListInstalled();
    Task DeleteModelAsync(string modelId, CancellationToken ct = default);
    long GetTotalDiskUsage();
}
```

#### Scenario: Ensure model that is already installed
- **WHEN** `EnsureModelAsync` is called for an already-installed model
- **THEN** the method SHALL return immediately without downloading

#### Scenario: List installed models when none exist
- **WHEN** `ListInstalled` is called with no models installed
- **THEN** the method SHALL return an empty list

### Requirement: P1 stub implementations
The system SHALL provide stub implementations for `ITranslationEngine`, `ILanguageDetector`, and `IModelManager` that require no external dependencies.

#### Scenario: Stub translation engine returns prefixed text
- **WHEN** `StubTranslationEngine.TranslateAsync("你好", "zh", "en")` is called
- **THEN** the result SHALL be `"[EN] 你好"`

#### Scenario: Stub language detector always returns Chinese
- **WHEN** `StubLanguageDetector.DetectAsync(anyText)` is called
- **THEN** the result SHALL be `DetectionResult("zh", 1.0f)`

#### Scenario: Stub model manager no-ops on ensure
- **WHEN** `StubModelManager.EnsureModelAsync(anyDescriptor)` is called
- **THEN** the method SHALL complete immediately without errors

### Requirement: Core DI registration extension
The system SHALL provide `ServiceCollectionExtensions.AddLiveLingoCore()` that registers all Core interfaces with their P1 stub implementations.

```csharp
public static IServiceCollection AddLiveLingoCore(this IServiceCollection services, Action<CoreOptions>? configure = null);
```

#### Scenario: Register and resolve translation pipeline
- **WHEN** `AddLiveLingoCore()` is called and `ITranslationPipeline` is resolved
- **THEN** a functioning `TranslationPipeline` instance SHALL be returned

### Requirement: Core project has zero platform dependencies
`LiveLingo.Core` SHALL NOT reference Avalonia, Windows-specific, or macOS-specific packages. Allowed dependencies: `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions`.

#### Scenario: Core project compiles without platform SDKs
- **WHEN** `dotnet build LiveLingo.Core.csproj` is run on any OS
- **THEN** the build SHALL succeed without errors
