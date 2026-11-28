using LiveLingo.Core.Inference;

namespace LiveLingo.Core.Models;

/// <summary>The chosen user-selectable model IDs across the three customizable pipeline stages.</summary>
public sealed record ModelSelection(string SpeechModelId, string CleanerModelId, string TranslatorModelId)
{
    /// <summary>32 GB product validation baseline selection.</summary>
    public static ModelSelection Baseline { get; } = new(
        ModelCatalog.SpeechQuality.Id,
        ModelCatalog.CleanerQuality.Id,
        ModelCatalog.TranslatorIndexQuality.Id);

    /// <summary>16 GB lightweight fallback selection.</summary>
    public static ModelSelection Fallback16Gb { get; } = new(
        ModelCatalog.SpeechSecondPass.Id,
        ModelCatalog.Cleaner.Id,
        ModelCatalog.TranslatorFast.Id);

    public ModelDescriptor Speech =>
        ModelCatalog.FindOption(ModelStage.Speech, SpeechModelId) ?? ModelCatalog.SpeechQuality;

    public ModelDescriptor Cleaner =>
        ModelCatalog.FindOption(ModelStage.Cleaner, CleanerModelId) ?? ModelCatalog.CleanerQuality;

    public ModelDescriptor Translator =>
        ModelCatalog.FindOption(ModelStage.Translator, TranslatorModelId) ?? ModelCatalog.TranslatorIndexQuality;

    /// <summary>True when Cleaner and Translator execute the exact same physical local GGUF artifact.</summary>
    public bool SharesLlmArtifact =>
        string.Equals(Cleaner.ArtifactId, Translator.ArtifactId, StringComparison.Ordinal);

    public ModelDescriptor DescriptorFor(ModelStage stage) => stage switch
    {
        ModelStage.Speech => Speech,
        ModelStage.Cleaner => Cleaner,
        ModelStage.Translator => Translator,
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };

    public string ModelIdFor(ModelStage stage) => DescriptorFor(stage).Id;

    public ModelSelection With(ModelStage stage, string modelId) => stage switch
    {
        ModelStage.Speech => this with { SpeechModelId = modelId },
        ModelStage.Cleaner => this with { CleanerModelId = modelId },
        ModelStage.Translator => this with { TranslatorModelId = modelId },
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };
}
