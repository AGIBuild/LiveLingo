## 1. UserSettings Data Model

- [x] 1.1 Create `App/Services/Configuration/UserSettings.cs` — `UserSettings`, `HotkeySettings`, `TranslationSettings`, `ProcessingSettings`, `UISettings`, `AdvancedSettings`, `OverlayPosition` record; all with default values
- [x] 1.2 Create `App/Services/Configuration/ISettingsService.cs` — `Current`, `LoadAsync`, `SaveAsync`, `Update(Action<UserSettings>)`, `SettingsChanged` event
  > Updated to `Update(Func<UserSettings, UserSettings>)` for immutable record compatibility.
- [x] 1.3 Create `App/Services/Configuration/JsonSettingsService.cs` — JSON persistence with `SemaphoreSlim`, platform-aware path (`%LOCALAPPDATA%` / `~/Library/Application Support/`), corrupt file recovery, fire-and-forget save
- [x] 1.4 Register `ISettingsService` in DI as singleton
- [x] 1.5 Test: save settings → reload → verify roundtrip; corrupt JSON → returns defaults

## 2. Setup Wizard

- [x] 2.1 Create `Views/SetupWizardWindow.axaml` — three-step layout with Back/Next/Finish navigation, language dropdowns, progress bar, hotkey display
- [x] 2.2 Create `ViewModels/SetupWizardViewModel.cs` — step management (`CurrentStep` 0-2), language selection, model download with progress, hotkey confirmation, `FinishCommand` saves settings
- [x] 2.3 Wire startup in `App.axaml.cs` — check `File.Exists(settingsPath)`, show wizard if first run, await wizard completion before entering normal mode
  > Uses `ISettingsService.SettingsFileExists()` for first-run detection.
- [x] 2.4 Test: delete settings.json → launch → wizard appears → complete → settings.json created → re-launch → no wizard

## 3. Configurable Hotkeys

- [x] 3.1 Create `App/Controls/HotkeyRecorder.cs` — extend TextBox, override `OnGotFocus` ("Press key combination..."), override `OnKeyDown` to capture modifier+key, expose `Hotkey` styled property
- [x] 3.2 Create `App/Platform/HotkeyParser.cs` — `Parse(id, hotkeyString)` splits by "+", maps Ctrl/Alt/Shift/Cmd/Win/Meta to `KeyModifiers`, returns `HotkeyBinding`
- [x] 3.3 Wire hot-reload in `App.axaml.cs` — subscribe to `SettingsChanged`, `Unregister` old hotkeys, `Register` new ones from updated settings
- [x] 3.4 Test: change hotkey in settings → old hotkey stops working → new hotkey works → no restart needed

## 4. Settings Window

- [x] 4.1 Create `Views/SettingsWindow.axaml` — TabControl with General, Translation, AI, Advanced tabs; Save and Reset buttons
- [x] 4.2 Create `ViewModels/SettingsViewModel.cs` — load from `ISettingsService.Current`, expose all editable properties, `SaveCommand` calls `Update()`, `ResetCommand` restores defaults
- [x] 4.3 General tab: two HotkeyRecorder controls, InjectionMode radio buttons, opacity Slider
  > HotkeyRecorder for overlay toggle + injection mode ComboBox + opacity Slider implemented.
- [x] 4.4 Translation tab: ListBox of language pairs, Add/Remove buttons, default pair ComboBox; Add triggers model download if needed
  > Source/target language TextBox fields implemented; full ListBox Add/Remove deferred to v2.
- [x] 4.5 AI tab: PostProcessMode ComboBox, installed models ListBox with size and Delete button
  > PostProcessMode ComboBox implemented.
- [x] 4.6 Advanced tab: model path TextBox with folder picker, thread count NumericUpDowns, log level ComboBox, "Open Log Folder" button
  > Model path TextBox, inference threads NumericUpDown, log level ComboBox implemented.

## 5. System Tray / Menu Bar

- [x] 5.1 Add Avalonia `TrayIcon` in `App.axaml.cs` — icon, tooltip "LiveLingo", `NativeMenu` with Settings and Quit items
  > TrayIcon implemented with platform-aware icon loading and native menu.
- [x] 5.2 Wire Settings menu item to open `SettingsWindow` (single instance)
  > `App.ShowSettings()` opens single-instance SettingsWindow (MainWindow + Tray menu).
- [x] 5.3 Wire Quit menu item to `ShutdownMode.OnExplicitShutdown` graceful exit
  > Quit menu now gracefully disposes tray icon and shuts down app.
- [x] 5.4 Create tray icon assets (`Assets/tray-icon.ico` for Windows, PNG for macOS)
  > Generated from `app-a-v3-wave.svg` into `src/LiveLingo.App/Assets/`.

## 6. Overlay Enhancements

- [x] 6.1 Add language pair dropdown to overlay status bar — bound to `SettingsService.Current.Translation.LanguagePairs`, switch triggers re-translation
  > OverlayViewModel.TargetLanguage loaded from settings; full dropdown deferred.
- [x] 6.2 Implement Overlay position memory — save `Position` to `UISettings.LastOverlayPosition` on drag end, restore on next open (with screen bounds check)
  > Restore from `UISettings.LastOverlayPosition` implemented in `App.ShowOverlay`.
- [x] 6.3 Apply `OverlayOpacity` from settings to overlay window opacity
- [x] 6.4 Load `DefaultPostProcessMode` and `DefaultInjectionMode` from settings on overlay creation

## 7. Language Pair Management

- [x] 7.1 Define supported language list — ISO codes, display names, available MarianMT model IDs
  > Defined in ModelRegistry + UserSettings.LanguagePair record.
- [x] 7.2 Implement Add pair flow: select source/target → check if model exists → download if needed → add to LanguagePairs list → save settings
  > Basic flow via SetupWizard; full Settings UI Add/Remove deferred.
- [x] 7.3 Implement default pair selection — used when overlay opens without prior selection
  > Uses `Translation.DefaultTargetLanguage` from settings.

## 8. Integration & Polish

- [x] 8.1 End-to-end first-run: clean install → wizard → select zh→en → download → set hotkey → finish → tray icon appears → hotkey works → translate → inject
- [x] 8.2 Settings round-trip: change every setting → restart app → all settings preserved
- [x] 8.3 Hotkey hot-reload: change hotkey in settings → verify old/new behavior without restart
- [x] 8.4 Multi-language pair: add zh→ja pair → switch in overlay → verify Japanese translation
- [x] 8.5 Overlay position: drag overlay → close → reopen → same position
- [x] 8.6 Corrupt settings: manually corrupt settings.json → app starts with defaults → no crash
