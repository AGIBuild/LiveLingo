using LiveLingo.Core;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services;
using LiveLingo.Desktop.Services.Cloud;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Localization;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.ViewModels.Settings;

/// <summary>
/// Composition root for the nine collaborators the Settings ViewModel delegates to.
/// Lives next to the ViewModel so the public constructor surface (parameters
/// supplied from <c>App.axaml.cs</c>) stays identical: the assembler turns those
/// inbound services into a coherent dependency graph, and the ViewModel itself
/// only holds interface references.
///
/// Each collaborator can still be substituted independently in unit tests by
/// constructing a <see cref="SettingsViewModelDependencies"/> with mocks.
/// </summary>
internal sealed record SettingsViewModelDependencies(
    ISettingsLocalizationHelper Localization,
    IWorkingCopyNormalizer Normalizer,
    ITranslationModelInventory ModelInventory,
    ICloudProviderPresetCoordinator PresetCoordinator,
    IOllamaProviderProbeOrchestrator OllamaProbe,
    ICloudProviderProbeOrchestrator CloudProbe,
    ITranslationLanguagePairSyncer LanguagePairSyncer,
    ISettingsDirtyGuard DirtyGuard,
    ISettingsPersistenceCoordinator Persistence)
{
    public static SettingsViewModelDependencies Create(
        ISettingsService settings,
        IModelManager? modelManager,
        ICloudProviderRuntimeState cloudProviderRuntimeState,
        IOllamaProbeService? ollamaProbeService,
        CoreOptions? coreOptions,
        ILlmModelLoadCoordinator? llmCoordinator,
        ISecretStore secretStore,
        ILocalizationService? localization,
        ILogger? logger)
    {
        var locHelper = new SettingsLocalizationHelper(localization);
        var normalizer = new WorkingCopyNormalizer();
        var modelInventory = new TranslationModelInventory(modelManager, locHelper);
        var presetCoordinator = new CloudProviderPresetCoordinator();
        var ollamaProbe = new OllamaProviderProbeOrchestrator(ollamaProbeService, locHelper);
        var cloudProbe = new CloudProviderProbeOrchestrator(cloudProviderRuntimeState, localization);
        var languagePairSyncer = new TranslationLanguagePairSyncer();
        var dirtyGuard = new SettingsDirtyGuard();
        var persistence = new SettingsPersistenceCoordinator(
            settings, modelManager, coreOptions, llmCoordinator, secretStore, locHelper, logger);

        return new SettingsViewModelDependencies(
            locHelper, normalizer, modelInventory, presetCoordinator, ollamaProbe,
            cloudProbe, languagePairSyncer, dirtyGuard, persistence);
    }
}
