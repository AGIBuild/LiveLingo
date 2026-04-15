namespace LiveLingo.Core.Models;

public interface IModelSelector
{
    ModelProfile SelectTranslationProfile(string sourceLanguage, string targetLanguage);

    ModelProfile SelectPostProcessingProfile();

    ModelProfile? FindProfileById(string id);
}
