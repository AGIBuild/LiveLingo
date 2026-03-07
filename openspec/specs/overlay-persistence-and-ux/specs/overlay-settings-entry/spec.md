## ADDED Requirements

### Requirement: Settings icon in overlay title bar
The overlay title bar SHALL display a settings icon button (⚙) that opens the Settings window.

#### Scenario: Click settings icon
- **WHEN** user clicks the ⚙ icon in the overlay title bar
- **THEN** the Settings window opens

#### Scenario: Icon placement
- **WHEN** overlay is displayed
- **THEN** the ⚙ icon is positioned in the title bar, before the close (✕) button

### Requirement: Settings icon uses event for decoupling
The `OverlayViewModel` SHALL expose a `RequestOpenSettings` event. The view layer SHALL invoke the App-level settings display logic via this event.

#### Scenario: Event raised on click
- **WHEN** the settings command is executed in the ViewModel
- **THEN** `RequestOpenSettings` event is raised and the App layer calls `ShowSettings()`
