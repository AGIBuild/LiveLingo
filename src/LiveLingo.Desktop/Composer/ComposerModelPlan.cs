using LiveLingo.Core.Models;

namespace LiveLingo.Desktop.Composer;

/// <summary>Which models each output needs, and which ones listening and importing a recording need on top.</summary>
public sealed record ComposerModelPlan(
    IReadOnlyList<ModelDescriptor> Clean,
    IReadOnlyList<ModelDescriptor> Translate,
    IReadOnlyList<ModelDescriptor> Voice,
    IReadOnlyList<ModelDescriptor> Transcription)
{
    public IReadOnlyList<ModelDescriptor> For(ComposerOutput output) => output switch
    {
        ComposerOutput.Clean => Clean,
        ComposerOutput.Translate => Translate,
        ComposerOutput.Original => [],
        _ => throw new ArgumentOutOfRangeException(nameof(output))
    };

    /// <param name="cleanInCloud">Cleanup runs on the user's cloud endpoint, so no local cleaner is needed.</param>
    /// <param name="translateInCloud">Translation runs on the user's cloud endpoint, so no local translator is needed.</param>
    public static ComposerModelPlan Create(TranslatorTier tier, bool cleanInCloud, bool translateInCloud) => new(
        cleanInCloud ? [] : [ModelCatalog.LlamaRuntime, ModelCatalog.Cleaner],
        translateInCloud ? [] : [ModelCatalog.LlamaRuntime, ModelCatalog.Translator(tier)],
        ModelCatalog.Speech,
        ModelCatalog.Transcription);
}
