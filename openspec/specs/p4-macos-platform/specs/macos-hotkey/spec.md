## ADDED Requirements

### Requirement: CGEventTap global hotkey monitoring
`CoreGraphicsHotkeyService` SHALL implement `IHotkeyService` using `CGEventTapCreate` to listen for global keyboard events. The event tap SHALL run on a dedicated background thread with its own `CFRunLoop`.

#### Scenario: Register and detect Ctrl+Alt+T
- **WHEN** a hotkey binding for Ctrl+Alt+T is registered and the user presses Ctrl+Alt+T in any application
- **THEN** `HotkeyTriggered` SHALL fire with the correct `HotkeyId`

#### Scenario: Event tap thread lifecycle
- **WHEN** `CoreGraphicsHotkeyService` is created and a hotkey is registered
- **THEN** a background thread named "CGEventTap" SHALL be started running `CFRunLoopRun`

### Requirement: Event tap uses ListenOnly mode
The event tap SHALL use `CGEventTapOptions.ListenOnly` to avoid consuming keyboard events. Other applications SHALL continue to receive all key events normally.

#### Scenario: Non-interference with other apps
- **WHEN** Ctrl+Alt+T is pressed and LiveLingo's hotkey fires
- **THEN** the key event SHALL also be delivered to the foreground application

### Requirement: macOS key code mapping
A static `MacKeyMap` class SHALL map human-readable key names ("A"-"Z", "Space", "Return", "Escape", "Tab") to macOS virtual key codes.

#### Scenario: Map key "T" to macOS key code
- **WHEN** `MacKeyMap.GetKeyCode("T")` is called
- **THEN** the result SHALL be `0x11`

#### Scenario: Unknown key throws
- **WHEN** `MacKeyMap.GetKeyCode("UnknownKey")` is called
- **THEN** `ArgumentException` SHALL be thrown

### Requirement: Modifier flag matching
The service SHALL correctly map `KeyModifiers` flags to `CGEventFlags`: Ctrl → `MaskControl`, Alt → `MaskAlternate`, Shift → `MaskShift`, Meta → `MaskCommand`.

#### Scenario: Match Ctrl+Alt combination
- **WHEN** a key event has `MaskControl | MaskAlternate` flags and the binding requires `Ctrl | Alt`
- **THEN** the binding SHALL match

### Requirement: Disposal stops event tap
`Dispose()` SHALL disable the event tap via `CGEventTapEnable(false)`, stop the `CFRunLoop`, and release all CoreFoundation objects.

#### Scenario: Dispose releases resources
- **WHEN** `Dispose()` is called
- **THEN** the event tap thread SHALL exit and no further hotkey events SHALL fire

### Requirement: Failed event tap creation
If `CGEventTapCreate` returns `IntPtr.Zero` (typically due to missing Accessibility permission), the service SHALL throw `PlatformNotSupportedException` with a descriptive message.

#### Scenario: Missing Accessibility permission
- **WHEN** `Register` is called without Accessibility permission granted
- **THEN** `PlatformNotSupportedException` SHALL be thrown with message mentioning Accessibility
