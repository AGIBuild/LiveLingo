namespace TranslationPoc;

public sealed record Term(string Source, string Target);
public sealed record Request(string Source, string TargetLanguage, IReadOnlyList<Term> Terms, string Context = "");
public sealed record Translation(string Text, double FirstVisibleMs, double TotalMs, int OutputTokens = 0);

public interface ITranslator
{
    Task<Translation> TranslateAsync(Request request, CancellationToken ct);
}
