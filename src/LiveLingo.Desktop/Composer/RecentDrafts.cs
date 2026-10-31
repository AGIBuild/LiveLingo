namespace LiveLingo.Desktop.Composer;

/// <summary>A closed composer's source text.</summary>
/// <param name="Written">The result was written or copied; an unwritten draft is restored on the next summon.</param>
public sealed record RecentDraft(DateTimeOffset At, string Text, bool Written);

/// <summary>
/// The last few source texts, newest first, so a closed composer can be continued. Memory only: nothing is persisted,
/// and it is gone when the app exits or the user clears it.
/// </summary>
public sealed class RecentDrafts(TimeProvider? time = null)
{
    public const int Capacity = 5;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly List<RecentDraft> _items = [];

    public IReadOnlyList<RecentDraft> Items => _items;

    public void Close(string sourceText, bool written)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return;

        _items.RemoveAll(item => item.Text == sourceText);
        _items.Insert(0, new RecentDraft(_time.GetUtcNow(), sourceText, written));
        if (_items.Count > Capacity)
            _items.RemoveRange(Capacity, _items.Count - Capacity);
    }

    /// <summary>Returns the latest draft when it was never written, and forgets it; otherwise "".</summary>
    public string TakeUnsent()
    {
        if (_items is not [{ Written: false } latest, ..])
            return "";

        _items.RemoveAt(0);
        return latest.Text;
    }

    public void Clear() => _items.Clear();
}
