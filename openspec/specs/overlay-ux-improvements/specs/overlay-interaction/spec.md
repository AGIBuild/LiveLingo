## ADDED Requirements

### Requirement: Input debounce

OverlayViewModel SHALL delay translation pipeline invocation by 400ms after the last SourceText change. If SourceText changes again within the 400ms window, the previous pending translation MUST be cancelled and the timer restarted.

```csharp
partial void OnSourceTextChanged(string value);
private async Task DebounceAndTranslateAsync(string text, CancellationToken ct);
```

#### Scenario: Rapid sequential input triggers single translation
- **WHEN** user types "a", "ab", "abc" within 400ms intervals
- **THEN** only one pipeline call is made with text "abc"

#### Scenario: Empty input cancels and clears
- **WHEN** user clears the source text to empty or whitespace
- **THEN** the pending translation is cancelled, TranslatedText is set to empty, and no pipeline call is made

#### Scenario: Input after debounce timeout triggers new translation
- **WHEN** user types "hello", waits >400ms, then types "world"
- **THEN** two separate pipeline calls are made

### Requirement: Copy translation to clipboard

OverlayViewModel SHALL expose a `CopyTranslationCommand` that copies the current TranslatedText to the system clipboard via `IClipboardService.SetTextAsync()`. After copying, `ShowCopiedFeedback` MUST be set to `true` for 800ms then reset to `false`.

```csharp
[ObservableProperty] private bool _showCopiedFeedback;
[RelayCommand] private async Task CopyTranslationAsync();
```

#### Scenario: Copy with valid translation
- **WHEN** TranslatedText is non-empty and user invokes CopyTranslationCommand
- **THEN** TranslatedText is set on the clipboard and ShowCopiedFeedback is true for ~800ms

#### Scenario: Copy with empty translation
- **WHEN** TranslatedText is empty or whitespace
- **THEN** CopyTranslationCommand does nothing, ShowCopiedFeedback remains false

#### Scenario: Copy without clipboard service
- **WHEN** IClipboardService is null (not injected)
- **THEN** CopyTranslationCommand does nothing, no exception is thrown

### Requirement: Swap source and target languages

OverlayViewModel SHALL expose a `SwapLanguagesCommand` that exchanges the current source language with the target language. The `_sourceLanguage` field MUST be mutable (not readonly).

```csharp
[ObservableProperty] private LanguageInfo? _selectedSourceLanguage;
[RelayCommand] private void SwapLanguages();
```

#### Scenario: Swap with both languages set
- **WHEN** source is "zh" and target is "en", user invokes SwapLanguagesCommand
- **THEN** source becomes "en", target becomes "zh", and existing source text is re-translated

#### Scenario: Swap with no selected target
- **WHEN** SelectedTargetLanguage is null
- **THEN** SwapLanguagesCommand does nothing

#### Scenario: Swap with source not in available languages
- **WHEN** old source language code is not found in AvailableTargetLanguages
- **THEN** source is updated to old target, target remains unchanged

### Requirement: Source text character count

OverlayViewModel SHALL expose a `SourceTextLength` property that reflects the character count of the current SourceText, updated in `OnSourceTextChanged`.

```csharp
[ObservableProperty] private int _sourceTextLength;
```

#### Scenario: Character count updates with input
- **WHEN** user types "Hello" (5 chars)
- **THEN** SourceTextLength equals 5

#### Scenario: Multibyte characters counted correctly
- **WHEN** user types "你好世界" (4 chars)
- **THEN** SourceTextLength equals 4

#### Scenario: Empty input
- **WHEN** source text is cleared
- **THEN** SourceTextLength equals 0
