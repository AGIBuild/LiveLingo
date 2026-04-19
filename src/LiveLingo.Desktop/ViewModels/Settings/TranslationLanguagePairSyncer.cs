using LiveLingo.Core.Models;
using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.ViewModels.Settings;

internal sealed class TranslationLanguagePairSyncer : ITranslationLanguagePairSyncer
{
    public bool IsSyncing { get; private set; }

    public void SyncLanguagePairFromModel(
        string? modelId,
        IReadOnlyList<TranslationModelOption> available,
        TranslationSettings translation)
    {
        if (IsSyncing || string.IsNullOrWhiteSpace(modelId))
            return;

        var selected = FindById(available, modelId);
        if (selected is null ||
            selected.Type != ModelType.Translation ||
            string.IsNullOrWhiteSpace(selected.SourceLanguage) ||
            string.IsNullOrWhiteSpace(selected.TargetLanguage))
        {
            return;
        }

        RunSyncing(() =>
        {
            translation.DefaultSourceLanguage = selected.SourceLanguage!;
            translation.DefaultTargetLanguage = selected.TargetLanguage!;
        });
    }

    public void SyncModelFromLanguagePair(
        string? sourceLanguage,
        string? targetLanguage,
        IReadOnlyList<TranslationModelOption> available,
        TranslationSettings translation)
    {
        if (IsSyncing) return;

        var matched = FindByLanguagePair(available, sourceLanguage, targetLanguage);
        RunSyncing(() => translation.ActiveTranslationModelId = matched?.Id);
    }

    public void RestoreModelSelectionAfterRefresh(
        string? previousModelId,
        string? sourceLanguage,
        string? targetLanguage,
        IReadOnlyList<TranslationModelOption> available,
        TranslationSettings translation)
    {
        var restored = !string.IsNullOrWhiteSpace(previousModelId)
            ? FindById(available, previousModelId)
            : null;

        restored ??= FindByLanguagePair(available, sourceLanguage, targetLanguage);

        RunSyncing(() => translation.ActiveTranslationModelId = restored?.Id);
    }

    private void RunSyncing(Action action)
    {
        IsSyncing = true;
        try
        {
            action();
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private static TranslationModelOption? FindById(
        IReadOnlyList<TranslationModelOption> available, string id) =>
        available.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    private static TranslationModelOption? FindByLanguagePair(
        IReadOnlyList<TranslationModelOption> available, string? source, string? target) =>
        available.FirstOrDefault(m =>
            m.Type == ModelType.Translation &&
            string.Equals(m.SourceLanguage, source, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.TargetLanguage, target, StringComparison.OrdinalIgnoreCase));
}
