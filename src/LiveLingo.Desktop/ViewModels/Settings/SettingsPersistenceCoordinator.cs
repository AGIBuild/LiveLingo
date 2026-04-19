using LiveLingo.Core;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services;
using LiveLingo.Desktop.Services.Configuration;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.ViewModels.Settings;

internal sealed class SettingsPersistenceCoordinator : ISettingsPersistenceCoordinator
{
    private readonly ISettingsService _settings;
    private readonly IModelManager? _modelManager;
    private readonly CoreOptions? _coreOptions;
    private readonly ILlmModelLoadCoordinator? _llmCoordinator;
    private readonly ISecretStore _secretStore;
    private readonly ISettingsLocalizationHelper _localization;
    private readonly ILogger? _logger;

    public SettingsPersistenceCoordinator(
        ISettingsService settings,
        IModelManager? modelManager,
        CoreOptions? coreOptions,
        ILlmModelLoadCoordinator? llmCoordinator,
        ISecretStore secretStore,
        ISettingsLocalizationHelper localization,
        ILogger? logger)
    {
        _settings = settings;
        _modelManager = modelManager;
        _coreOptions = coreOptions;
        _llmCoordinator = llmCoordinator;
        _secretStore = secretStore;
        _localization = localization;
        _logger = logger;
    }

    public async Task<SettingsPersistenceOutcome> PersistAsync(
        SettingsPersistenceRequest request,
        CancellationToken ct)
    {
        var workingCopy = request.WorkingCopy;
        var advancedBefore = request.SettingsBeforeSave.Advanced.DeepClone();
        var translationBefore = request.SettingsBeforeSave.Translation.DeepClone();

        var migrationOutcome = await TryMigrateStoragePathAsync(
            workingCopy.Advanced.ModelStoragePath,
            request.OriginalModelStoragePath,
            ct).ConfigureAwait(false);
        if (!migrationOutcome.Succeeded)
            return new SettingsPersistenceOutcome(false, migrationOutcome.ErrorMessage, request.OriginalModelStoragePath);

        await SettingsSecretCoordinator.PersistSecretsAsync(workingCopy, _secretStore, ct).ConfigureAwait(false);
        _settings.Replace(workingCopy);

        if (_coreOptions is not null)
            CoreOptionsSync.ApplyFromSettings(workingCopy, _coreOptions, _modelManager);

        var translationModelChanged = !string.Equals(
            translationBefore.ActiveTranslationModelId,
            workingCopy.Translation.ActiveTranslationModelId,
            StringComparison.OrdinalIgnoreCase);

        if (_llmCoordinator is not null &&
            (CoreOptionsSync.AdvancedSettingsAffectLlmLoad(advancedBefore, workingCopy.Advanced) || translationModelChanged))
        {
            await _llmCoordinator.RequestRetryPrimaryTranslationModelAsync(ct).ConfigureAwait(false);
        }

        return new SettingsPersistenceOutcome(true, null, migrationOutcome.UpdatedOriginalPath);
    }

    private async Task<MigrationOutcome> TryMigrateStoragePathAsync(
        string? newPath,
        string? originalPath,
        CancellationToken ct)
    {
        var oldNormalized = CoreOptionsSync.NormalizePathForCompare(originalPath);
        var newNormalized = CoreOptionsSync.NormalizePathForCompare(newPath);
        if (_modelManager is null || string.IsNullOrEmpty(newNormalized) ||
            string.Equals(oldNormalized, newNormalized, StringComparison.OrdinalIgnoreCase))
        {
            return MigrationOutcome.NoOp(originalPath);
        }

        try
        {
            await _modelManager.MigrateStoragePathAsync(newPath!).ConfigureAwait(false);
            return MigrationOutcome.Migrated(newPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to migrate model storage path");
            return MigrationOutcome.Failed(
                _localization.Translate("settings.advanced.migrationFailed", "Migration failed: {0}", ex.Message));
        }
    }

    private readonly record struct MigrationOutcome(bool Succeeded, string? ErrorMessage, string? UpdatedOriginalPath)
    {
        public static MigrationOutcome NoOp(string? originalPath) => new(true, null, originalPath);
        public static MigrationOutcome Migrated(string? newPath) => new(true, null, newPath);
        public static MigrationOutcome Failed(string error) => new(false, error, null);
    }
}
