## ADDED Requirements

### Requirement: Win32 hotkey service
`Win32HotkeyService` SHALL implement `IHotkeyService` using `WH_KEYBOARD_LL` low-level keyboard hook via `SetWindowsHookEx`. The hook SHALL run on a dedicated thread and dispatch events to the Avalonia UI thread.

#### Scenario: Ctrl+Alt+T triggers overlay
- **WHEN** the user presses Ctrl+Alt+T globally (any application focused)
- **THEN** `HotkeyTriggered` SHALL fire on the UI thread with `HotkeyId = "overlay"`

#### Scenario: Hook cleanup on dispose
- **WHEN** `Win32HotkeyService.Dispose()` is called
- **THEN** the keyboard hook SHALL be uninstalled via `UnhookWindowsHookEx` and no further events SHALL fire

### Requirement: Win32 window tracker
`Win32WindowTracker` SHALL implement `IWindowTracker` using `GetForegroundWindow`, `GetWindowRect`, `GetWindowThreadProcessId`, and child window discovery for Electron/Chromium apps.

#### Scenario: Detect Chrome renderer child for Slack
- **WHEN** Slack Desktop is the foreground window
- **THEN** `InputChildHandle` SHALL be the HWND of `Chrome_RenderWidgetHostHWND` found via recursive `FindWindowExW`

#### Scenario: Fallback for native Win32 apps
- **WHEN** a native Win32 app (e.g. Notepad) is the foreground window
- **THEN** `InputChildHandle` SHALL be obtained from `GetGUIThreadInfo` focused HWND, or equal `Handle` if no child is focused

### Requirement: Win32 text injector dual-strategy
`Win32TextInjector` SHALL implement `ITextInjector` with two injection strategies:
1. **Primary (SendInput)**: Set clipboard via `IClipboardService`, bring target to foreground via `ForceForeground`, simulate Ctrl+V via `SendInput`, optionally simulate Enter.
2. **Fallback (WM_CHAR)**: Send each character via `PostMessageW(WM_CHAR)` to the `InputChildHandle`, optionally send Enter via `PostMessageW(WM_KEYDOWN/WM_KEYUP)`.

#### Scenario: SendInput strategy succeeds
- **WHEN** `InjectAsync` is called and the target window accepts foreground activation
- **THEN** text SHALL be injected via clipboard + SendInput Ctrl+V, and the method SHALL return successfully

#### Scenario: Fallback to WM_CHAR on SendInput failure
- **WHEN** `SendInput` returns 0 (failure, e.g. UIPI restriction)
- **THEN** the injector SHALL automatically fall back to WM_CHAR character-by-character injection

#### Scenario: AutoSend simulates Enter key
- **WHEN** `autoSend=true` and text injection succeeds
- **THEN** the Enter key SHALL be simulated after a short delay (100ms) to trigger message send

### Requirement: Win32 clipboard service
`Win32ClipboardService` SHALL implement `IClipboardService` using `OpenClipboard`, `SetClipboardData`, `GetClipboardData` Win32 APIs.

#### Scenario: Set text to clipboard
- **WHEN** `SetTextAsync("test")` is called
- **THEN** the system clipboard SHALL contain "test" in `CF_UNICODETEXT` format

### Requirement: Windows platform services aggregation
`WindowsPlatformServices` SHALL implement `IPlatformServices` by composing `Win32HotkeyService`, `Win32WindowTracker`, `Win32TextInjector`, and `Win32ClipboardService`.

#### Scenario: Construct WindowsPlatformServices
- **WHEN** `WindowsPlatformServices` is instantiated
- **THEN** all four sub-service properties SHALL be non-null

#### Scenario: Dispose releases hotkey hook
- **WHEN** `WindowsPlatformServices.Dispose()` is called
- **THEN** the hotkey hook SHALL be uninstalled and system resources released

### Requirement: NativeMethods P/Invoke declarations
All Win32 P/Invoke declarations SHALL be centralized in `NativeMethods.cs` under `LiveLingo.App.Platform.Windows` namespace. The file SHALL include declarations for: `SetWindowsHookEx`, `UnhookWindowsHookEx`, `CallNextHookEx`, `GetForegroundWindow`, `GetWindowRect`, `GetWindowThreadProcessId`, `FindWindowExW`, `GetClassNameW`, `GetGUIThreadInfo`, `SendInput`, `PostMessageW`, `SetForegroundWindow`, `AllowSetForegroundWindow`, `AttachThreadInput`, `OpenClipboard`, `CloseClipboard`, `SetClipboardData`, `GetClipboardData`, `EmptyClipboard`, `GlobalAlloc`, `GlobalLock`, `GlobalUnlock`.

#### Scenario: P/Invoke calls do not throw DllNotFoundException
- **WHEN** the application runs on Windows 10+
- **THEN** all P/Invoke methods SHALL resolve successfully from user32.dll and kernel32.dll
