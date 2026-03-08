# Contributing to LiveLingo

Thanks for your interest in contributing! This guide covers everything you need to know to get the project running locally, understand the codebase, and submit quality contributions.

---

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0+ | Required |
| Git | Any recent | Required |
| [Nuke](https://nuke.build/) | Via `dotnet tool restore` | Build automation |
| [Velopack](https://velopack.io/) | Via `dotnet tool restore` | Windows installer packaging |
| [WiX Toolset](https://wixtoolset.org/) | Via `dotnet tool restore` | Windows MSI packaging |
| [Stryker.NET](https://stryker-mutator.io/) | Via `dotnet tool restore` | Mutation testing |

---

## Quick Start

```bash
git clone https://github.com/user/LiveLingo.git
cd LiveLingo

# Restore CLI tools (Velopack, WiX, Stryker)
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
│   ├── LiveLingo.Core/              # Pure C# — translation pipeline, AI engines, models
│   │   ├── Engines/                 # LLamaSharp (Qwen2.5) + MarianMT ONNX engines
│   │   ├── Models/                  # Model registry, download manager
│   │   ├── Processing/              # Post-processing (summarize, optimize, colloquialize)
│   │   ├── Settings/                # User settings model + JSON persistence
│   │   └── Translation/             # Translation pipeline orchestration
│   │
│   └── LiveLingo.Desktop/           # Avalonia 11 UI application
│       ├── Platform/
│       │   ├── Windows/             # Win32 hotkeys, SendInput text injection
│       │   └── macOS/               # CGEventTap hotkeys, Cocoa text injection
│       ├── Views/                   # AXAML windows + code-behind
│       ├── ViewModels/              # CommunityToolkit.Mvvm — pure C#, no Avalonia refs
│       ├── Services/                # Localization, update service
│       └── Styles/                  # AppTheme, adaptive color system
│
├── tests/
│   ├── LiveLingo.Core.Tests/        # Unit tests for Core
│   └── LiveLingo.Desktop.Tests/     # Unit tests for ViewModels
│
├── build/                           # Nuke build automation
│   ├── BuildTask.cs                 # All build targets
│   ├── windows/LiveLingo.wxs        # WiX MSI definition
│   └── macos/                       # macOS packaging scripts
│
├── .github/workflows/               # CI/CD (compile → test → pack → release)
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
- **Interface-only dependencies** — `ISettingsService`, `ITranslationPipeline`, `ITextInjector`, `IModelManager`, etc.
- **CommunityToolkit.Mvvm** — Use `ObservableObject`, `RelayCommand`, `[ObservableProperty]`
- **UI communication** — Via events (`event Action? RequestClose`) or property change notifications

### View Layer

- Views handle **binding and layout only**
- Code-behind is limited to Avalonia-specific interactions: window positioning, focus, animation, drag
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
./build.ps1 Pack --Runtime win-x64 --Version 1.0.0   # Velopack .exe installer
./build.ps1 PackMsi --Runtime win-x64 --Version 1.0.0 # WiX .msi installer
./build.sh PackMac --Runtime osx-arm64 --Version 1.0.0 # macOS .pkg installer
```

Use `--Configuration Release` for release builds. On CI, configuration defaults to Release automatically.

---

## Testing Standards

The `Test` target enforces three quality gates. **All must pass for CI to succeed.**

### 1. Code Coverage

| Metric | Threshold | Measured by |
|--------|-----------|-------------|
| Line coverage | ≥ **96%** | XPlat Code Coverage (Cobertura) |
| Branch coverage | ≥ **92%** | XPlat Code Coverage (Cobertura) |

**What's excluded from coverage** (configured in `test.runsettings`):
- Platform implementations (`Platform.Windows.*`, `Platform.macOS.*`)
- View layer (`Views.*`, `Controls.*`)
- Infrastructure (`Program`, `App`, `VelopackUpdateService`)
- AI model hosts that require GPU (`QwenModelHost`, `MarianOnnxEngine`)

### 2. Mutation Testing

| Metric | Threshold | Tool |
|--------|-----------|------|
| Mutation score | ≥ **80%** | [Stryker.NET](https://stryker-mutator.io/) |

Stryker only targets `LiveLingo.Core` (source generators in Avalonia projects are incompatible).

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
   │          │        │         └─ Create GitHub Release with installers
   │          │        └─ Matrix build: win-x64 (.exe + .msi) / osx-arm64 (.pkg)
   │          └─ Unit tests + coverage + mutation gates
   └─ Build verification (all platforms)
```

Only final installer artifacts (`.exe`, `.msi`, `.pkg`) are published — no source archives or debug files.

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
- Follows existing code style (Inter font, dark theme conventions)

---

## Adding a New Language

1. Add the language code to the supported list in `LiveLingo.Core`
2. Ensure the AI model supports it (Qwen2.5 covers most major languages)
3. Add UI translations in `Resources/i18n/` if adding a new UI locale
4. Add tests for the new language pair

---

## Need Help?

Open an issue for bugs, questions, or feature ideas. We're happy to help you get started!
