using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.ViewModels.Settings;

/// <summary>
/// Bidirectional sync between the active translation model id and the
/// (source language, target language) pair. Owns the re-entrancy guard so
/// echoes of <see cref="System.ComponentModel.PropertyChanged"/> events
/// don't loop.
///
/// All write operations target a <see cref="TranslationSettings"/> instance;
/// reads consult the supplied list of available <see cref="TranslationModelOption"/>.
/// </summary>
internal interface ITranslationLanguagePairSyncer
{
    /// <summary>True while a sync operation is mutating the translation settings.</summary>
    bool IsSyncing { get; }

    /// <summary>
    /// Active translation model changed → write its language pair into <paramref name="translation"/>.
    /// No-op when the model is missing or not a translation model.
    /// </summary>
    void SyncLanguagePairFromModel(
        string? modelId,
        IReadOnlyList<TranslationModelOption> available,
        TranslationSettings translation);

    /// <summary>
    /// Source/target language changed → pick the matching translation model id
    /// (or null when no model matches) and write it into <paramref name="translation"/>.
    /// </summary>
    void SyncModelFromLanguagePair(
        string? sourceLanguage,
        string? targetLanguage,
        IReadOnlyList<TranslationModelOption> available,
        TranslationSettings translation);

    /// <summary>
    /// Re-pick the active translation model id after the available list was rebuilt,
    /// preferring the previously-selected id, then a language-pair match.
    /// </summary>
    void RestoreModelSelectionAfterRefresh(
        string? previousModelId,
        string? sourceLanguage,
        string? targetLanguage,
        IReadOnlyList<TranslationModelOption> available,
        TranslationSettings translation);
}
