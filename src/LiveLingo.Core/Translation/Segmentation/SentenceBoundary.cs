namespace LiveLingo.Core.Translation.Segmentation;

/// <summary>
/// Sentence-end mark detection shared by every segmentation collaborator.
/// CJK marks (。！？…) split immediately because CJK text never carries a
/// trailing space; Latin marks (.!?) only count when followed by a true
/// sentence boundary so abbreviations ("U.S.A.") and decimals ("3.14") do
/// not confuse the splitter.
/// </summary>
internal static class SentenceBoundary
{
    public static readonly char[] CjkEndChars = ['。', '！', '？', '…'];
    public static readonly char[] LatinEndChars = ['.', '!', '?'];

    public static bool IsCjkEnd(char c) => Array.IndexOf(CjkEndChars, c) >= 0;
    public static bool IsLatinEnd(char c) => Array.IndexOf(LatinEndChars, c) >= 0;
    public static bool IsAnyEnd(char c) => IsCjkEnd(c) || IsLatinEnd(c);

    /// <summary>
    /// True when the Latin sentence-end mark at <paramref name="position"/> is
    /// followed by whitespace/EOF AND the next non-whitespace character is not
    /// a lowercase letter (which would suggest an abbreviation like
    /// "Mr. smith" or "U.S.A. is big").
    /// </summary>
    public static bool IsLatinBoundary(string text, int position)
    {
        var next = position + 1;
        if (next >= text.Length) return true;

        var c = text[next];
        if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
            return false;

        while (next < text.Length &&
               (text[next] == ' ' || text[next] == '\t' ||
                text[next] == '\n' || text[next] == '\r'))
            next++;

        if (next >= text.Length) return true;

        return !char.IsLower(text[next]);
    }

    /// <summary>
    /// True when the trimmed text ends with a sentence-terminating mark.
    /// Used by the unit planner to decide whether two line-connected
    /// fragments belong in the same translation unit.
    /// </summary>
    public static bool EndsWithMark(ReadOnlySpan<char> text)
    {
        var span = text.TrimEnd();
        if (span.IsEmpty) return false;
        var last = span[^1];
        return IsCjkEnd(last) || IsLatinEnd(last);
    }

    /// <summary>
    /// Counts sentence-ending punctuation that would drive a segmentation
    /// split. Latin marks only count when the boundary heuristic agrees, so
    /// abbreviations and decimals are never mistaken for sentence ends.
    /// </summary>
    public static int CountEndings(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (IsCjkEnd(c))
            {
                count++;
                while (i + 1 < text.Length && IsAnyEnd(text[i + 1])) i++;
                continue;
            }

            if (IsLatinEnd(c) && IsLatinBoundary(text, i))
            {
                count++;
                while (i + 1 < text.Length && IsAnyEnd(text[i + 1])) i++;
            }
        }
        return count;
    }
}
