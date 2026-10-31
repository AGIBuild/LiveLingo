namespace LiveLingo.Desktop.Composer;

/// <summary>
/// Keeps the source text of a composer that was closed before its result was written, so the next summon can
/// continue it. Memory only: nothing is persisted, and it is gone when the app exits.
/// </summary>
public sealed class UnsentDraft
{
    private string _text = "";

    public void Close(string sourceText, bool written) =>
        _text = written || string.IsNullOrWhiteSpace(sourceText) ? "" : sourceText;

    /// <summary>Returns the kept text and forgets it.</summary>
    public string Take()
    {
        var text = _text;
        _text = "";
        return text;
    }
}
