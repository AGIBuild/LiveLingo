using System.Text.RegularExpressions;

namespace Dictation.Harness;

public static class Scoring
{
    /// <summary>Character error rate on text without punctuation, spaces or case. Lower is better.</summary>
    public static double Cer(string reference, string hypothesis)
    {
        var r = Strings.Normalize(reference);
        var h = Strings.Normalize(hypothesis);
        if (r.Length == 0) return h.Length == 0 ? 0 : 1;

        var previous = new int[h.Length + 1];
        var current = new int[h.Length + 1];
        for (var j = 0; j <= h.Length; j++) previous[j] = j;
        for (var i = 1; i <= r.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= h.Length; j++)
            {
                var cost = r[i - 1] == h[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return (double)previous[h.Length] / r.Length;
    }

    public static int Hits(string text, IEnumerable<string> patterns) =>
        patterns.Count(p => Regex.IsMatch(text, p, RegexOptions.IgnoreCase));

    public static int ForbiddenHits(string text, IEnumerable<string> patterns) => Hits(text, patterns);

    /// <summary>Share of protected terms present, ignoring case and spacing.</summary>
    public static (int Hit, int Total) Terms(string text, IReadOnlyList<string> terms)
    {
        var normalized = Strings.Normalize(text);
        return (terms.Count(t => normalized.Contains(Strings.Normalize(t), StringComparison.Ordinal)), terms.Count);
    }

    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0) return double.NaN;
        var sorted = values.OrderBy(v => v).ToArray();
        var rank = p * (sorted.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }
}
