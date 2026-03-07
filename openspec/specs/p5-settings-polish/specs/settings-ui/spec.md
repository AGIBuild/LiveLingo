## ADDED Requirements

### Requirement: Settings window with tabbed layout
A `SettingsWindow` SHALL provide four tabs: General, Translation, AI, Advanced. Changes SHALL be applied when clicking Save.

#### Scenario: Open settings from tray
- **WHEN** user right-clicks the system tray icon and selects "Settings"
- **THEN** the `SettingsWindow` SHALL open

#### Scenario: Save settings
- **WHEN** user modifies overlay hotkey to "Ctrl+Alt+L" and clicks Save
- **THEN** `settings.json` SHALL be updated and the hotkey SHALL be hot-reloaded

### Requirement: General tab
The General tab SHALL contain: overlay hotkey recorder, inject hotkey recorder, injection mode selector (PasteOnly/PasteAndSend), overlay opacity slider (0.5-1.0).

#### Scenario: Change overlay opacity
- **WHEN** user sets opacity slider to 0.7 and saves
- **THEN** subsequent overlays SHALL render with opacity 0.7

### Requirement: Translation tab with language pair management
The Translation tab SHALL display a list of configured language pairs with Add/Remove buttons, and a default pair selector.

#### Scenario: Add language pair
- **WHEN** user clicks Add and selects "English → Chinese"
- **THEN** `"en→zh"` SHALL be added to the language pairs list and model download SHALL be triggered if the MarianMT model is not installed

#### Scenario: Remove language pair
- **WHEN** user selects "zh→ja" and clicks Remove
- **THEN** the pair SHALL be removed from the list (model files are NOT deleted)

### Requirement: AI tab with model management
The AI tab SHALL display: default post-processing mode selector, list of installed models with sizes, and Delete button per model.

#### Scenario: Delete Qwen model
- **WHEN** user clicks Delete on the Qwen model entry
- **THEN** the model SHALL be deleted from disk and the entry SHALL be removed from the list

### Requirement: Advanced tab
The Advanced tab SHALL contain: model storage path (with folder picker), translation thread count, LLM thread count, log level selector, and "Open Log Folder" button.

#### Scenario: Change model storage path
- **WHEN** user selects a new folder and saves
- **THEN** `CoreOptions.ModelStoragePath` SHALL be updated (existing models are NOT moved)

### Requirement: System tray icon
The app SHALL display a system tray icon (Windows) or menu bar icon (macOS) with right-click menu containing "Settings" and "Quit".

#### Scenario: Quit from tray
- **WHEN** user right-clicks tray icon and selects "Quit"
- **THEN** the application SHALL shut down gracefully

### Requirement: Overlay position memory
When the user drags the overlay to a new position, the position SHALL be saved to `UISettings.LastOverlayPosition`. On next overlay open, the saved position SHALL be used (if within screen bounds).

#### Scenario: Position persists across sessions
- **WHEN** user drags overlay to (500, 300), closes it, and reopens
- **THEN** the overlay SHALL appear at (500, 300)

#### Scenario: Saved position off-screen
- **WHEN** saved position is outside current screen bounds (e.g. monitor disconnected)
- **THEN** the overlay SHALL fall back to automatic positioning relative to the target window

### Requirement: Language pair switcher in overlay
The overlay status bar SHALL include a language pair dropdown showing configured pairs. Switching pairs SHALL trigger re-translation of current text.

#### Scenario: Switch language pair
- **WHEN** user switches from "auto→en" to "zh→ja" in the overlay dropdown
- **THEN** the current `SourceText` SHALL be re-translated using the zh→ja model
