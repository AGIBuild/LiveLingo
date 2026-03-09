# fixed-language-catalog Specification

## Purpose

Provides a centralized fixed language catalog for UI selection flows. The catalog decouples language selection from model installation and engine capabilities, allowing users to select any supported language pair regardless of whether a translation model is installed. Translation failures for unsupported pairs are handled by the normal error flow.

## Requirements

### Requirement: Desktop fixed language catalog

Desktop layer SHALL define a centralized fixed language catalog used by UI selection flows. The catalog SHALL expose at least 10 language entries: `zh`, `en`, `ja`, `ko`, `fr`, `de`, `es`, `ru`, `ar`, `pt`, each with stable `LanguageInfo(Code, DisplayName)` values.

```csharp
public interface ILanguageCatalog
{
    IReadOnlyList<LanguageInfo> All { get; }
}
```

#### Scenario: Catalog returns stable language set
- **WHEN** `ILanguageCatalog.All` is read
- **THEN** it SHALL return the fixed language list in deterministic order with no runtime filtering by model state

### Requirement: UI language selectors source from catalog

`SetupWizardViewModel`, `SettingsViewModel`, and `OverlayViewModel` SHALL source selectable translation languages from `ILanguageCatalog.All` instead of engine-declared capabilities.

#### Scenario: Same options across wizard, settings, and overlay
- **WHEN** user opens setup wizard, settings translation tab, and overlay picker
- **THEN** all three selectors SHALL present the same language set from the fixed catalog

### Requirement: Language selection decouples from model support checks

Language selection SHALL remain available regardless of model installation/support status. The system MUST NOT disable or hide language options based on `SupportsLanguagePair` or installed model list.

#### Scenario: Uninstalled pair still selectable
- **WHEN** user selects a language pair whose model is not installed
- **THEN** selection SHALL be accepted and persisted, and subsequent translation failure SHALL be handled by normal error flow
