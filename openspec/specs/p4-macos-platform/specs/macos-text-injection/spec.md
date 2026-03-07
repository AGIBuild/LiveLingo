## ADDED Requirements

### Requirement: Primary injection via AXUIElement SetValue
`MacTextInjector` SHALL attempt text injection by: (1) getting the target app's `AXUIElement` via PID, (2) querying `AXFocusedUIElement`, (3) reading current `AXValue` and `AXSelectedTextRange`, (4) inserting text at cursor position via `AXUIElementSetAttributeValue`.

#### Scenario: Inject into TextEdit
- **WHEN** `InjectAsync` is called with TextEdit as the target and text "Hello"
- **THEN** "Hello" SHALL appear at the cursor position in TextEdit

#### Scenario: Inject at cursor position (not replace all)
- **WHEN** the target field contains "abc|def" (cursor at position 3) and "XYZ" is injected
- **THEN** the field SHALL contain "abcXYZdef" with cursor at position 6

### Requirement: Fallback injection via clipboard + Cmd+V
If `AXUIElementSetAttributeValue` fails (returns non-zero error code), `MacTextInjector` SHALL fall back to: (1) set clipboard text via `IClipboardService`, (2) simulate Cmd+V via `CGEventPost`.

#### Scenario: AXUIElement fails, Cmd+V succeeds
- **WHEN** `AXUIElementSetAttributeValue` returns error and Cmd+V fallback is used
- **THEN** text SHALL be pasted from clipboard into the target application

### Requirement: AutoSend simulates Return key
When `autoSend=true`, after text injection (either strategy), `MacTextInjector` SHALL simulate the Return key press via `CGEventPost` with a 100ms delay.

#### Scenario: AutoSend triggers message send
- **WHEN** `InjectAsync` is called with `autoSend=true` on Slack
- **THEN** the Enter key SHALL be simulated after text injection, triggering message send

#### Scenario: AutoSend=false no Return key
- **WHEN** `InjectAsync` is called with `autoSend=false`
- **THEN** only text SHALL be injected without pressing Return

### Requirement: CGEvent key simulation
Key simulation SHALL use `CGEventCreateKeyboardEvent` + `CGEventPost` with `CGEventSourceStateID.HIDSystemState`. Key-down and key-up SHALL be sent as separate events with 30-50ms delay between them.

#### Scenario: Cmd+V simulation
- **WHEN** clipboard paste is simulated
- **THEN** key-down for V with Command flag SHALL be posted, followed by 50ms delay, then key-up for V

### Requirement: Electron/Slack Accessibility compatibility
The injector SHALL log whether `AXFocusedUIElement` returns a valid element for Electron apps. If Electron's Accessibility support is insufficient (AXValue set returns error), the fallback strategy SHALL be used automatically.

#### Scenario: Slack Desktop (Electron) injection
- **WHEN** `InjectAsync` targets Slack Desktop on macOS
- **THEN** text SHALL be injected via AXUIElement or Cmd+V fallback, with diagnostic logging

### Requirement: MacClipboardService via NSPasteboard
`MacClipboardService` SHALL implement `IClipboardService` using Objective-C runtime calls to `[NSPasteboard generalPasteboard]` for `clearContents`, `setString:forType:`, and `stringForType:`.

#### Scenario: Set and retrieve clipboard
- **WHEN** `SetTextAsync("test")` is called
- **THEN** `GetTextAsync()` SHALL return "test"
