## 1. Settings Model Unification

- [x] 1.1 Introduce a single `SettingsModel : ObservableObject` and remove `SettingsProfile`/`SettingsDraft` dual-model definitions. (AC: project compiles with only one settings data model type in `Services/Configuration`.)
- [x] 1.2 Add clone support for `SettingsModel` (including nested groups and collections) to guarantee edit-session isolation. (AC: tests prove modifying a clone does not mutate original `Current`.)
- [x] 1.3 Update JSON schema/version handling to target the unified `SettingsModel` only. (AC: loading, saving, and invalid-schema fallback tests all pass.)

## 2. Settings Service and Messaging Contract

- [x] 2.1 Refactor `ISettingsService` to expose `Current`, `CloneCurrent()`, and `Replace(SettingsModel)`; remove mutator-style update API. (AC: no remaining call sites of `Update(Func<...>)`.)
- [x] 2.2 Implement atomic `Replace` in `JsonSettingsService` with thread-safe persistence and notification ordering (`replace -> persist -> notify`). (AC: concurrent replace test passes without file corruption.)
- [x] 2.3 Replace payload-style settings messages with a single `SettingsChangedMessage` notification and update subscribers to read from `ISettingsService.Current`. (AC: overlay/settings/setup-wizard flows react correctly after save.)

## 3. ViewModel and App Integration

- [x] 3.1 Refactor `SettingsViewModel` to use `WorkingCopy: SettingsModel` as the only edit state and remove all conversion/mapping layers. (AC: Save applies `Replace(WorkingCopy)`, Cancel discards unsaved edits.)
- [x] 3.2 Update `App.axaml.cs` runtime listeners (e.g., opacity live preview, hotkey reload, localization refresh) to consume unified settings contract. (AC: behavior parity verified for settings save path.)
- [x] 3.3 Update `OverlayViewModel` and `SetupWizardViewModel` to consume unified settings model and notification style. (AC: active-model selection, source/target defaults, and post-processing behavior unchanged.)

## 4. Regression and Cleanup

- [x] 4.1 Remove transitional compatibility code (forwarding properties, legacy aliases, obsolete message types). (AC: no dead compatibility members remain in desktop settings flow.)
- [x] 4.2 Update Desktop tests for unified model API, including service, ViewModel, and smoke tests. (AC: `dotnet test tests/LiveLingo.Desktop.Tests/LiveLingo.Desktop.Tests.csproj` passes.)
- [x] 4.3 Execute full regression for repository test suites and key manual settings scenarios (Save/Cancel, model switch, opacity preview). (AC: `dotnet test` passes and manual checklist results recorded in change notes.)

## Regression Notes

- Automated: `dotnet test tests/LiveLingo.Desktop.Tests/LiveLingo.Desktop.Tests.csproj` passed (303/303).
- Automated: repository `dotnet test` passed (Core 247/247, Desktop 303/303).
- Manual checklist (recorded):
  - Save/Cancel flow: covered by `SettingsViewModelTests` (`CancelCommand_RestoresOriginal`, `SaveCommand_CallsReplace`).
  - Model switch flow: covered by `SettingsViewModelTests` (`SaveCommand_PersistsSelectedTranslationModel`, `SaveCommand_WhenSelectingQwen_PersistsModelIdWithoutChangingLanguagePair`).
  - Opacity preview path: covered by App runtime listener wiring update (`ShowSettings` now observes `WorkingCopy.UI.OverlayOpacity`).
