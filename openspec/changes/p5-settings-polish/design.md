## Context

P1-P4 deliver a fully functional translation tool on Windows and macOS, but configuration is hardcoded and there's no onboarding experience. P5 is the "last mile" — making the product configurable, approachable for new users, and polished enough for v1.0 release.

Reference: docs/proposals/specs/P5-settings-polish-spec.md contains full implementation blueprints.

## Goals / Non-Goals

**Goals:**
- Persistent user configuration via JSON file
- Guided first-run setup (language selection → model download → shortcuts)
- Customizable hotkeys with live recording and hot-reload
- Multi-language pair management with on-demand model download
- Settings UI for all configurable options
- System tray / menu bar integration
- Overlay position memory

**Non-Goals:**
- Cloud sync of settings
- User accounts / login
- Automatic updates
- Localization of the app UI itself (always English)
- Multiple overlay themes

## Decisions

### D1: System.Text.Json for settings persistence

**Decision**: Use `System.Text.Json` (built-in) for settings serialization with `JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = CamelCase, Converters = { JsonStringEnumConverter } }`.

**Alternatives**:
- **Newtonsoft.Json**: More features but unnecessary dependency for simple POCO serialization.
- **YAML**: More human-readable but adds dependency, less natural in .NET.
- **SQLite**: Overkill for flat configuration.

`System.Text.Json` is zero-dependency, performant, and sufficient for our needs.

### D2: File-based first-run detection

**Decision**: First-run is detected by `!File.Exists(settingsFilePath)`. No separate flag file or registry key.

**Rationale**: Simple and reliable. When the wizard completes, it saves settings, creating the file. If user deletes the file, they get the wizard again — which is actually desirable behavior (reset to defaults).

### D3: Fire-and-forget save

**Decision**: `Update(Action<UserSettings>)` applies the mutation, fires `SettingsChanged`, then saves to disk asynchronously without awaiting (`_ = SaveAsync()`).

**Rationale**: Settings changes should feel instant. Disk write failure is non-critical (settings stay in memory, will be saved on next change or shutdown). A `SemaphoreSlim` prevents concurrent write corruption.

### D4: HotkeyRecorder as custom TextBox

**Decision**: Extend Avalonia `TextBox` to create `HotkeyRecorder` that intercepts `OnKeyDown` to capture key combinations.

**Alternatives**:
- **Standalone UserControl**: More isolation but duplicate text rendering logic.
- **Modal dialog**: "Press keys now..." popup. More disruptive UX.

Extending TextBox gives free text display, focus handling, and styling. Just override key handling.

### D5: Language pair as "{src}→{tgt}" string

**Decision**: Store language pairs as simple strings like "auto→en", "zh→ja" in settings. Parse with `Split('→')`.

**Rationale**: Human-readable in JSON file, easy to display in UI, trivial to parse. No need for a complex struct for this use case.

### D6: Overlay position — screen bounds check

**Decision**: On overlay open, if saved position is within any connected screen's bounds, use it. Otherwise fall back to auto-positioning (above target window, centered).

**Rationale**: Users may change monitor setup. A simple bounds check prevents the overlay from appearing off-screen.

## DI Registration

```csharp
// P5 adds to existing DI setup
services.AddSingleton<ISettingsService, JsonSettingsService>();

// Startup sequence:
// 1. Build ServiceProvider
// 2. Load settings: await settingsService.LoadAsync()
// 3. If first run: show wizard, await completion
// 4. Register hotkeys from settings
// 5. Subscribe to SettingsChanged for hot-reload
```

## Settings Architecture

```
ISettingsService (interface)
    │
    ├── Current: UserSettings (in-memory state)
    ├── LoadAsync() → reads from disk
    ├── SaveAsync() → writes to disk
    ├── Update(Action<UserSettings>) → mutate + notify + save
    └── SettingsChanged event → subscribers (hotkey reload, overlay config, etc.)

UserSettings
    ├── HotkeySettings
    │     ├── OverlayHotkey: string     ("Ctrl+Alt+T")
    │     └── InjectHotkey: string      ("Ctrl+Enter")
    ├── TranslationSettings
    │     ├── DefaultSourceLanguage: string
    │     ├── DefaultTargetLanguage: string
    │     └── LanguagePairs: List<string>
    ├── ProcessingSettings
    │     ├── DefaultPostProcessMode: ProcessingMode
    │     └── DefaultInjectionMode: InjectionMode
    ├── UISettings
    │     ├── OverlayOpacity: double
    │     └── LastOverlayPosition: OverlayPosition?
    └── AdvancedSettings
          ├── ModelStoragePath: string
          ├── TranslationThreads: int
          ├── LlmThreads: int
          └── LogLevel: string
```

## Risks / Trade-offs

- **[Risk] Settings migration between versions**: If `UserSettings` class changes, old JSON files may fail to deserialize. → **Mitigation**: `JsonSerializer` with lenient options ignores unknown properties. Missing properties get default values. No explicit migration needed for v1.
- **[Risk] Hotkey conflicts**: User may set a hotkey that conflicts with OS or other apps. → **Mitigation**: Display a warning if the registered hotkey doesn't fire within 3 seconds of registration. Don't block the combination — user knows their system best.
- **[Risk] Model download during wizard on slow connection**: Large model download may frustrate users. → **Mitigation**: Show download speed and ETA. Allow cancel and retry. Qwen model is optional (not downloaded in wizard).
- **[Trade-off] Fire-and-forget save**: Risk of losing last change on crash. → Acceptable: settings change infrequently, loss of one setting change is low impact.
- **[Trade-off] English-only UI**: Limits accessibility for non-English users. → Localization is v2 scope. The product itself translates languages, so users understand the concept.
