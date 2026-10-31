using LiveLingo.Core.Models;

namespace LiveLingo.Desktop.Composer;

/// <summary>Which models each output needs, and which ones listening needs on top.</summary>
public sealed record ComposerModelPlan(
    IReadOnlyList<ModelDescriptor> Clean,
    IReadOnlyList<ModelDescriptor> Translate,
    IReadOnlyList<ModelDescriptor> Voice)
{
    public IReadOnlyList<ModelDescriptor> For(ComposerOutput output) => output switch
    {
        ComposerOutput.Clean => Clean,
        ComposerOutput.Translate => Translate,
        ComposerOutput.Original => [],
        _ => throw new ArgumentOutOfRangeException(nameof(output))
    };

    public static ComposerModelPlan Create(TranslatorTier tier) => new(
        [ModelCatalog.LlamaRuntime, ModelCatalog.Cleaner],
        [ModelCatalog.LlamaRuntime, ModelCatalog.Translator(tier)],
        ModelCatalog.Speech);
}
