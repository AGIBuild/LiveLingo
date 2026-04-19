using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.ViewModels.Settings;

/// <summary>
/// Stateless transformations applied to a freshly-loaded <see cref="SettingsModel"/>
/// so the rest of the Settings ViewModel can rely on canonical fields:
/// non-null nested groups, defaulted routing/preset values, and a translation
/// model selection that matches an actually-available option when possible.
/// </summary>
internal interface IWorkingCopyNormalizer
{
    /// <summary>
    /// Fills in missing nested groups, defaults, and infers preset id from base URL.
    /// Mutates <paramref name="translation"/> in place.
    /// </summary>
    void Normalize(TranslationSettings translation);

    /// <summary>
    /// Picks the most appropriate translation model option from the available list,
    /// preferring an exact id match, then a language-pair match, otherwise <c>null</c>.
    /// </summary>
    TranslationModelOption? ResolveInitialTranslationModel(
        TranslationSettings translation,
        IReadOnlyList<TranslationModelOption> availableTranslationModels);
}
