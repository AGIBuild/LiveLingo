namespace LiveLingo.Core.Translation;

public sealed record TranslatedText(string Text, double FirstVisibleMs, double TotalMs);

public interface ITranslator
{
    /// <exception cref="HttpRequestException">The inference endpoint could not serve the request.</exception>
    /// <exception cref="InvalidDataException">The model output was incomplete or lost a protected literal.</exception>
    Task<TranslatedText> TranslateAsync(TranslationRequest request, CancellationToken ct);
}
