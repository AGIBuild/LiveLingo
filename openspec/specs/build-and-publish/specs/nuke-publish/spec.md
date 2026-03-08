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
- `--mainExe LiveLingo.Desktop.exe` (Windows) or `--mainExe LiveLingo.Desktop` (macOS)
- `--outputDir releases/`

After `vpk pack`, the target SHALL rename the setup exe to `LiveLingo-{Version}-win-x64-Setup.exe` for consistent naming.

#### Scenario: Pack generates Windows installer
- **WHEN** `nuke Pack --runtime win-x64 --version 1.0.0` is executed after Publish
- **THEN** `releases/` contains `LiveLingo-1.0.0-win-x64-Setup.exe`
- **AND** Velopack nupkg/delta files remain in `releases/` for auto-update but are NOT uploaded to GitHub Release

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

### Requirement: MSI packaging target
`BuildTask` SHALL have a `PackMsi` Target that generates a Windows MSI installer using WiX Toolset. It SHALL depend on `Publish` and output to `releases/`.

#### Scenario: MSI generated for Windows
- **WHEN** `nuke PackMsi --runtime win-x64 --version 1.0.0` is executed after Publish
- **THEN** `releases/LiveLingo-{version}-win-x64.msi` is created
- **AND** the MSI installs LiveLingo to `%ProgramFiles%\LiveLingo\`

#### Scenario: PackMsi requires Windows runtime
- **WHEN** `PackMsi` is invoked with a non-Windows runtime (e.g., `osx-arm64`)
- **THEN** the target fails with a clear error message

### Requirement: WiX source file
A `build/windows/LiveLingo.wxs` file SHALL define the MSI package structure including:
- Product metadata (Name, Version, Manufacturer, UpgradeCode)
- Install directory (`ProgramFiles\LiveLingo`)
- All published files from `publish/{Runtime}/`
- Desktop and Start Menu shortcuts for `LiveLingo.Desktop.exe`

#### Scenario: WiX source structure
- **WHEN** `build/windows/LiveLingo.wxs` is inspected
- **THEN** it defines a `Package` element with `Name="LiveLingo"`
- **AND** it references the publish output directory for file harvesting

### Requirement: WiX tool dependency
The `vpk` and `wix` CLI tools SHALL both be available as dotnet tools. The tool manifest SHALL include `wix`.

#### Scenario: WiX tool available
- **WHEN** `dotnet tool restore` is executed
- **THEN** the `wix` command is available for MSI building
