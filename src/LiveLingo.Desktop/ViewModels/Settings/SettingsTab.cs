namespace LiveLingo.Desktop.ViewModels.Settings;

/// <summary>
/// Canonical index of every tab exposed by <c>SettingsWindow.axaml</c>. Centralising the indices
/// here keeps view-models, message senders and tests in lock-step whenever a new tab is inserted —
/// no more silent breakage from numeric drift.
/// </summary>
public enum SettingsTab
{
    General = 0,
    Translation = 1,
    Speech = 2,
    Models = 3,
    Advanced = 4,
    AI = 5,
    Diagnostics = 6,
}
