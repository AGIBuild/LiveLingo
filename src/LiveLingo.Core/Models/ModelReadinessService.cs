namespace LiveLingo.Core.Models;

public sealed class ModelReadinessService(IModelManager modelManager) : IModelReadinessService
{
    public bool IsInstalled(string modelId) =>
        modelManager.ListInstalled().Any(m =>
            string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));

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
        if (IsInstalled(ModelRegistry.Qwen25_15B.Id))
            return;

        throw new ModelNotReadyException(
            ModelType.PostProcessing,
            ModelRegistry.Qwen25_15B.Id,
            $"Post-processing model '{ModelRegistry.Qwen25_15B.DisplayName}' is not downloaded.",
            "Open Settings -> Models and download Qwen model.");
    }
}
