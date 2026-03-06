## ADDED Requirements

### Requirement: DI container setup in App.axaml.cs
`App.OnFrameworkInitializationCompleted` SHALL create a `ServiceCollection`, register Core services via `AddLiveLingoCore()`, register platform services based on OS detection, build the `ServiceProvider`, and use it for the application lifetime.

#### Scenario: Windows DI registration
- **WHEN** the app starts on Windows
- **THEN** `IPlatformServices` SHALL resolve to `WindowsPlatformServices`

#### Scenario: Missing platform on unsupported OS
- **WHEN** the app starts on an unsupported OS (not Windows, not macOS)
- **THEN** the app SHALL throw a descriptive `PlatformNotSupportedException` at startup

### Requirement: OverlayViewModel constructor injection
`OverlayViewModel` SHALL accept `TargetWindowInfo`, `ITranslationPipeline`, and `ITextInjector` via constructor injection. It SHALL NOT use any static method calls for translation or injection.

```csharp
public OverlayViewModel(TargetWindowInfo target, ITranslationPipeline pipeline, ITextInjector injector)
```

#### Scenario: OverlayViewModel translates via pipeline
- **WHEN** `SourceText` property changes to a non-empty value
- **THEN** the ViewModel SHALL call `_pipeline.ProcessAsync()` and update `TranslatedText`

#### Scenario: OverlayViewModel injects via injector
- **WHEN** `InjectAsync(autoSend: true)` is called
- **THEN** the ViewModel SHALL call `_injector.InjectAsync()` with the target window info and translated text

#### Scenario: OverlayViewModel cancels previous translation
- **WHEN** `SourceText` changes rapidly (multiple times within 100ms)
- **THEN** the ViewModel SHALL cancel the previous `ProcessAsync` call and start a new one with the latest text

### Requirement: Overlay creation flow
When the global hotkey fires, `App` SHALL: (1) get `TargetWindowInfo` from `IWindowTracker`, (2) skip if target is the app's own window, (3) resolve `ITranslationPipeline` from DI, (4) create `OverlayViewModel` with target/pipeline/injector, (5) create and show `OverlayWindow`.

#### Scenario: Hotkey triggers overlay with correct dependencies
- **WHEN** user presses Ctrl+Alt+T while Slack is focused
- **THEN** a new `OverlayWindow` SHALL appear with a ViewModel wired to the translation pipeline and text injector

#### Scenario: Hotkey ignored when own window is focused
- **WHEN** user presses Ctrl+Alt+T while LiveLingo's own window is focused
- **THEN** no overlay SHALL be created

### Requirement: Diagnostic tools conditionally compiled
PoC diagnostic CLI tools (`InjectionTest`, `SlackAutoTest`, `WindowDiagnostic`) SHALL only compile when `DEBUG` configuration is active, guarded by `#if DEBUG`.

#### Scenario: Release build excludes diagnostics
- **WHEN** `dotnet build -c Release` is executed
- **THEN** diagnostic CLI arguments (`--test-inject`, `--diag-window`, `--test-slack`) SHALL NOT be recognized

#### Scenario: Debug build includes diagnostics
- **WHEN** `dotnet build -c Debug` is executed and `--test-inject` argument is provided
- **THEN** `InjectionTest.Run()` SHALL execute
