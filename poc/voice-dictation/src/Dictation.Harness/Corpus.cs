using System.Text;

namespace Dictation.Harness;

/// <summary>Timeline written next to each corpus wav by build-corpus.sh.</summary>
public sealed record Timeline(
    string Id,
    string Category,
    string Voice,
    int DurationMs,
    List<TimelinePart> Parts,
    List<string> Protect,
    List<string> MustKeep,
    List<string> Forbid)
{
    public string SpokenText => string.Concat(Parts.Select(p => p.Text));
    public int LastSpeechEndMs => Parts[^1].EndMs;
}

public sealed record TimelinePart(string Text, int StartMs, int EndMs);

public static class Strings
{
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
