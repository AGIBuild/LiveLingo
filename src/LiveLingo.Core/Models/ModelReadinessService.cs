namespace LiveLingo.Core.Models;

public sealed class ModelReadinessService(IModelManager modelManager) : IModelReadinessService
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
        var descriptor = ModelRegistry.FindTranslationModel(sourceLanguage, targetLanguage);
        if (descriptor is null)
            throw new NotSupportedException($"No translation model available for {sourceLanguage}->{targetLanguage}.");

        if (IsInstalled(descriptor.Id))
            return;

        throw new ModelNotReadyException(
            ModelType.Translation,
            descriptor.Id,
            $"Translation model '{descriptor.DisplayName}' is not downloaded.",
            "Open Settings -> Models and download the required translation model.");
    }

    public void EnsurePostProcessingModelReady()
    {
        if (IsInstalled(ModelRegistry.Qwen35_9B.Id) || IsInstalled(ModelRegistry.Qwen25_15B.Id))
            return;

        throw new ModelNotReadyException(
            ModelType.PostProcessing,
            ModelRegistry.Qwen25_15B.Id,
            "No Qwen GGUF is downloaded for post-processing.",
            "Open Settings → Models and download the primary translation model (or Qwen 2.5 1.5B as a lighter option).");
    }
}
