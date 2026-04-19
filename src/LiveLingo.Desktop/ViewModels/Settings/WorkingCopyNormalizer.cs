using LiveLingo.Core;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.ViewModels.Settings;

internal sealed class WorkingCopyNormalizer : IWorkingCopyNormalizer
{
    public void Normalize(TranslationSettings translation)
    {
        translation.ModelPolicy ??= new ModelPolicySettings();
        translation.CloudProvider ??= new CloudProviderSettings();

        if (string.IsNullOrWhiteSpace(translation.ModelPolicy.RoutingMode))
            translation.ModelPolicy.RoutingMode = nameof(TranslationRoutingMode.PreferLocal);

        if (string.IsNullOrWhiteSpace(translation.CloudProvider.PresetId))
            translation.CloudProvider.PresetId = CloudProviderPresetCatalog.InferFromBaseUrl(translation.CloudProvider.BaseUrl).Id;
        if (string.IsNullOrWhiteSpace(translation.CloudProvider.ProviderType))
            translation.CloudProvider.ProviderType = "OpenAICompatible";

        if (string.IsNullOrWhiteSpace(translation.CloudProvider.BaseUrl))
            translation.CloudProvider.BaseUrl = "https://api.openai.com/v1";
        else
            translation.CloudProvider.PresetId = CloudProviderPresetCatalog.InferFromBaseUrl(translation.CloudProvider.BaseUrl).Id;

        if (string.IsNullOrWhiteSpace(translation.ModelPolicy.PreferredLocalTranslationModelId))
            translation.ModelPolicy.PreferredLocalTranslationModelId = translation.ActiveTranslationModelId;
        else if (string.IsNullOrWhiteSpace(translation.ActiveTranslationModelId))
            translation.ActiveTranslationModelId = translation.ModelPolicy.PreferredLocalTranslationModelId;
    }

    public TranslationModelOption? ResolveInitialTranslationModel(
        TranslationSettings translation,
        IReadOnlyList<TranslationModelOption> availableTranslationModels)
    {
        if (!string.IsNullOrWhiteSpace(translation.ActiveTranslationModelId))
        {
            var byId = availableTranslationModels.FirstOrDefault(m =>
                string.Equals(m.Id, translation.ActiveTranslationModelId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;
        }

        return availableTranslationModels.FirstOrDefault(m =>
            m.Type == ModelType.Translation &&
            string.Equals(m.SourceLanguage, translation.DefaultSourceLanguage, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.TargetLanguage, translation.DefaultTargetLanguage, StringComparison.OrdinalIgnoreCase));
    }
}
