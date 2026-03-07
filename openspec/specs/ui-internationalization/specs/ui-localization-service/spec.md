## ADDED Requirements

### Requirement: Localization service provides key-based text lookup
The system SHALL provide an `ILocalizationService` that resolves localized UI text by key for the active UI culture.

```csharp
public interface ILocalizationService
{
    CultureInfo CurrentCulture { get; }
    string T(string key);
    string T(string key, params object[] args);
    void SetCulture(string cultureName);
}
```

#### Scenario: Resolve text from current culture
- **WHEN** the current culture is `zh-CN` and key `tray.settings` is requested
- **THEN** the service returns the Chinese text for the settings menu item

### Requirement: Resource fallback strategy
The service SHALL resolve keys using this order: active culture -> `en-US` -> key literal.

#### Scenario: Missing key in current culture
- **WHEN** key `dialog.update.available` is missing in `zh-CN` but exists in `en-US`
- **THEN** the service returns the `en-US` value

#### Scenario: Missing key in all cultures
- **WHEN** key `unknown.key` does not exist in any resource
- **THEN** the service returns `unknown.key`

### Requirement: Parameterized localization
The service SHALL support formatted strings with positional arguments.

#### Scenario: Render version in about dialog
- **WHEN** key `dialog.about.version` is `Version: {0}` and argument `1.2.3`
- **THEN** the resolved text is `Version: 1.2.3`

### Requirement: DI registration
`ILocalizationService` SHALL be registered in App DI container as singleton and available to UI composition points.

#### Scenario: Resolve service from DI
- **WHEN** app startup builds the service provider
- **THEN** `ILocalizationService` can be resolved successfully
