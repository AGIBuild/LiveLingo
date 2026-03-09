namespace LiveLingo.Core.Models;

public interface IModelReadinessService
{
    bool IsInstalled(string modelId);
    void EnsureTranslationModelReady(string sourceLanguage, string targetLanguage);
    void EnsurePostProcessingModelReady();
}
