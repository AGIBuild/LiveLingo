## ADDED Requirements

### Requirement: MSI packaging target
`BuildTask` SHALL have a `PackMsi` Target that generates a Windows MSI installer using WiX Toolset v5. It SHALL depend on `Publish` and output to `releases/`.

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
The `vpk` and `wix` CLI tools SHALL both be available as dotnet tools. The tool manifest (`.config/dotnet-tools.json`) SHALL include `wix`.

#### Scenario: WiX tool available
- **WHEN** `dotnet tool restore` is executed
- **THEN** the `wix` command is available for MSI building

## MODIFIED Requirements

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
