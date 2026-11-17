using LiveLingo.Core.Inference;

namespace LiveLingo.Core.Models;

/// <summary>The chosen user-selectable model IDs across the three customizable pipeline stages.</summary>
public sealed record ModelSelection(string SpeechModelId, string CleanerModelId, string TranslatorModelId)
{
    /// <summary>16 GB product validation baseline selection.</summary>
    public static ModelSelection Baseline { get; } = new(
        ModelCatalog.SpeechSecondPass.Id,
        ModelCatalog.Cleaner.Id,
        ModelCatalog.TranslatorFast.Id);

    public ModelDescriptor Speech =>
        ModelCatalog.FindOption(ModelStage.Speech, SpeechModelId) ?? ModelCatalog.SpeechSecondPass;

    public ModelDescriptor Cleaner =>
        ModelCatalog.FindOption(ModelStage.Cleaner, CleanerModelId) ?? ModelCatalog.Cleaner;

    public ModelDescriptor Translator =>
        ModelCatalog.FindOption(ModelStage.Translator, TranslatorModelId) ?? ModelCatalog.TranslatorFast;

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
