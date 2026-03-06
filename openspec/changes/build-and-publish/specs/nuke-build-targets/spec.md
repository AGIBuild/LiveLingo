## ADDED Requirements

### Requirement: Nuke Clean Target
`BuildTask.Clean` SHALL delete the following directories: all `bin/` and `obj/` under `src/` and `tests/`, `publish/`, and `releases/`.

#### Scenario: Clean removes build artifacts
- **WHEN** `nuke Clean` is executed
- **THEN** all `bin/` and `obj/` directories under `src/` and `tests/` are deleted
- **AND** `publish/` and `releases/` directories are deleted if they exist

#### Scenario: Clean succeeds when directories already absent
- **WHEN** `nuke Clean` is executed and target directories do not exist
- **THEN** the target completes without error

### Requirement: Nuke Restore Target
`BuildTask.Restore` SHALL invoke `dotnet restore` on the solution file `LiveLingo.slnx`.

#### Scenario: Restore downloads packages
- **WHEN** `nuke Restore` is executed
- **THEN** `dotnet restore LiveLingo.slnx` runs successfully
- **AND** all project dependencies are resolved

### Requirement: Nuke Compile Target
`BuildTask.Compile` SHALL invoke `dotnet build` on the solution file with `--no-restore`, using the configured `Configuration` parameter. It SHALL depend on `Restore`.

#### Scenario: Compile builds all projects
- **WHEN** `nuke Compile` is executed with `Configuration = Release`
- **THEN** `dotnet build LiveLingo.slnx -c Release --no-restore` runs
- **AND** all projects compile without errors

### Requirement: Nuke Test Target
`BuildTask.Test` SHALL invoke `dotnet test` on the solution file with `--no-build`, using `test.runsettings` for code coverage collection. It SHALL depend on `Compile`.

#### Scenario: Test runs with coverage
- **WHEN** `nuke Test` is executed
- **THEN** `dotnet test LiveLingo.slnx --no-build -c {Configuration} --settings test.runsettings --collect:"XPlat Code Coverage"` runs
- **AND** test results and coverage reports are generated

#### Scenario: Test failure causes build failure
- **WHEN** `nuke Test` is executed and any test fails
- **THEN** the Nuke build exits with a non-zero exit code

### Requirement: Solution reference
`BuildTask` SHALL declare a `[Solution]` attribute field referencing `LiveLingo.slnx` for use across all targets.

#### Scenario: Solution auto-discovered
- **WHEN** `BuildTask` is instantiated
- **THEN** the `Solution` field is populated from the workspace root's `LiveLingo.slnx`
