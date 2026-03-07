## ADDED Requirements

### Requirement: Target language displays as text link
The target language selector SHALL display as a styled text link (language code with underline) in the overlay bottom bar, not as a native ComboBox.

#### Scenario: Default display
- **WHEN** overlay opens with target language `en`
- **THEN** the bottom bar shows `en` as a clickable text link with underline

### Requirement: Click activates dropdown panel
Clicking the target language text link SHALL open a styled popup panel listing all available target languages.

#### Scenario: Open language picker
- **WHEN** user clicks the target language text link
- **THEN** a popup panel appears above/below the link showing all available languages

### Requirement: Selection closes popup and updates display
Selecting a language from the popup SHALL close the popup, update the text link display, and trigger retranslation.

#### Scenario: Select new language
- **WHEN** user selects `ja` from the popup
- **THEN** the popup closes, the text link shows `ja`, and retranslation begins

### Requirement: Popup styling matches overlay theme
The popup panel SHALL use the overlay's dark theme styling (dark background, rounded corners, hover highlights) rather than default system dropdown style.

#### Scenario: Visual consistency
- **WHEN** the language popup is open
- **THEN** it has dark background (#1C1C1E or similar), rounded corners, and hover highlight on items
