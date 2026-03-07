## ADDED Requirements

### Requirement: VelopackApp initialization
`Program.Main()` SHALL call `VelopackApp.Build().Run()` before any Avalonia initialization. This handles Velopack install/uninstall/update lifecycle hooks.

#### Scenario: Normal startup
- **WHEN** the application starts normally (not via Velopack hook)
- **THEN** `VelopackApp.Build().Run()` returns immediately
- **AND** the Avalonia application launches normally

#### Scenario: Velopack install hook
- **WHEN** the application is launched by Velopack during installation
- **THEN** `VelopackApp.Build().Run()` handles the install hook and exits

### Requirement: IUpdateService interface
The application SHALL define an `IUpdateService` interface:

```csharp
public interface IUpdateService
{
    bool IsUpdateAvailable { get; }
    string? AvailableVersion { get; }
    Task<bool> CheckForUpdateAsync(CancellationToken ct = default);
    Task DownloadAndApplyAsync(IProgress<int>? progress = null, CancellationToken ct = default);
}
```

#### Scenario: Interface registered in DI
- **WHEN** the DI container is built
- **THEN** `IUpdateService` is registered as a singleton with `VelopackUpdateService` implementation

### Requirement: Startup update check
On application startup (in `App.OnFrameworkInitializationCompleted`), the application SHALL check for updates via `IUpdateService.CheckForUpdateAsync()`. If an update is available, the user SHALL be notified (via tray notification or dialog). The check SHALL NOT block application startup.

#### Scenario: Update available on startup
- **WHEN** the application starts and an update is available
- **THEN** the update check runs in the background
- **AND** the user is notified of the available update

#### Scenario: No update available
- **WHEN** the application starts and no update is available
- **THEN** the application proceeds normally without any notification

#### Scenario: Network failure during check
- **WHEN** the application starts and the update server is unreachable
- **THEN** the update check fails silently (logged as warning)
- **AND** the application proceeds normally

### Requirement: Periodic update check
The application SHALL check for updates periodically (every 4 hours) while running. The interval SHALL be configurable via `UserSettings`.

#### Scenario: Periodic check finds update
- **WHEN** 4 hours have elapsed since the last check
- **THEN** `CheckForUpdateAsync()` is called in the background
- **AND** if an update is found, the user is notified

### Requirement: Download and apply update
When the user accepts an update, `IUpdateService.DownloadAndApplyAsync()` SHALL download the update (with progress reporting) and apply it. The application SHALL restart after applying.

#### Scenario: Successful update
- **WHEN** the user accepts an available update
- **THEN** the update is downloaded with progress reporting
- **AND** the update is applied and the application restarts

#### Scenario: Download cancelled
- **WHEN** the user cancels during download
- **THEN** the download stops and no update is applied
- **AND** the application continues running normally

### Requirement: Update source configuration
The update source URL SHALL be configurable. Default SHALL point to a GitHub Releases URL or a placeholder URL.

#### Scenario: Custom update source
- **WHEN** `UserSettings.UpdateUrl` is set to a custom URL
- **THEN** `UpdateManager` uses that URL for update checks

### Requirement: Velopack NuGet dependency
`LiveLingo.App.csproj` SHALL reference the `Velopack` NuGet package.

#### Scenario: Package reference added
- **WHEN** the project is restored
- **THEN** the Velopack assembly is available for compilation
