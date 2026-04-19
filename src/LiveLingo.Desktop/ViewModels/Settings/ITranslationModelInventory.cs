using LiveLingo.Core.Models;

namespace LiveLingo.Desktop.ViewModels.Settings;

/// <summary>
/// Computes the user-facing list of installed translation models from
/// <see cref="IModelManager"/> and parses translation model ids into
/// <c>source→target</c> language pair labels.
/// </summary>
internal interface ITranslationModelInventory
{
    /// <summary>
    /// Snapshots the currently installed translation models, sorted by display name.
    /// </summary>
    IReadOnlyList<TranslationModelOption> Snapshot();

    /// <summary>
    /// Builds the human-readable pair label (e.g. <c>"en→zh"</c>) for a translation
    /// model option, or a generic localized label for non-translation model types.
    /// </summary>
    string BuildPairLabel(ModelType type, string? source, string? target);

    /// <summary>
    /// Parses a Marian-OPUS-style model id (<c>"opus-mt-{src}-{tgt}"</c>) into its
    /// language pair, returning <c>true</c> when the id matches the expected shape.
    /// </summary>
    bool TryParseLanguagePairFromModelId(string modelId, out string sourceLanguage, out string targetLanguage);
}
