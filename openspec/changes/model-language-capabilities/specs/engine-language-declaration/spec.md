## ADDED Requirements

### Requirement: LanguageInfo value type

The system SHALL define a `LanguageInfo` record in `LiveLingo.Core.Engines` namespace with `Code` (ISO 639-1) and `DisplayName` (human-readable name).

```csharp
public record LanguageInfo(string Code, string DisplayName);
```

#### Scenario: LanguageInfo provides display-friendly representation
- **WHEN** a `LanguageInfo` is created with `("zh", "中文")`
- **THEN** `Code` SHALL be `"zh"` and `DisplayName` SHALL be `"中文"`

### Requirement: ITranslationEngine declares supported languages

`ITranslationEngine` interface SHALL include a `SupportedLanguages` property of type `IReadOnlyList<LanguageInfo>`.

```csharp
public interface ITranslationEngine : IDisposable
{
    Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct = default);
    bool SupportsLanguagePair(string sourceLanguage, string targetLanguage);
    IReadOnlyList<LanguageInfo> SupportedLanguages { get; }
}
```

#### Scenario: LlamaTranslationEngine declares multilingual support
- **WHEN** `LlamaTranslationEngine.SupportedLanguages` is accessed
- **THEN** it SHALL return at least 10 languages including en, zh, ja, ko, fr, de, es, ru, ar, pt with their display names

#### Scenario: StubTranslationEngine declares test languages
- **WHEN** `StubTranslationEngine.SupportedLanguages` is accessed
- **THEN** it SHALL return a non-empty list for testing purposes

### Requirement: LlamaTranslationEngine consolidates language data

`LlamaTranslationEngine` SHALL use its existing `LanguageNames` dictionary as the single source to implement `SupportedLanguages`, eliminating any separate language definition.

#### Scenario: SupportedLanguages and LanguageNames are consistent
- **WHEN** comparing `SupportedLanguages` codes with the `LanguageNames` dictionary keys
- **THEN** they SHALL contain the same set of language codes

### Requirement: SupportsLanguagePair uses SupportedLanguages

`LlamaTranslationEngine.SupportsLanguagePair()` SHALL check that both source and target language codes exist in `SupportedLanguages`, instead of returning `true` unconditionally.

#### Scenario: Supported pair returns true
- **WHEN** `SupportsLanguagePair("zh", "en")` is called
- **THEN** it SHALL return `true`

#### Scenario: Unsupported pair returns false
- **WHEN** `SupportsLanguagePair("zh", "xyz")` is called
- **THEN** it SHALL return `false`
