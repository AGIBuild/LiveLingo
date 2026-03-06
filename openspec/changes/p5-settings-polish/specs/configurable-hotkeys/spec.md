## ADDED Requirements

### Requirement: HotkeyRecorder custom control
A `HotkeyRecorder` control (extending TextBox) SHALL capture key combinations when focused. It SHALL display "Press key combination..." when recording, then show the captured combination (e.g. "Ctrl+Alt+L").

#### Scenario: Record Ctrl+Alt+L
- **WHEN** the HotkeyRecorder has focus and user presses Ctrl+Alt+L
- **THEN** the `Hotkey` property SHALL be set to `"Ctrl+Alt+L"` and the TextBox SHALL display the same

#### Scenario: Ignore standalone modifier keys
- **WHEN** user presses only Ctrl (without a non-modifier key)
- **THEN** the recording SHALL continue waiting for a complete combination

### Requirement: HotkeyParser string to binding conversion
`HotkeyParser.Parse(id, hotkeyString)` SHALL convert a human-readable string like "Ctrl+Alt+T" into a `HotkeyBinding` with correct `KeyModifiers` and key name.

#### Scenario: Parse Ctrl+Alt+T
- **WHEN** `HotkeyParser.Parse("overlay", "Ctrl+Alt+T")` is called
- **THEN** the result SHALL have `Modifiers = Ctrl | Alt` and `Key = "T"`

#### Scenario: Parse Cmd+Shift+Space for macOS
- **WHEN** `HotkeyParser.Parse("overlay", "Cmd+Shift+Space")` is called
- **THEN** the result SHALL have `Modifiers = Meta | Shift` and `Key = "Space"`

#### Scenario: Missing key throws
- **WHEN** `HotkeyParser.Parse("id", "Ctrl+Alt")` is called (no non-modifier key)
- **THEN** `ArgumentException` SHALL be thrown

### Requirement: Hot-reload on settings change
When `SettingsChanged` fires with updated hotkey values, the app SHALL unregister old hotkeys and register new ones without requiring a restart.

#### Scenario: Change hotkey without restart
- **WHEN** user changes overlay hotkey from "Ctrl+Alt+T" to "Ctrl+Alt+L" and saves
- **THEN** pressing Ctrl+Alt+T SHALL no longer trigger the overlay, and pressing Ctrl+Alt+L SHALL trigger it

### Requirement: macOS modifier key display
On macOS, modifier keys SHALL display as symbols: Ctrl → "⌃", Alt/Option → "⌥", Shift → "⇧", Cmd → "⌘".

#### Scenario: macOS hotkey display
- **WHEN** the hotkey "Cmd+Shift+T" is displayed on macOS
- **THEN** the display text SHALL show "⌘⇧T"
