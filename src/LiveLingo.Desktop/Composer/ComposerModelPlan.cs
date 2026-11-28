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
    /// <param name="cleanOnLocalHost">Cleanup runs on a shared local model host (Bionic / LM Studio / Ollama), so no built-in GGUF or llama-server is needed.</param>
    /// <param name="translateOnLocalHost">Translation runs on a shared local model host, so no built-in GGUF or llama-server is needed.</param>
    public static ComposerModelPlan Create(
        ModelSelection selection,
        bool cleanInCloud,
        bool translateInCloud,
        bool speechInCloud = false,
        bool speechNativeClean = false,
        bool cleanOnLocalHost = false,
        bool translateOnLocalHost = false,
        bool speechOnLocalHost = false)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return new ComposerModelPlan(
            cleanInCloud || cleanOnLocalHost ? [] : [ModelCatalog.LlamaRuntime, selection.Cleaner.ResolveArtifact()],
            translateInCloud || translateOnLocalHost ? [] : [ModelCatalog.LlamaRuntime, selection.Translator.ResolveArtifact()],
            speechInCloud || speechOnLocalHost ? [] : ModelCatalog.SpeechFor(selection.Speech),
            speechInCloud || speechOnLocalHost ? [] : ModelCatalog.TranscriptionFor(selection.Speech),
            SpeechNativeClean: speechInCloud && speechNativeClean && !cleanInCloud && !cleanOnLocalHost);
    }

    public static ComposerModelPlan Create(TranslatorTier tier, bool cleanInCloud, bool translateInCloud, bool speechInCloud = false) =>
        Create(ModelSelection.Fallback16Gb with { TranslatorModelId = ModelCatalog.Translator(tier).Id }, cleanInCloud, translateInCloud, speechInCloud);
}
