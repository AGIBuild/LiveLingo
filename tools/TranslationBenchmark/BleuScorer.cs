namespace TranslationBenchmark;

/// <summary>
/// Corpus-BLEU-4 with brevity penalty. Tokenizes on whitespace + punctuation boundaries.
/// For CJK languages, characters are treated as individual tokens.
/// </summary>
public static class BleuScorer
{
    private static readonly int MaxN = 4;

    public static double Score(string hypothesis, string reference)
    {
        var hyp = Tokenize(hypothesis);
        var refs = Tokenize(reference);

        if (hyp.Count == 0) return 0.0;

        var precisions = new double[MaxN];
        for (int n = 1; n <= MaxN; n++)
        {
            var hypNgrams = GetNgrams(hyp, n);
            if (hypNgrams.Count == 0)
            {
                precisions[n - 1] = 0;
                continue;
            }
            var refNgrams = GetNgramCounts(refs, n);
            int clippedCount = 0;
            foreach (var (ngram, count) in hypNgrams)
                clippedCount += Math.Min(count, refNgrams.GetValueOrDefault(ngram, 0));
            precisions[n - 1] = (double)clippedCount / hypNgrams.Values.Sum();
        }

        // Brevity penalty
        double bp = hyp.Count >= refs.Count
            ? 1.0
            : Math.Exp(1.0 - (double)refs.Count / hyp.Count);

        // Geometric mean of non-zero precisions; if any is 0, BLEU is 0
        double logSum = 0;
        for (int n = 0; n < MaxN; n++)
        {
            if (precisions[n] <= 0) return 0.0;
            logSum += Math.Log(precisions[n]);
        }

        return bp * Math.Exp(logSum / MaxN);
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        foreach (char c in text.ToLowerInvariant())
        {
            if (char.IsWhiteSpace(c)) continue;
            // CJK: treat each character as its own token
            if (c >= 0x4E00 && c <= 0x9FFF ||
                c >= 0x3040 && c <= 0x30FF ||
                c >= 0xAC00 && c <= 0xD7AF)
            {
                tokens.Add(c.ToString());
            }
            else if (char.IsLetterOrDigit(c) || c == '\'')
            {
                if (tokens.Count > 0 && !IsCjkOrPunct(tokens[^1][0]))
                    tokens[^1] += c;
                else
                    tokens.Add(c.ToString());
            }
            else if (!char.IsPunctuation(c) && !char.IsSymbol(c))
            {
                // skip
            }
        }
        return tokens;
    }

    private static bool IsCjkOrPunct(char c) =>
        c >= 0x4E00 && c <= 0x9FFF ||
        c >= 0x3040 && c <= 0x30FF ||
        c >= 0xAC00 && c <= 0xD7AF;

    private static Dictionary<string, int> GetNgrams(List<string> tokens, int n)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i <= tokens.Count - n; i++)
        {
            var gram = string.Join(" ", tokens.Skip(i).Take(n));
            counts[gram] = counts.GetValueOrDefault(gram, 0) + 1;
        }
        return counts;
    }

    private static Dictionary<string, int> GetNgramCounts(List<string> tokens, int n) =>
        GetNgrams(tokens, n);
}
