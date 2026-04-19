using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.ViewModels.Settings;

/// <summary>
/// Encapsulates the "Save" pipeline for the Settings ViewModel:
/// model storage path migration → secret persistence →
/// <see cref="ISettingsService.Replace"/> → CoreOptions sync →
/// LLM model retry, in that order.
///
/// Returns a structured outcome so the ViewModel can decide whether to surface
/// a migration error, update its <c>_originalModelStoragePath</c> bookkeeping
/// field, and broadcast UI messages. Pure side-effect orchestration; the
/// coordinator does not touch any UI state on its own.
/// </summary>
internal interface ISettingsPersistenceCoordinator
{
    Task<SettingsPersistenceOutcome> PersistAsync(
        SettingsPersistenceRequest request,
        CancellationToken ct);
}

internal sealed record SettingsPersistenceRequest(
    SettingsModel WorkingCopy,
    string? OriginalModelStoragePath,
    SettingsModel SettingsBeforeSave);

internal sealed record SettingsPersistenceOutcome(
    bool MigrationSucceeded,
    string? MigrationErrorMessage,
    string? UpdatedOriginalModelStoragePath);
