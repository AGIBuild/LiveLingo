namespace LiveLingo.Core.Translation.Segmentation;

/// <summary>
/// Splits a single trimmed line into its constituent sentences using
/// <see cref="SentenceBoundary"/>. Adjacent sentence-end marks ("?!", "...")
/// stay attached to the preceding sentence; intra-sentence whitespace
/// (' ', '\t') is consumed between sentences.
/// </summary>
internal static class SentenceSplitter
{
    public static IReadOnlyList<string> Split(string text)
    {
        var sentences = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var isCjkEnd = SentenceBoundary.IsCjkEnd(c);
            var isLatinEnd = SentenceBoundary.IsLatinEnd(c);
            if (!isCjkEnd && !isLatinEnd) continue;

            if (isLatinEnd && !SentenceBoundary.IsLatinBoundary(text, i))
                continue;

            // Absorb consecutive sentence-end punctuation (e.g. "?!", "...")
            var endOfPunct = i + 1;
            while (endOfPunct < text.Length && SentenceBoundary.IsAnyEnd(text[endOfPunct]))
                endOfPunct++;

            // Skip inter-sentence whitespace. Newlines never reach this code
            // path because the line pre-pass has already peeled them off.
            var afterWs = endOfPunct;
            while (afterWs < text.Length &&
                   (text[afterWs] == ' ' || text[afterWs] == '\t'))
                afterWs++;

            var body = text[start..endOfPunct].Trim();
            if (body.Length > 0)
                sentences.Add(body);

            i = afterWs - 1; // compensate for loop ++
            start = afterWs;
        }

        if (start < text.Length)
        {
            var tail = text[start..].Trim();
            if (tail.Length > 0)
                sentences.Add(tail);
        }

        return sentences;
    }
}
