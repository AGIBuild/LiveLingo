namespace LiveLingo.Core.Translation;

public interface ITranslationPipeline
{
    Task<TranslationResult> ProcessAsync(
        TranslationRequest request,
        CancellationToken ct = default);
}
