## ADDED Requirements

### Requirement: Staged release workflow
The GitHub Actions release workflow (`.github/workflows/release.yml`) SHALL be structured as four sequential Jobs: **Compile → Test → Pack → Release**. Each Job SHALL depend on the previous Job's success.

#### Scenario: Compile job verifies build
- **WHEN** a release is triggered (tag push or workflow_dispatch)
- **THEN** the Compile Job runs `dotnet build` on the solution
- **AND** the workflow fails immediately if compilation fails

#### Scenario: Test job runs after compile
- **WHEN** the Compile Job succeeds
- **THEN** the Test Job executes `nuke Test` (unit tests + coverage)
- **AND** the workflow fails if tests fail

#### Scenario: Pack job runs after test
- **WHEN** the Test Job succeeds
- **THEN** the Pack Job executes platform-specific packaging via matrix strategy

#### Scenario: Release job runs after all packs
- **WHEN** all Pack matrix jobs succeed
- **THEN** the Release Job collects all artifacts and creates a GitHub Release

### Requirement: Cross-platform matrix build
The Pack Job SHALL use a matrix strategy to build for both `win-x64` (on `windows-latest`) and `osx-arm64` (on `macos-latest`) in parallel.

#### Scenario: Windows packaging
- **WHEN** the Pack Job runs on `windows-latest` with `runtime: win-x64`
- **THEN** it executes `nuke Pack PackMsi --runtime win-x64 --version {version}`
- **AND** uploads `*.exe` and `*.msi` files as workflow artifacts

#### Scenario: macOS packaging
- **WHEN** the Pack Job runs on `macos-latest` with `runtime: osx-arm64`
- **THEN** it executes `nuke PackMac --runtime osx-arm64 --version {version}`
- **AND** uploads `*.pkg` files as workflow artifacts

### Requirement: Clean release artifacts
The Release Job SHALL only upload installer files to the GitHub Release. It SHALL NOT include source code archives, `.nupkg`, delta packages, `RELEASES`, `.json`, or any other non-installer files.

#### Scenario: Only installers are released
- **WHEN** the Release Job creates a GitHub Release
- **THEN** the release assets contain only `*.exe`, `*.msi`, and `*.pkg` files
- **AND** no `.nupkg`, `.json`, `RELEASES`, or source archives are included

#### Scenario: Release naming convention
- **WHEN** release assets are uploaded
- **THEN** Windows exe installer is named `LiveLingo-{version}-win-x64-Setup.exe`
- **AND** Windows MSI is named `LiveLingo-{version}-win-x64.msi`
- **AND** macOS pkg is named `LiveLingo-{version}-osx-arm64.pkg`

### Requirement: Version resolution
The workflow SHALL resolve the version from the tag name (stripping the `v` prefix). It SHALL support both tag push triggers and `workflow_dispatch` with a manual tag input.

#### Scenario: Version from tag push
- **WHEN** a tag `v1.2.3` is pushed
- **THEN** the version `1.2.3` is passed to all build steps

#### Scenario: Version from workflow_dispatch
- **WHEN** the workflow is manually triggered with `tag: v2.0.0`
- **THEN** the version `2.0.0` is passed to all build steps
