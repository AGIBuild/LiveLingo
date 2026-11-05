# Contributing to LiveLingo

Thanks for your interest in contributing! This guide covers everything you need to know to get the project running locally, understand the codebase, and submit quality contributions.

---

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0+ | Required |
| Git | Any recent | Required |
| [Nuke](https://nuke.build/) | Via `dotnet tool restore` | Build automation |
| [Velopack](https://velopack.io/) | Via `dotnet tool restore` | Windows (.msi) and macOS packaging, updates |
| [Stryker.NET](https://stryker-mutator.io/) | Via `dotnet tool restore` | Mutation testing |

---

## Quick Start

```bash
git clone https://github.com/AGIBuild/LiveLingo.git
cd LiveLingo

# Restore CLI tools (Velopack, Stryker)
dotnet tool restore

# Run in development mode
dotnet run --project src/LiveLingo.Desktop

# Or use Nuke
./build.sh Run       # macOS / Linux
./build.ps1 Run      # Windows
```

---

## Project Structure

```
LiveLingo/
├── src/
│   ├── LiveLingo.Core/              # Pure C# — composer domain, inference, models
│   │   ├── Drafting/ Dictation/     # The draft, dictation session, fidelity gate
│   │   ├── Cleanup/ Translation/    # Sentence cleanup and translation schedulers over llama-server
│   │   ├── Inference/ Cloud/        # llama-server host, local and opt-in cloud chat clients
│   │   ├── Speech/Native/           # sherpa-onnx adapters (excluded from unit coverage)
│   │   ├── Transcription/           # WAV import and SRT export
│   │   └── Models/                  # Catalog, download / install / cleanup collaborators
│   │
│   ├── LiveLingo.HfGguf/            # Hugging Face resolve URLs and the resumable downloader
│   │
│   └── LiveLingo.Desktop/           # Avalonia shell + web UI
│       ├── Composer/ Settings/      # View models, texts and the thin windows around the pages
│       ├── Web/                     # Bridge, view-state snapshots, intents, and Assets/ (the page:
│       │                            #   Preact + htm sources and CSS, embedded; no Node build)
│       ├── Platform/
│       │   ├── Windows/             # Win32 hotkeys, SendInput text injection
│       │   └── macOS/               # CGEventTap hotkeys, Cocoa text injection
│       └── Services/                # Settings, localization, update service
│
├── docs/superpowers/                # Design specs, plans, UX prototypes (see below)
├── poc/                             # Validated POCs (sources only; corpora are not committed)
├── tests/
│   ├── LiveLingo.Core.Tests/        # Unit tests for Core
│   ├── LiveLingo.Desktop.Tests/     # Unit tests for Desktop services, view models and view states
│   └── LiveLingo.Web.Tests/         # Playwright tests of the page on WebKit and Chromium
│                                    #   (golden fixtures are written by Desktop.Tests with UPDATE_FIXTURES=1)
│
├── build/                           # Nuke build automation
│   ├── BuildTask.cs                 # All build targets
│   └── macos/                       # macOS packaging scripts
│
├── .github/workflows/               # CI/CD (compile → test → pack → release)
├── CHANGELOG.md                     # Release notes (Keep a Changelog format)
└── test.runsettings                 # Code coverage configuration
```

---

## Architecture Principles

### Core Rule

**UI doesn't test logic. Logic doesn't depend on UI.**

This is the single most important rule. It ensures ViewModels are fully testable without any UI framework, and Views remain thin binding layers.

### ViewModel Layer

- **No Avalonia references** — ViewModels must never import `Avalonia.*` namespaces
- **No threading** — No `Dispatcher`, `Dispatcher.UIThread`, or thread scheduling APIs
- **Interface-only dependencies** — `ISettingsService`, `IModelManager`, `ITextInjector`, `IVoiceInput`, etc.
- **CommunityToolkit.Mvvm** — Use `ObservableObject`, `RelayCommand`, `[ObservableProperty]`
- **UI communication** — Via events (`event Action? RequestClose`) or property change notifications

### View Layer

- The windows are thin Avalonia shells around a web view; the page draws snapshots and **does not hold logic**
- Window code is limited to Avalonia-specific interactions: window positioning, focus, parking
- `Dispatcher` calls are only allowed in Views or `App.axaml.cs`

### Platform Layer

- All platform differences go through interfaces (`IPlatformServices`, `IClipboardService`, `ITextInjector`)
- Implementations live in `Platform/Windows/` or `Platform/macOS/`
- **No P/Invoke or platform APIs in ViewModels** — ever

---

## Build Targets

LiveLingo uses [Nuke](https://nuke.build/) for build automation. All targets are defined in `build/BuildTask.cs`.

```bash
# Common targets
./build.sh Clean                                     # Clean artifacts
./build.sh Build                                     # Compile solution
./build.sh Test                                      # Run tests + coverage + mutation
./build.sh Run                                       # Launch in Debug mode
./build.sh Mutate                                    # Run mutation testing only

# Packaging
./build.sh Publish --Runtime osx-arm64               # Publish for macOS
./build.ps1 Publish --Runtime win-x64                # Publish for Windows
./build.sh Pack --Runtime osx-arm64 --Version 1.0.0  # macOS: .pkg installer + update package and feed
./build.ps1 Pack --Runtime win-x64 --Version 1.0.0   # Windows: .msi installer + update package and feed
```

End-to-end probes download the pinned models (SHA256-verified) and use real inference; they are not part of the unit-test gate:

```bash
./build.sh ProbeTranslation                                   # runtime + translator on llama-server
./build.sh ProbeVoice --ProbeCorpus poc/voice-dictation/corpus # speech models + dictation + cleanup on recorded audio
```

Use `--Configuration Release` for release builds. On CI, configuration defaults to Release automatically.

### Obsolete model cleanup

Settings lists model directories that are not in `ModelCatalog` (for example the 1.x Gemma, Marian and Qwen
processors) with their total size, and removes them through `IModelManager.CleanObsoleteModelsAsync` only when the user
presses *Remove old models*. Nothing is deleted silently (design spec, S7).

### Settings migration

`SettingsMigration` reads a schema 2 file (1.x) and keeps the hotkey, interface language, update settings and the
advanced paths and levels. Any other schema version starts from defaults. A new schema version must add its own
migration instead of resetting the file.

---

## Model downloads

Model downloads are a **process-global concern**: the first-run banner, the
settings model cards, the composer status line and any background
"ensure model before use" call must all observe the same lifecycle so that

- a download started from one surface is visible on every other surface, and
- two surfaces asking for the same model never produce two parallel downloads.

`IModelDownloadCoordinator` (default impl `InProcessModelDownloadCoordinator`)
is the single owner of that lifecycle.

### Rules

1. **UI code MUST go through the coordinator.**

   ```csharp
   await _coordinator.StartAsync(descriptor, ct);
   _coordinator.StateChanged += OnDownloadStateChanged; // for live progress
   var state = _coordinator.GetState(descriptor.Id);    // for snapshot reads
   _coordinator.Cancel(descriptor.Id);                  // for cancel
   ```

   This applies to every ViewModel, Service or background coordinator that
   wants to *report* a download to the user.

2. **`IModelManager.EnsureModelAsync` is a low-level primitive.** It is
   marked `[EditorBrowsable(Advanced)]` and the XML doc points new callers
   at the coordinator. The only legitimate direct callers are
   non-user-facing on-demand model loads inside an engine, where byte-level
   dedup is still provided by `InflightDownloadRegistry` but no UI surface
   needs to reflect progress.

3. **Subscribers must dispose cleanly.** Any class that subscribes to
   `IModelDownloadCoordinator.StateChanged` must implement `IDisposable` (or
   already does) and unsubscribe in `Dispose` — otherwise the coordinator
   keeps the subscriber alive for the lifetime of the process.

4. **Tests use `InProcessModelDownloadCoordinator` over a mocked
   `IModelManager`** for integration-style tests, and mock the coordinator
   directly (`Substitute.For<IModelDownloadCoordinator>()` +
   `Raise.Event<EventHandler<ModelDownloadState>>(...)`) for ViewModel unit
   tests that want to drive specific lifecycle events.

For tests that don't need a real coordinator, substitute `IModelDownloadCoordinator`.

---

## Testing Standards

The `Test` target enforces three quality gates. **All must pass for CI to succeed.**

### 1. Code Coverage

| Metric | Threshold | Measured by |
|--------|-----------|-------------|
| Line coverage | ≥ **80%** | XPlat Code Coverage (Cobertura) |
| Branch coverage | ≥ **65%** | XPlat Code Coverage (Cobertura) |

**What's excluded from coverage** (configured in `test.runsettings`):
- Platform implementations (`Platform.Windows.*`, `Platform.macOS.*`)
- Window shells and the web view host (`ComposerWindow`, `SettingsWindow`, `WebSurface`, `StorageFileDialogs`)
- Infrastructure (`Program`, `App`, `VelopackUpdateService`)
- AI model hosts and native adapters (`Inference.LlamaServerHost*`, `Speech.Native.*`)
  — covered by end-to-end Nuke probe targets instead of unit-test mocks

### 2. Mutation Testing

| Metric | Threshold | Tool |
|--------|-----------|------|
| Mutation score | ≥ **80%** | [Stryker.NET](https://stryker-mutator.io/) |

Stryker only targets the pure-logic modules of `LiveLingo.Core`
(`Drafting`, `Translation`, `Dictation`, `Cleanup`; see `tests/LiveLingo.Core.Tests/stryker-config.json`).
Source generators in Avalonia projects are incompatible with it.

### 3. Test Conventions

- **Mock everything** — ViewModels use [NSubstitute](https://nsubstitute.github.io/) for all dependencies
- **No real I/O** — Tests must not touch disk, network, or UI frameworks
- **Naming** — `MethodName_Scenario_ExpectedResult` (e.g., `Translate_EmptyInput_ReturnsEmpty`)

---

## Release Workflow

The CI pipeline (`.github/workflows/release.yml`) runs on tag push (`v*`) with four sequential stages:

```
compile  →  test  →  pack  →  release
   │          │        │         │
   │          │        │         └─ Check the update feeds, then create the GitHub Release
   │          │        └─ Matrix build: win-x64 (.msi) / osx-arm64 (.pkg), each with its update package and feed
   │          └─ Unit tests + coverage gates (on macOS, where the web tests run both browsers)
   └─ Build verification
```

To release: push a tag `v<semver>` (or run *Create Tag*; a version with a suffix such as `2.1.0-rc.1` is published as a
pre-release). The version is the tag; nothing in the source needs editing.

### Automatic updates

Installed applications update themselves from the GitHub releases of the repository
(`Update.UpdateUrl` in the settings, default `https://github.com/AGIBuild/LiveLingo`) through
[Velopack](https://velopack.io/):

- Every release carries, per platform, the full package (`LiveLingo-<version>-full.nupkg` on Windows,
  `LiveLingo-<version>-osx-full.nupkg` on macOS) and its feed (`releases.win.json` / `releases.osx.json`). The channels
  (`win`, `osx`) are Velopack's defaults for the platforms, so the feeds do not collide. The *release* job refuses to
  publish a release whose feed does not name a package that is in it.
- The application checks at start and every `Update.CheckIntervalHours`, downloads a newer version in the background,
  and then offers *Restart to Update* in the tray menu; quitting also installs it. Pre-releases are not offered.
  *Settings → Models → Updates* shows the version, the progress of a check or download, and the result; the tray's
  *Check for Updates* opens it and starts a check.
- A release lists only what is needed: the installer (`.msi` / `.pkg`), the full package (the file the application
  downloads to update; it is not a library) and the feed. Velopack's `Setup.exe` and legacy `RELEASES` files are built
  but not released. There is one feed per platform because a feed lists packages without saying which system they are for.
- Only an application installed by an installer of the release (the .msi or the .pkg — both Velopack) can
  update itself; the .msi is built by `vpk pack --msi` and installs per user, so updates never need elevation. A
  development build and a copy installed by the hand-built `.msi` of 2.1.0 and earlier cannot; they skip the check, and
  *Updates* says so and links to the download page.
- A release without a feed (the 1.x releases) is skipped. The application reads the latest 10 releases.
- Installers are not signed, so Windows SmartScreen and macOS Gatekeeper warn on the first install; updates run from the
  installed application. Signing (`--signAppIdentity`, `--notaryProfile`) is not set up.

Only the installers, the update packages and the feeds are published — no source archives or debug files.

---

## Making Changes

1. **Fork & branch** — Create a feature branch from `main`
2. **Write tests first** — Or at least alongside your code
3. **Run `./build.sh Test`** — Ensure all quality gates pass locally
4. **Keep commits focused** — One logical change per commit
5. **Open a PR** — Describe what and why, link related issues

### What makes a good PR

- Tests for new logic (ViewModels, Core services)
- No Avalonia references leaked into ViewModels
- Coverage and mutation thresholds maintained
- Follows the v3 prototype (`docs/superpowers/prototypes/livelingo-ux-v3/`): design tokens in `Web/Assets/styles/`, system font, light and dark themes
- UI changes keep the contract: state lives in C#; the page only draws a snapshot and sends closed-set intents

---

## Adding a New Language

Only Chinese ↔ English is validated (see `poc/text-translation/REPORT.md` and the
voice design spec). Adding a language requires a new evaluation run on a
representative corpus before it is exposed in the UI; UI locales live in
`Resources/i18n/`.

---

## Need Help?

Open an issue for bugs, questions, or feature ideas. We're happy to help you get started!
