## ADDED Requirements

### Requirement: Foreground window detection via NSWorkspace
`MacWindowTracker` SHALL implement `IWindowTracker` by querying `NSWorkspace.SharedWorkspace.FrontmostApplication` for the active app, then using `CGWindowListCopyWindowInfo` to retrieve window geometry.

#### Scenario: Get Slack window info
- **WHEN** Slack is the foreground application
- **THEN** `GetForegroundWindowInfo()` SHALL return a `TargetWindowInfo` with Slack's process name, window title, and bounding rect

#### Scenario: No foreground window
- **WHEN** the Finder desktop is focused with no app windows visible
- **THEN** `GetForegroundWindowInfo()` SHALL return null

### Requirement: Window geometry from CGWindowList
The tracker SHALL use `CGWindowListCopyWindowInfo` with `OptionOnScreenOnly | ExcludeDesktopElements` to find the first window belonging to the frontmost application's PID at window layer 0 (standard layer).

#### Scenario: Correct window bounds
- **WHEN** Slack's main window is at position (100, 200) with size 1200x800
- **THEN** the returned `TargetWindowInfo` SHALL have `Left=100, Top=200, Width=1200, Height=800`

### Requirement: InputChildHandle equals Handle on macOS
On macOS, there is no concept of child window handles like Windows' `Chrome_RenderWidgetHostHWND`. `InputChildHandle` SHALL be set equal to `Handle` (the window number).

#### Scenario: InputChildHandle same as Handle
- **WHEN** `GetForegroundWindowInfo()` returns a result
- **THEN** `InputChildHandle` SHALL equal `Handle`
