## ADDED Requirements

### Requirement: Accessibility permission check
`AccessibilityPermission.IsGranted()` SHALL call `AXIsProcessTrustedWithOptions(IntPtr.Zero)` to check if Accessibility permission is currently granted.

#### Scenario: Permission granted
- **WHEN** the user has granted Accessibility permission to LiveLingo
- **THEN** `IsGranted()` SHALL return `true`

#### Scenario: Permission not granted
- **WHEN** the user has not granted Accessibility permission
- **THEN** `IsGranted()` SHALL return `false`

### Requirement: Permission request with system prompt
`AccessibilityPermission.RequestAndCheck()` SHALL call `AXIsProcessTrustedWithOptions` with `kAXTrustedCheckOptionPrompt = true` to trigger the system authorization dialog.

#### Scenario: Request triggers system dialog
- **WHEN** `RequestAndCheck()` is called without existing permission
- **THEN** macOS SHALL display the standard "LiveLingo would like to control this computer" dialog

### Requirement: First-launch permission guide
On macOS, if `IsGranted()` returns `false` at app startup, the app SHALL display a guide window with instructions: (1) click "Open Settings" to navigate to System Settings → Accessibility, (2) toggle LiveLingo ON, (3) click "Verify" to re-check.

#### Scenario: Guide window displayed on first launch
- **WHEN** the app starts on macOS without Accessibility permission
- **THEN** a guide window SHALL appear with "Open Settings", "Verify", and "Quit" buttons

#### Scenario: Open Settings navigates to system preferences
- **WHEN** user clicks "Open Settings"
- **THEN** macOS System Settings SHALL open to the Privacy & Security → Accessibility pane

#### Scenario: Verify succeeds after granting
- **WHEN** user grants permission and clicks "Verify"
- **THEN** `IsGranted()` SHALL return `true` and the guide window SHALL close

#### Scenario: Verify fails prompts restart
- **WHEN** user clicks "Verify" but permission is still not effective (macOS caching)
- **THEN** the guide SHALL display a message suggesting app restart

### Requirement: Info.plist Accessibility usage description
The app's `Info.plist` SHALL contain `NSAccessibilityUsageDescription` explaining why LiveLingo needs Accessibility access.

#### Scenario: Usage description present
- **WHEN** macOS displays the Accessibility permission dialog
- **THEN** it SHALL include LiveLingo's custom usage description text
