namespace LiveLingo.Core.Models;

public sealed class ModelReadinessService(IModelManager modelManager, IModelSelector selector) : IModelReadinessService
{
    public bool IsInstalled(string modelId)
    {
        if (!modelManager.ListInstalled().Any(m =>
                string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase)))
            return false;

        var descriptor = ModelRegistry.AllModels.FirstOrDefault(m =>
            string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
        return descriptor is null || modelManager.HasAllExpectedLocalAssets(descriptor);
    }

    public void EnsureTranslationModelReady(string sourceLanguage, string targetLanguage)
    {
        var profile = selector.SelectTranslationProfile(sourceLanguage, targetLanguage);

        if (profile.RuntimeKind == ModelRuntimeKind.RemoteHttp)
            return;

        if (IsInstalled(profile.Id))
            return;

        throw new ModelNotReadyException(
            ModelType.Translation,
            profile.Id,
            $"Translation model '{profile.DisplayName}' is not downloaded.",
            "Open Settings -> Models and download the required translation model.");
    }

    public void EnsurePostProcessingModelReady()
    {
        var profile = selector.SelectPostProcessingProfile();
        if (profile.RuntimeKind == ModelRuntimeKind.RemoteHttp)
            return;

        if (IsInstalled(profile.Id))
            return;

        if (!string.Equals(profile.Id, ModelRegistry.Qwen25_15B.Id, StringComparison.OrdinalIgnoreCase) &&
            IsInstalled(ModelRegistry.Qwen25_15B.Id))
        {
            return;
        }

        throw new ModelNotReadyException(
            ModelType.PostProcessing,
            profile.Id,
            $"Model '{profile.DisplayName}' is not downloaded for post-processing.",
            "Open Settings → Models and download the primary translation model (or Qwen 2.5 1.5B as a lighter option).");
    }
}
