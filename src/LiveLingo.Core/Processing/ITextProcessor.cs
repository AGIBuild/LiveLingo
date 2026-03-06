namespace LiveLingo.Core.Processing;

public interface ITextProcessor : IDisposable
{
    string Name { get; }

    Task<string> ProcessAsync(
        string text,
        string language,
        CancellationToken ct = default);
}
