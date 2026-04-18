namespace LiveLingo.Core.Models;

public interface IModelSelector
{
    /// <summary>
    /// Selects the best translation profile for the given language pair and optional
    /// routing context. Prefer <see cref="BuildTranslationRoutePlan"/> for production
    /// translation invocations so runtime fallbacks are available; this single-profile
    /// API is retained for callers that genuinely need one answer (e.g. UI badges).
    /// </summary>
    ModelProfile SelectTranslationProfile(
        string sourceLanguage,
        string targetLanguage,
        TranslationRoutingContext? context = null);

    /// <summary>
    /// Builds an ordered list of translation candidates consistent with the user's
    /// routing mode and configured providers. The first candidate is the primary
    /// target; subsequent candidates are runtime fallbacks tried by
    /// <see cref="ITranslationInvoker"/> when the primary fails, times out, or
    /// produces output that fails the post-run quality guard.
    /// </summary>
    TranslationRoutePlan BuildTranslationRoutePlan(
        string sourceLanguage,
        string targetLanguage,
        TranslationRoutingContext? context = null);

    ModelProfile SelectPostProcessingProfile();

    ModelProfile? FindProfileById(string id);
}
