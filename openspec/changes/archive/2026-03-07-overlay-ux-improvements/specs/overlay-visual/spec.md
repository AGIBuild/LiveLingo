## ADDED Requirements

### Requirement: Adaptive window height

OverlayWindow XAML SHALL use `SizeToContent="Height"` with `MaxHeight="500"` instead of a fixed `Height`. The translation result Border SHALL have `MaxHeight="200"`. The source input TextBox SHALL have `MaxHeight="120"`.

#### Scenario: Short translation fits without scrolling
- **WHEN** translation result is 1-2 lines
- **THEN** window height adapts to content, no empty space

#### Scenario: Long translation respects max height
- **WHEN** translation result exceeds 200px height
- **THEN** translation area is capped at MaxHeight, window does not exceed 500px

### Requirement: Translation loading indicator

OverlayViewModel SHALL expose an `IsTranslating` boolean property. It MUST be set to `true` when `RunPipelineAsync` begins and `false` when it completes (success, error, or cancellation via `finally` block). The XAML SHALL display an indeterminate `ProgressBar` bound to `IsTranslating`.

```csharp
[ObservableProperty] private bool _isTranslating;
```

#### Scenario: Loading bar visible during translation
- **WHEN** pipeline is processing
- **THEN** IsTranslating is true and ProgressBar is visible

#### Scenario: Loading bar hidden after completion
- **WHEN** pipeline completes (success or error)
- **THEN** IsTranslating is false and ProgressBar is hidden

#### Scenario: Loading bar hidden after cancellation
- **WHEN** source text is cleared while translating
- **THEN** IsTranslating is set to false

### Requirement: Compact language selector

The target language ComboBox SHALL display only the language code (2-3 characters) instead of the full display name. Width SHALL be reduced to 70px. A "→" label SHALL precede the ComboBox.

#### Scenario: Language code display
- **WHEN** overlay is shown
- **THEN** ComboBox items show "en", "zh", "ja" etc. instead of "English", "中文", "日本語"

### Requirement: Keyboard shortcut labels

Keyboard shortcut hints (`Ctrl+Enter`, `Esc`) SHALL be displayed as styled Border labels with monospace font in the bottom bar, separate from the StatusText. StatusText SHALL only show dynamic status information (timing, errors).

#### Scenario: Status text after translation
- **WHEN** translation completes in 42ms
- **THEN** StatusText shows "Translated (42ms)" without shortcut hints

#### Scenario: Shortcut labels always visible
- **WHEN** overlay is shown
- **THEN** `[Ctrl+Enter]` and `[Esc]` labels are visible in the bottom bar regardless of StatusText content

### Requirement: Fade-in and fade-out animation

The root Panel of OverlayWindow SHALL have a `DoubleTransition` on `Opacity` with 150ms duration. On open, Opacity transitions from 0 to 1. On close/cancel, Opacity transitions from 1 to 0 before `Close()` is called (160ms delay).

#### Scenario: Window appears with fade-in
- **WHEN** overlay window opens
- **THEN** root Panel opacity transitions from 0 to 1 over 150ms

#### Scenario: Window disappears with fade-out
- **WHEN** user presses Esc or clicks close
- **THEN** root Panel opacity transitions to 0, then Close() is called after 160ms

### Requirement: Copy button in translation area

The translation result Border SHALL contain a "Copy" button at the top-right corner. When clicked, the button text SHALL briefly change to "Copied!" (green) for 800ms, bound to `ShowCopiedFeedback`.

#### Scenario: Copy button visible with translation
- **WHEN** TranslatedText is non-empty
- **THEN** "Copy" button is visible in the translation result area

#### Scenario: Button feedback on copy
- **WHEN** user clicks Copy button
- **THEN** button text changes to "Copied!" in green for ~800ms then reverts to "Copy"

### Requirement: Swap button in bottom bar

A "⇄" button SHALL be placed in the bottom bar between the shortcut labels and the mode toggle, bound to `SwapLanguagesCommand`. A source language indicator SHALL show the current source language code or "auto".

#### Scenario: Swap button visible
- **WHEN** overlay is shown
- **THEN** ⇄ button is visible in the bottom bar

#### Scenario: Source indicator shows configured language
- **WHEN** source language is "zh"
- **THEN** indicator shows "zh"

#### Scenario: Source indicator shows auto
- **WHEN** no source language is configured
- **THEN** indicator shows "auto" (via FallbackValue)

### Requirement: Character count display

The source input area SHALL overlay a character count at the bottom-right corner in semi-transparent small text, bound to `SourceTextLength`.

#### Scenario: Character count visible
- **WHEN** user has typed text in the source input
- **THEN** character count is displayed at the bottom-right of the input area
