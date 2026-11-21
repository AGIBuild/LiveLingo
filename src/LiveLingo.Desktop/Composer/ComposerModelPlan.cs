using LiveLingo.Core.Models;

namespace LiveLingo.Desktop.Composer;

/// <summary>Which models each output needs, and which ones listening and importing a recording need on top.</summary>
public sealed record ComposerModelPlan(
    IReadOnlyList<ModelDescriptor> Clean,
    IReadOnlyList<ModelDescriptor> Translate,
    IReadOnlyList<ModelDescriptor> Voice,
    IReadOnlyList<ModelDescriptor> Transcription,
    bool SpeechNativeClean = false)
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
    /// <param name="speechInCloud">Speech recognition runs on the user's cloud endpoint, so no local ONNX speech models are needed.</param>
    /// <param name="speechNativeClean">The active cloud speech model natively polishes dictated speech without needing a separate cleaner LLM.</param>
    public static ComposerModelPlan Create(
        ModelSelection selection,
        bool cleanInCloud,
        bool translateInCloud,
        bool speechInCloud = false,
        bool speechNativeClean = false)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return new ComposerModelPlan(
            cleanInCloud ? [] : [ModelCatalog.LlamaRuntime, selection.Cleaner],
            translateInCloud ? [] : [ModelCatalog.LlamaRuntime, selection.Translator],
            speechInCloud ? [] : ModelCatalog.SpeechFor(selection.Speech),
            speechInCloud ? [] : ModelCatalog.TranscriptionFor(selection.Speech),
            SpeechNativeClean: speechInCloud && speechNativeClean && !cleanInCloud);
    }

    public static ComposerModelPlan Create(TranslatorTier tier, bool cleanInCloud, bool translateInCloud, bool speechInCloud = false) =>
        Create(ModelSelection.Baseline with { TranslatorModelId = ModelCatalog.Translator(tier).Id }, cleanInCloud, translateInCloud, speechInCloud);
}
