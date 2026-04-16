namespace LiveLingo.Core.Models;

public interface IModelSelector
{
    /// <summary>
    /// Selects the best translation profile for the given language pair and optional
    /// routing context. When <paramref name="context"/> is provided, text-length and
    /// quality-mode hints may escalate the effective routing mode automatically.
    /// </summary>
    ModelProfile SelectTranslationProfile(
        string sourceLanguage,
        string targetLanguage,
        TranslationRoutingContext? context = null);

    ModelProfile SelectPostProcessingProfile();

    ModelProfile? FindProfileById(string id);
}
