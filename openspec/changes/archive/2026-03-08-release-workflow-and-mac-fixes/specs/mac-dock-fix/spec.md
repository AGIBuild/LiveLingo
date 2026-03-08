## ADDED Requirements

### Requirement: macOS agent activation policy after Avalonia init
The application SHALL set `NSApplicationActivationPolicyAccessory` (value `1`) via `setActivationPolicy:` in `App.OnFrameworkInitializationCompleted` on macOS, ensuring the call happens after Avalonia initialization completes.

#### Scenario: Dock icon hidden after startup
- **WHEN** the application starts on macOS
- **THEN** `setActivationPolicy:` is called with `NSApplicationActivationPolicyAccessory` after Avalonia initializes
- **AND** the application does NOT appear in the Dock

#### Scenario: Menu bar tray icon remains
- **WHEN** the application is running on macOS with no Dock icon
- **THEN** the system tray (menu bar) icon is visible and functional
- **AND** all tray menu items (Open Translator, Settings, Check Updates, About, Quit) are accessible

### Requirement: Remove pre-Avalonia SetMacAgentMode
The `SetMacAgentMode()` call in `Program.cs` (before `BuildAvaloniaApp().StartWithClassicDesktopLifetime`) SHALL be removed, as Avalonia overrides the activation policy during initialization.

#### Scenario: Program.cs cleanup
- **WHEN** the codebase is inspected
- **THEN** `Program.cs` does NOT contain `SetMacAgentMode()` or `setActivationPolicy:` calls
- **AND** the activation policy is set exclusively in `App.OnFrameworkInitializationCompleted`

### Requirement: Info.plist LSUIElement preserved
The `build/macos/Info.plist` SHALL retain `LSUIElement = true` as a fallback for .app bundle distribution.

#### Scenario: Info.plist configuration
- **WHEN** the macOS .app bundle is built
- **THEN** `Info.plist` contains `<key>LSUIElement</key><true/>` to suppress Dock icon at the OS level
