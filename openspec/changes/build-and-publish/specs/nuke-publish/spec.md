## ADDED Requirements

### Requirement: Nuke Publish Target
`BuildTask.Publish` SHALL invoke `dotnet publish` on `src/LiveLingo.App/LiveLingo.App.csproj` with self-contained mode for the specified `Runtime`. Output SHALL go to `publish/{Runtime}/`. It SHALL depend on `Test`.

#### Scenario: Publish for Windows
- **WHEN** `nuke Publish --runtime win-x64` is executed
- **THEN** `dotnet publish src/LiveLingo.App -c Release -r win-x64 --self-contained -o publish/win-x64` runs
- **AND** `publish/win-x64/` contains the self-contained application

#### Scenario: Publish for macOS
- **WHEN** `nuke Publish --runtime osx-arm64` is executed
- **THEN** `dotnet publish src/LiveLingo.App -c Release -r osx-arm64 --self-contained -o publish/osx-arm64` runs
- **AND** `publish/osx-arm64/` contains the self-contained application

### Requirement: Nuke Pack Target
`BuildTask.Pack` SHALL invoke Velopack CLI (`vpk pack`) to generate an installer and update packages from the Publish output. It SHALL depend on `Publish`.

The `vpk pack` invocation SHALL include:
- `--packId LiveLingo`
- `--packVersion {Version}`
- `--packDir publish/{Runtime}`
- `--mainExe LiveLingo.App.exe` (Windows) or `--mainExe LiveLingo.App` (macOS)
- `--outputDir releases/`
- `--icon {icon-path}` when available

#### Scenario: Pack generates Windows installer
- **WHEN** `nuke Pack --runtime win-x64 --version 1.0.0` is executed after Publish
- **THEN** `releases/` contains a `LiveLingo-win-Setup.exe` installer
- **AND** `releases/` contains delta/full `.nupkg` update packages

#### Scenario: Pack generates macOS bundle
- **WHEN** `nuke Pack --runtime osx-arm64 --version 1.0.0` is executed after Publish
- **THEN** `releases/` contains a macOS distributable bundle

### Requirement: Version Parameter
`BuildTask` SHALL accept a `--version` parameter (semver2 format) for the Pack target. If not provided, it SHALL read from `<Version>` in `LiveLingo.App.csproj`.

#### Scenario: Version from parameter
- **WHEN** `nuke Pack --version 2.1.0` is executed
- **THEN** the generated package uses version `2.1.0`

#### Scenario: Version from csproj
- **WHEN** `nuke Pack` is executed without `--version` and `LiveLingo.App.csproj` contains `<Version>1.0.0</Version>`
- **THEN** the generated package uses version `1.0.0`

### Requirement: Runtime Parameter
`BuildTask` SHALL accept a `--runtime` parameter for Publish and Pack targets. It SHALL default to `win-x64`.

#### Scenario: Default runtime
- **WHEN** `nuke Publish` is executed without `--runtime`
- **THEN** the build uses `win-x64` as runtime

### Requirement: Output directories in gitignore
The `publish/` and `releases/` directories SHALL be listed in `.gitignore`.

#### Scenario: Gitignore updated
- **WHEN** the change is applied
- **THEN** `.gitignore` contains entries for `publish/` and `releases/`

### Requirement: Velopack CLI tool dependency
`build/_build.csproj` SHALL reference the `vpk` tool. The recommended approach is to install `vpk` as a .NET global/local tool or use `Nuke.Common`'s `ToolResolver`.

#### Scenario: vpk available during Pack
- **WHEN** `nuke Pack` is executed
- **THEN** `vpk` CLI is available and can be invoked
