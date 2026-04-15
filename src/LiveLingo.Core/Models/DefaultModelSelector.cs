using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Models;

public sealed class DefaultModelSelector : IModelSelector
{
    private readonly IModelCatalog _catalog;
    private readonly CoreOptions _options;
    private readonly ICloudProviderRuntimeState _cloudRuntimeState;

    public DefaultModelSelector(
        IModelCatalog catalog,
        IOptions<CoreOptions> options,
        ICloudProviderRuntimeState? cloudRuntimeState = null)
    {
        _catalog = catalog;
        _options = options.Value;
        _cloudRuntimeState = cloudRuntimeState ?? new NullCloudProviderRuntimeState();
    }

    public ModelProfile SelectTranslationProfile(string sourceLanguage, string targetLanguage) =>
        ModelSelectionPolicy.SelectTranslationProfile(
            _catalog,
            _options.ActiveTranslationModelId,
            sourceLanguage,
            targetLanguage,
            _options.TranslationRoutingMode,
            _options.RouteUnsupportedLanguagePairsToCloud,
            CreateCloudPreferences(),
            _cloudRuntimeState.GetRoutingState(CreateCloudPreferences()));

    public ModelProfile SelectPostProcessingProfile() =>
        ModelSelectionPolicy.SelectPostProcessingProfile(
            _catalog,
            _options.ActiveTranslationModelId,
            _options.DefaultTargetLanguage,
            _options.TranslationRoutingMode,
            _options.RoutePostProcessingToCloud,
            CreateCloudPreferences(),
            _cloudRuntimeState.GetRoutingState(CreateCloudPreferences()));

    public ModelProfile? FindProfileById(string id) =>
        ModelSelectionPolicy.FindProfileById(_catalog, id, CreateCloudPreferences());

    private CloudModelPreferences CreateCloudPreferences() =>
        new(
            _options.CloudProviderEnabled,
            _options.CloudProviderBaseUrl,
            _options.CloudProviderApiKey,
            _options.CloudTranslationModelId,
            _options.CloudPostProcessingModelId);
}
