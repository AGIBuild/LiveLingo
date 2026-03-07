## 1. Solution & Project Setup

- [x] 1.1 Create `LiveLingo.slnx` solution file with `/src/` and `/tests/` folders
- [x] 1.2 Create `src/LiveLingo.Core/LiveLingo.Core.csproj` (net10.0, nullable, no platform deps). Add `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions`
- [x] 1.3 Restructure existing `src/LiveLingo.App/LiveLingo.App.csproj` to reference `LiveLingo.Core` and add `Microsoft.Extensions.DependencyInjection`
- [x] 1.4 Verify `dotnet build` succeeds for the entire solution

## 2. Core Interfaces & Records

- [x] 2.1 Create `Translation/ITranslationPipeline.cs`, `TranslationRequest.cs`, `TranslationResult.cs` with exact signatures from spec
- [x] 2.2 Create `Engines/ITranslationEngine.cs` (IDisposable, TranslateAsync, SupportsLanguagePair)
- [x] 2.3 Create `Processing/ITextProcessor.cs`, `ProcessingOptions.cs`, `ProcessingMode.cs`
- [x] 2.4 Create `LanguageDetection/ILanguageDetector.cs`, `DetectionResult.cs`
- [x] 2.5 Create `Models/IModelManager.cs`, `ModelDescriptor.cs`, `InstalledModel.cs`, `ModelDownloadProgress.cs`, `ModelType.cs`
- [x] 2.6 Create `CoreOptions.cs` with `ModelStoragePath` and `DefaultTargetLanguage` properties

## 3. Stub Implementations

- [x] 3.1 Create `Engines/StubTranslationEngine.cs` — returns `"[EN] {input}"`, `SupportsLanguagePair` always true
- [x] 3.2 Create `LanguageDetection/StubLanguageDetector.cs` — returns `DetectionResult("zh", 1.0f)`
- [x] 3.3 Create `Models/StubModelManager.cs` — `EnsureModelAsync` returns immediately, `ListInstalled` returns empty
- [x] 3.4 Create `Translation/TranslationPipeline.cs` — orchestrates detector → engine → processors, handles cancellation and same-language short-circuit

## 4. Core DI Registration

- [x] 4.1 Create `ServiceCollectionExtensions.cs` with `AddLiveLingoCore(Action<CoreOptions>?)` that registers pipeline, stubs, and options
- [x] 4.2 Verify `ITranslationPipeline` resolves correctly from a test `ServiceProvider`

## 5. Platform Abstractions

- [x] 5.1 Create `Platform/IPlatformServices.cs` (IDisposable, aggregates 4 sub-services)
- [x] 5.2 Create `Platform/IHotkeyService.cs`, `HotkeyBinding.cs`, `HotkeyEventArgs.cs`, `KeyModifiers.cs`
- [x] 5.3 Create `Platform/IWindowTracker.cs`, `TargetWindowInfo.cs`
- [x] 5.4 Create `Platform/ITextInjector.cs` (async interface with autoSend parameter)
- [x] 5.5 Create `Platform/IClipboardService.cs` (async SetText/GetText)

## 6. Windows Platform Migration

- [x] 6.1 Move `Services/Platform/Windows/GlobalKeyboardHook.cs` → `Platform/Windows/Win32HotkeyService.cs`, implement `IHotkeyService`
- [x] 6.2 Move `Services/Platform/Windows/WindowTracker.cs` → `Platform/Windows/Win32WindowTracker.cs`, implement `IWindowTracker`
- [x] 6.3 Extract clipboard logic from `TextInjector.cs` → `Platform/Windows/Win32ClipboardService.cs`, implement `IClipboardService`
- [x] 6.4 Move `Services/Platform/Windows/TextInjector.cs` → `Platform/Windows/Win32TextInjector.cs`, implement `ITextInjector` (inject `IClipboardService`, wrap sync code in `Task.Run`)
- [x] 6.5 Move `Services/Platform/Windows/NativeMethods.cs` → `Platform/Windows/NativeMethods.cs`
- [x] 6.6 Create `Platform/Windows/WindowsPlatformServices.cs` composing all 4 services
- [x] 6.7 Move diagnostic tools to `Platform/Windows/Diagnostics/` with `#if DEBUG` guards

## 7. DI Assembly & ViewModel Refactoring

- [x] 7.1 Refactor `App.axaml.cs` — create `ServiceCollection`, call `AddLiveLingoCore()`, register `WindowsPlatformServices`, build provider
- [x] 7.2 Refactor `OverlayViewModel` — constructor takes `(TargetWindowInfo, ITranslationPipeline, ITextInjector)`, remove all static calls
- [x] 7.3 Refactor `OverlayWindow.axaml.cs` — receive ViewModel with injected dependencies, wire `InjectAsync(autoSend)` on Ctrl+Enter
- [x] 7.4 Update `App.ShowOverlay()` — resolve services from DI, create ViewModel with dependencies

## 8. Cleanup & Verification

- [x] 8.1 Delete old `Services/Platform/` directory and any remaining static call sites
- [x] 8.2 `dotnet build` — zero errors, zero warnings related to the refactoring
- [x] 8.3 Run app — Ctrl+Alt+T opens overlay, type text → shows `[EN] xxx` stub translation
- [x] 8.4 Run `--test-slack` diagnostic — verify injection behavior matches PoC (SendInput primary, WM_CHAR fallback)
- [x] 8.5 Verify `LiveLingo.Core.csproj` has no Avalonia or platform-specific references
